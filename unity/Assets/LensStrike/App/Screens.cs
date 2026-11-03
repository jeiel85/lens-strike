#nullable enable
// 경기 밖 화면: 설정(닉네임·캐릭터·연습/방 만들기/참가), 대기실(참가 코드·좌표 맞추기·시작), 결과.
using System;
using System.Collections.Generic;
using LensStrike.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LensStrike.App
{
    /// <summary>세로로 쌓이는 스크롤 패널. 가로 화면처럼 높이가 모자라면 스크롤된다.</summary>
    public sealed class Sheet
    {
        public readonly RectTransform Root;
        public readonly RectTransform Content;
        private readonly RectTransform panel;

        public Sheet(Transform parent, string name, bool dimBackground, bool bottom)
        {
            var bg = Ui.Image(name, parent, dimBackground ? new Color(0.02f, 0.03f, 0.04f, 0.97f) : new Color(0, 0, 0, 0), raycast: dimBackground);
            Root = Ui.Stretch(bg.rectTransform);
            var p = Ui.Image("Panel", Root, Ui.Panel, raycast: true);
            panel = p.rectTransform;
            if (bottom)
            {
                // 대기실은 화면 아래쪽만 덮어서, 가운데 조준선으로 상대를 비춰 볼 수 있게 한다
                panel.anchorMin = new Vector2(0, 0);
                panel.anchorMax = new Vector2(1, 0.46f);
                panel.offsetMin = new Vector2(24, 24);
                panel.offsetMax = new Vector2(-24, 0);
            }
            else
            {
                Ui.Stretch(panel, 40, 80, 40, 80);
            }
            var viewport = Ui.Rect("Viewport", panel);
            Ui.Stretch(viewport, 40, 40, 40, 40);
            viewport.gameObject.AddComponent<RectMask2D>();
            Content = Ui.Rect("Content", viewport);
            Content.anchorMin = new Vector2(0, 1);
            Content.anchorMax = new Vector2(1, 1);
            Content.pivot = new Vector2(0.5f, 1);
            Content.offsetMin = Content.offsetMax = Vector2.zero;
            var layout = Content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 26;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.content = Content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        public void Show(bool on) => Root.gameObject.SetActive(on);

        public T Add<T>(T component, float height) where T : Component
        {
            var le = component.gameObject.GetComponent<LayoutElement>() ?? component.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return component;
        }

        public Text Label(string text, int size, Color color, float height, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = Ui.Text("Label", Content, text, size, color, align, style);
            return Add(t, height);
        }

        /// <summary>가로로 나란히 놓는 줄</summary>
        public RectTransform Row(float height, float spacing = 20)
        {
            var r = Ui.Rect("Row", Content);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return r;
        }
    }

    public sealed class SetupScreen
    {
        public readonly Sheet Sheet;
        public Action? Practice, Host;
        public Action<string>? Join;
        public CharacterId Character { get; private set; }
        private readonly InputField name, code;
        private readonly Text status;
        private readonly List<(CharacterId id, Image bg)> cards = new List<(CharacterId, Image)>();
        private readonly List<Button> actions = new List<Button>();

        public SetupScreen(Transform canvas)
        {
            Sheet = new Sheet(canvas, "Setup", dimBackground: true, bottom: false);
            Sheet.Label("LENS STRIKE AR", 76, Ui.Accent, 110, FontStyle.Bold);
            Sheet.Label("렌즈 스트라이크 · 서로의 위치를 AR로 공유해 쏘는 대전", 32, Ui.Muted, 50);

            Sheet.Label("캐릭터", 30, Ui.Muted, 44);
            Character = (CharacterId)Mathf.Clamp(PlayerPrefs.GetInt("ls.character", 0), 0, 2);
            foreach (var def in Defs.Characters.Values)
            {
                var id = def.Id;
                var card = Ui.Button(def.Name, Sheet.Content, "", new Color(1, 1, 1, 0.06f), Color.white, () => SelectCharacter(id));
                var label = card.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = FontStyle.Normal;
                label.fontSize = 30;
                label.supportRichText = true;
                var weapons = string.Join(" · ", Array.ConvertAll(def.Weapons, w => Defs.Weapons[w].Name));
                label.text = $"<b><color={def.ColorHex}><size=40>{def.Name}</size></color></b>  <color=#9aa3b2>HP {def.MaxHp}</color>\n<b>{weapons}</b>\n<color=#9aa3b2>{def.Tagline}</color>";
                Ui.Stretch(label.rectTransform, 30, 10, 30, 10);
                Sheet.Add(card, 190);
                cards.Add((id, card.image));
            }
            SelectCharacter(Character);

            Sheet.Label("닉네임", 30, Ui.Muted, 44);
            name = Sheet.Add(Ui.Input("Name", Sheet.Content, "닉네임", 12), 100);
            name.text = PlayerPrefs.GetString("ls.name", "");

            var row = Sheet.Row(110);
            actions.Add(Ui.Button("Practice", row, "혼자 연습", new Color(1, 1, 1, 0.1f), Color.white, () => Practice?.Invoke()));
            actions.Add(Ui.Button("Host", row, "방 만들기", Ui.Accent, new Color(0.1f, 0.08f, 0.02f), () => Host?.Invoke()));
            var joinRow = Sheet.Row(110);
            code = Ui.Input("Code", joinRow, "참가 코드", 12);
            code.characterValidation = InputField.CharacterValidation.Alphanumeric;
            code.onValueChanged.AddListener(v =>
            {
                var up = v.ToUpperInvariant();
                if (up != v) code.text = up;
            });
            var join = Ui.Button("Join", joinRow, "참가", Ui.Accent, new Color(0.1f, 0.08f, 0.02f), () => Join?.Invoke(code.text.Trim()));
            join.gameObject.AddComponent<LayoutElement>().preferredWidth = 220;
            code.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            join.GetComponent<LayoutElement>().flexibleWidth = 0;
            actions.Add(join);

            status = Sheet.Label("", 30, Ui.Muted, 90);
            Sheet.Label(
                "· 주변을 확인하고, 도로 근처·사람 많은 곳에서는 하지 마세요.\n" +
                "· 카메라 영상은 이 기기 안에서만 위치 추적에 쓰고, 다른 기기에는 위치·방향과 게임 정보만 보냅니다.\n" +
                "· 대전은 두 사람이 같은 자리에서 좌표를 맞춘 뒤 시작합니다.",
                26, Ui.Muted, 190);
        }

        public string PlayerName
        {
            get
            {
                var n = Wire.CleanName(name.text);
                PlayerPrefs.SetString("ls.name", n);
                return n;
            }
        }

        private void SelectCharacter(CharacterId id)
        {
            Character = id;
            PlayerPrefs.SetInt("ls.character", (int)id);
            foreach (var (cid, bg) in cards)
                bg.color = cid == id ? Ui.Hex(Defs.Characters[cid].ColorHex) * new Color(1, 1, 1, 0.28f) : new Color(1, 1, 1, 0.06f);
        }

        public void SetStatus(string text, bool error = false)
        {
            status.text = text;
            status.color = error ? Ui.Danger : Ui.Muted;
        }

        public void SetBusy(bool busy)
        {
            foreach (var b in actions) b.interactable = !busy;
        }

        public void Show(bool on) => Sheet.Show(on);
    }

    public sealed class LobbyScreen
    {
        public readonly Sheet Sheet;
        public Action? Leave, Start, SetAnchor;
        private readonly Text title, codeText, players, steps, status;
        private readonly Button start, anchor;

        public LobbyScreen(Transform canvas)
        {
            Sheet = new Sheet(canvas, "Lobby", dimBackground: false, bottom: true);
            title = Sheet.Label("", 40, Color.white, 60, FontStyle.Bold);
            codeText = Sheet.Label("", 64, Ui.Accent, 84, FontStyle.Bold);
            players = Sheet.Label("", 30, Color.white, 90);
            steps = Sheet.Label("", 30, Color.white, 150);
            anchor = Sheet.Add(Ui.Button("Anchor", Sheet.Content, "여기가 기준 (조준점을 먼 물체에 맞추고)", Ui.Hex("#4cc9f0"), new Color(0.02f, 0.08f, 0.1f), () => SetAnchor?.Invoke(), 34), 110);
            status = Sheet.Label("", 28, Ui.Muted, 50);
            var row = Sheet.Row(110);
            Ui.Button("Leave", row, "나가기", new Color(1, 1, 1, 0.1f), Color.white, () => Leave?.Invoke());
            start = Ui.Button("Start", row, "시작", Ui.Accent, new Color(0.1f, 0.08f, 0.02f), () => Start?.Invoke());
        }

        public void Render(GameLink link, Snapshot? s, bool calibrated, bool guestCalibrated, bool hostAnchorKnown)
        {
            var host = link.IsHost;
            title.text = host ? "방을 열었습니다" : "방에 들어왔습니다";
            codeText.gameObject.SetActive(host && link.JoinCode != null);
            codeText.text = $"참가 코드  {link.JoinCode}";
            var list = new List<string>();
            if (s != null)
                foreach (var p in s.Players)
                    list.Add($"{p.Name}{(p.Id == link.MyId ? " (나)" : "")} · {Defs.Characters[p.Character].Name}{(p.Id == Unity.Netcode.NetworkManager.ServerClientId ? " · 방장" : "")}");
            players.text = list.Count > 0 ? string.Join("\n", list) : "";
            var full = s != null && s.Players.Count == 2;

            if (host)
            {
                steps.text = !calibrated
                    ? "① 둘이 같이 쓸 기준 자리를 정하세요(바닥 타일 한 칸 등).\n② 그 자리에 서서, 멀리 있는 물체(창틀·간판 등)를 조준점 가운데 두고 아래 버튼을 누르세요."
                    : !full ? "기준을 저장했습니다. 상대가 코드로 들어오기를 기다리는 중…"
                    : !guestCalibrated ? "자리를 비켜 주세요. 상대가 같은 자리에서 같은 물체를 겨누고 기준을 맞추는 중…"
                    : "좌표를 맞췄습니다. 서로 떨어져서 시작하세요!";
                anchor.gameObject.SetActive(true);
                Ui.SetLabel(anchor, calibrated ? "기준 다시 저장" : "여기가 기준");
            }
            else
            {
                steps.text = !hostAnchorKnown
                    ? "방장이 기준 자리를 정하는 중입니다…"
                    : !calibrated
                        ? "방장이 섰던 자리에 서서, 방장이 겨눈 같은 물체를 조준점 가운데 두고 아래 버튼을 누르세요."
                        : "좌표를 맞췄습니다. 방장이 시작하기를 기다리는 중… (방장 머리 위에 이름이 보이면 성공)";
                anchor.gameObject.SetActive(hostAnchorKnown);
                Ui.SetLabel(anchor, calibrated ? "기준 다시 맞추기" : "여기가 기준");
            }
            start.gameObject.SetActive(host);
            start.interactable = host && full && calibrated && guestCalibrated;
        }

        public void SetStatus(string text) => status.text = text;
        public void Show(bool on) => Sheet.Show(on);
    }

    public sealed class ResultScreen
    {
        public readonly Sheet Sheet;
        public Action? Leave, Rematch;
        private readonly Text title, score, wait;
        private readonly Button rematch;

        public ResultScreen(Transform canvas)
        {
            Sheet = new Sheet(canvas, "Result", dimBackground: false, bottom: true);
            title = Sheet.Label("", 90, Ui.Accent, 130, FontStyle.Bold, TextAnchor.MiddleCenter);
            score = Sheet.Label("", 48, Color.white, 80, FontStyle.Bold, TextAnchor.MiddleCenter);
            wait = Sheet.Label("", 30, Ui.Muted, 50, align: TextAnchor.MiddleCenter);
            var row = Sheet.Row(110);
            Ui.Button("Leave", row, "나가기", new Color(1, 1, 1, 0.1f), Color.white, () => Leave?.Invoke());
            rematch = Ui.Button("Rematch", row, "다시 하기", Ui.Accent, new Color(0.1f, 0.08f, 0.02f), () => Rematch?.Invoke());
        }

        public void Render(GameLink link, Snapshot s)
        {
            var me = s.Find(link.MyId);
            var opp = s.Other(link.MyId);
            var won = s.WinnerId == link.MyId;
            title.text = won ? "승리!" : "패배";
            title.color = won ? Ui.Accent : Ui.Danger;
            score.text = $"{me?.Name ?? "나"} {me?.Kills ?? 0} : {opp?.Kills ?? 0} {opp?.Name ?? "상대"}";
            rematch.gameObject.SetActive(link.IsHost);
            wait.text = link.IsHost ? "" : "방장이 다시 시작하기를 기다리는 중…";
        }

        public void Show(bool on) => Sheet.Show(on);
    }
}
