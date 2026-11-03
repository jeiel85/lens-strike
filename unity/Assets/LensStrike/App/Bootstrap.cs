#nullable enable
// 앱 시작점. 화면을 만들고 설정 → (연습 | 방 만들기 | 참가) → 대기실(좌표 맞추기) → 경기 → 결과 흐름을 잇는다.
using System;
using System.Collections;
using System.Threading.Tasks;
using LensStrike.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace LensStrike.App
{
    public sealed class Bootstrap : MonoBehaviour
    {
        public Camera ArCamera = null!;

        private Canvas canvas = null!;
        private CanvasScaler scaler = null!;
        private ArWorld ar = null!;
        private Sfx sfx = null!;
        private Hud hud = null!;
        private SetupScreen setup = null!;
        private LobbyScreen lobby = null!;
        private ResultScreen result = null!;
        private Text toast = null!;
        private float toastUntil;
        private GamePlay? play;
        private AnchorSample? hostAnchor;
        private bool guestCalibrated;
        private bool busy;

        private void Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;

            ar = gameObject.AddComponent<ArWorld>();
            ar.Cam = ArCamera;
            sfx = gameObject.AddComponent<Sfx>();
            BuildUi();
            StartCoroutine(CheckAr());
        }

        private void BuildUi()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var go = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // 안전 영역(노치·내비게이션 바) 안쪽에 그린다
            var safe = Ui.Rect("Safe", go.transform);
            Ui.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();

            hud = new Hud(safe);
            hud.Show(false);
            hud.ExitPressed = Leave;
            setup = new SetupScreen(safe);
            lobby = new LobbyScreen(safe);
            result = new ResultScreen(safe);
            lobby.Show(false);
            result.Show(false);

            toast = Ui.Text("Toast", safe, "", 32, Color.white);
            Ui.Place(toast.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(1000, 120));
            toast.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);

            // 연결을 시작할 때의 캐릭터를 끝까지 쓴다(연결 중에 카드를 눌러도 방장 기록과 내 무기가 어긋나지 않게)
            setup.Practice = () => Run(c => Task.FromResult<GameLink>(new PracticeLink(setup.PlayerName, c)));
            setup.Host = () => Run(async c => await NetHostLink.OpenAsync(setup.PlayerName, c));
            setup.Join = code =>
            {
                if (code.Length < 4)
                {
                    setup.SetStatus("참가 코드를 입력해 주세요.", true);
                    return;
                }
                Run(async c => await NetGuestLink.JoinAsync(code, setup.PlayerName, c));
            };
            lobby.Leave = Leave;
            lobby.SetAnchor = SaveAnchor;
            lobby.Start = () => (play?.Link as HostSide)?.StartMatch();
            result.Leave = Leave;
            result.Rematch = () => (play?.Link as HostSide)?.StartMatch();
        }

        /// <summary>
        /// AR 세션은 ARSession 컴포넌트가 스스로 시작한다(필요하면 Google Play AR 서비스 설치까지).
        /// 여기서는 결과만 기다려 안내한다. 시작에 실패하면 ARSession이 스스로 꺼진다.
        /// </summary>
        private IEnumerator CheckAr()
        {
            setup.SetStatus("AR을 준비하는 중…");
            var session = FindFirstObjectByType<ARSession>();
            var deadline = Time.unscaledTime + 20f;
            while (Time.unscaledTime < deadline)
            {
                if (ARSession.state >= ARSessionState.SessionInitializing)
                {
                    setup.SetStatus("준비 완료");
                    yield break;
                }
                if (ARSession.state == ARSessionState.Unsupported || session == null || !session.enabled)
                {
                    setup.SetStatus("이 기기는 AR(Google Play AR 서비스)을 지원하지 않습니다.", true);
                    setup.SetBusy(true);
                    yield break;
                }
                yield return null;
            }
            setup.SetStatus("AR을 시작하지 못했습니다. 카메라 권한과 Google Play AR 서비스를 확인해 주세요.", true);
        }

        private async void Run(Func<CharacterId, Task<GameLink>> open)
        {
            if (busy) return;
            busy = true;
            var character = setup.Character;
            setup.SetBusy(true);
            setup.SetStatus("연결하는 중…");
            try
            {
                var link = await open(character);
                Begin(link, character);
                setup.SetStatus("준비 완료");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                setup.SetStatus(e is TimeoutException ? e.Message : Services.Explain(e), true);
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            }
            finally
            {
                busy = false;
                setup.SetBusy(false);
            }
        }

        private void Begin(GameLink link, CharacterId character)
        {
            hostAnchor = null;
            guestCalibrated = false;
            if (!link.IsPractice) ar.ResetCalibration();
            play = new GamePlay(link, character, ar, hud, sfx, canvas);
            play.PhaseChanged = OnPhase;
            play.Updated = _ => RenderLobby();
            link.Status += (message, fatal) =>
            {
                if (fatal) Leave();
                Toast(message);
            };
            link.HostAnchorReceived += a =>
            {
                hostAnchor = a;
                if (ar.Calibrated)
                {
                    // 방장이 기준을 다시 저장했으면 다시 맞춰야 한다
                    ar.ResetCalibration();
                    (link as NetGuestLink)?.SendCalibrated(false);
                    Toast("방장이 기준을 다시 정했습니다. 다시 맞춰 주세요.");
                }
                RenderLobby();
            };
            link.GuestCalibrationChanged += ok =>
            {
                guestCalibrated = ok;
                if (ok) Toast("상대가 좌표를 맞췄습니다.");
                RenderLobby();
            };
            setup.Show(false);
            if (link.IsPractice)
            {
                lobby.Show(false);
            }
            else
            {
                lobby.Show(true);
                RenderLobby();
            }
        }

        private void SaveAnchor()
        {
            if (play == null) return;
            if (!ar.Tracking)
            {
                Toast("주변을 천천히 비춰 위치 추적이 잡힌 뒤 눌러 주세요.");
                return;
            }
            var sample = ar.LocalSample();
            if (play.Link is NetHostLink host)
            {
                ar.SetAsHostFrame();
                host.SetAnchor(sample);
                hostAnchor = sample;
                Toast("기준을 저장했습니다.");
            }
            else if (play.Link is NetGuestLink guest && hostAnchor.HasValue)
            {
                ar.CalibrateTo(hostAnchor.Value, sample);
                guest.SendCalibrated(true);
                Toast("좌표를 맞췄습니다.");
            }
            RenderLobby();
        }

        private void RenderLobby()
        {
            if (play == null || play.Link.IsPractice) return;
            lobby.Render(play.Link, play.Snapshot, ar.Calibrated, guestCalibrated, hostAnchor.HasValue);
        }

        private void OnPhase(Snapshot s)
        {
            if (play == null) return;
            switch (s.Phase)
            {
                case Phase.Playing:
                    lobby.Show(false);
                    result.Show(false);
                    if (!play.Link.IsPractice) Toast("시작! 상대를 조준점 가운데 두고 화면을 터치하세요.");
                    break;
                case Phase.Lobby:
                    if (!play.Link.IsPractice)
                    {
                        result.Show(false);
                        lobby.Show(true);
                        RenderLobby();
                    }
                    break;
                case Phase.Ended:
                    result.Render(play.Link, s);
                    result.Show(true);
                    break;
            }
        }

        private void Leave()
        {
            play?.Dispose();
            play = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            ar.ResetCalibration();
            lobby.Show(false);
            result.Show(false);
            setup.Show(true);
        }

        private void Toast(string text)
        {
            toast.text = text;
            toastUntil = Time.unscaledTime + 3f;
        }

        private void Update()
        {
            // 짧은 변 기준으로 크기를 맞춰 세로·가로에서 같은 크기로 보이게 한다
            scaler.matchWidthOrHeight = Screen.width > Screen.height ? 1f : 0f;
            scaler.referenceResolution = Screen.width > Screen.height ? new Vector2(1920, 1080) : new Vector2(1080, 1920);
            toast.gameObject.SetActive(Time.unscaledTime < toastUntil);
            var landscape = Screen.width > Screen.height;
            setup.Sheet.Layout(landscape);
            lobby.Sheet.Layout(landscape);
            result.Sheet.Layout(landscape);
            play?.Update();
        }

        private void OnDestroy() => play?.Dispose();
    }

    /// <summary>화면 안전 영역(노치·둥근 모서리·내비게이션 바)에 맞춰 UI 영역을 줄인다</summary>
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect last;

        private void Update()
        {
            var area = Screen.safeArea;
            if (area == last || Screen.width == 0) return;
            last = area;
            var r = (RectTransform)transform;
            r.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            r.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
    }
}
