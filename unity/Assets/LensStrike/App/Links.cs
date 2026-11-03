#nullable enable
// 경기 연결. 방장은 경기 규칙(Match)을 직접 돌리고, 참가자는 방장에게 붙는다.
// 연습 모드는 네트워크 없이 같은 규칙에 훈련 표적을 넣어 돌린다.
// 기기 간 연결은 Unity Multiplayer Services 세션(Relay, 참가 코드) + Netcode for GameObjects.
using System;
using System.Threading.Tasks;
using LensStrike.Core;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace LensStrike.App
{
    public abstract class GameLink
    {
        public abstract ulong MyId { get; }
        public abstract bool IsHost { get; }
        public virtual bool IsPractice => false;
        public virtual string? JoinCode => null;

        public event Action<Snapshot>? SnapshotReceived;
        public event Action<HitEvent>? HitReceived;
        /// <summary>연결 상태 안내. 두 번째 값이 true면 경기를 이어갈 수 없다.</summary>
        public event Action<string, bool>? Status;
        public event Action<PoseMsg>? OpponentPose;
        public event Action<ProjectileMsg>? OpponentProjectile;
        public event Action<AnchorSample>? HostAnchorReceived;
        public event Action<bool>? GuestCalibrationChanged;

        public abstract void Shoot(ShotReport shot);
        public virtual void SendPose(PoseMsg pose) { }
        public virtual void SendProjectile(ProjectileMsg p) { }
        public virtual void Tick(float dtMs) { }
        public abstract void Close();

        protected void RaiseSnapshot(Snapshot s) => SnapshotReceived?.Invoke(s);
        protected void RaiseHit(HitEvent e) => HitReceived?.Invoke(e);
        protected void RaiseStatus(string m, bool fatal) => Status?.Invoke(m, fatal);
        protected void RaisePose(PoseMsg p) => OpponentPose?.Invoke(p);
        protected void RaiseProjectile(ProjectileMsg p) => OpponentProjectile?.Invoke(p);
        protected void RaiseAnchor(AnchorSample a) => HostAnchorReceived?.Invoke(a);
        protected void RaiseGuestCalibration(bool ok) => GuestCalibrationChanged?.Invoke(ok);
    }

    /// <summary>방장 쪽 공통: 경기 규칙을 돌리고 결과를 알린다.</summary>
    public abstract class HostSide : GameLink
    {
        public readonly Match Match;
        private float tickAccum;
        private const float TickMs = 100f;

        protected HostSide(MatchOptions options) => Match = new Match(options);

        public override bool IsHost => true;
        public override ulong MyId => NetworkManager.ServerClientId;

        public virtual void StartMatch()
        {
            Match.Start();
            Broadcast();
        }

        public override void Shoot(ShotReport shot) => Resolve(MyId, shot);

        protected void Resolve(ulong shooterId, ShotReport shot)
        {
            var target = OpponentOf(shooterId);
            if (target == null) return;
            var e = Match.ApplyShot(shooterId, target.Id, shot, Time.realtimeSinceStartup * 1000f);
            if (e.HasValue) Emit(e.Value);
            Broadcast();
        }

        protected PlayerState? OpponentOf(ulong id)
        {
            foreach (var p in Match.Players) if (p.Id != id) return p;
            return null;
        }

        public override void Tick(float dtMs)
        {
            tickAccum += dtMs;
            if (tickAccum < TickMs) return;
            var dt = tickAccum;
            tickAccum = 0;
            foreach (var e in Match.Tick(dt)) Emit(e);
            Broadcast();
        }

        protected virtual void Emit(HitEvent e) => RaiseHit(e);
        protected virtual void Broadcast() => RaiseSnapshot(Snapshot.From(Match));
    }

    public sealed class PracticeLink : HostSide
    {
        public const ulong DummyId = 999;
        public override bool IsPractice => true;

        public PracticeLink(string name, CharacterId character)
            : base(new MatchOptions { KillTarget = 0, RespawnMs = 1500, ProtectMs = 0 })
        {
            Match.AddPlayer(MyId, name, character);
            Match.AddPlayer(DummyId, "훈련 표적", CharacterId.Ranger);
            Match.Start();
        }

        public override void Close() { }
    }

    /// <summary>Relay 세션을 만들고(방장) 들어가는(참가자) 공통 준비</summary>
    internal static class Services
    {
        public static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public static string Explain(Exception e) => e switch
        {
            SessionException se when se.Error == SessionError.SessionNotFound => "방을 찾을 수 없습니다. 코드를 다시 확인해 주세요.",
            SessionException se when se.Error == SessionError.Unknown && se.Message.Contains("full", StringComparison.OrdinalIgnoreCase) => "이미 두 명이 들어가 있는 방입니다.",
            RequestFailedException => "연결 서버와 통신하지 못했습니다. 인터넷 연결을 확인해 주세요.",
            _ => $"연결 중 오류가 났습니다: {e.Message}",
        };
    }

    public sealed class NetHostLink : HostSide
    {
        private readonly ISession session;
        private ulong? guestId;
        private AnchorSample? anchor;
        public bool GuestCalibrated { get; private set; }

        public override string? JoinCode => session.Code;
        public bool GuestConnected => guestId.HasValue && Match.Has(guestId.Value);

        private NetHostLink(ISession session) : base(new MatchOptions())
        {
            this.session = session;
            var nm = NetworkManager.Singleton;
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler(Msg.Hello, OnHello);
            cmm.RegisterNamedMessageHandler(Msg.Pose, (sender, r) => { if (sender == guestId) RaisePose(Wire.ReadPose(r)); });
            cmm.RegisterNamedMessageHandler(Msg.Shot, (sender, r) =>
            {
                var s = Wire.ReadShot(r);
                if (s.HasValue && sender == guestId) Resolve(sender, s.Value);
            });
            cmm.RegisterNamedMessageHandler(Msg.Projectile, (sender, r) =>
            {
                var p = Wire.ReadProjectile(r);
                if (p.HasValue && sender == guestId) RaiseProjectile(p.Value);
            });
            cmm.RegisterNamedMessageHandler(Msg.Calibrated, (sender, r) =>
            {
                r.ReadValueSafe(out bool ok);
                if (sender != guestId) return;
                GuestCalibrated = ok;
                RaiseGuestCalibration(ok);
            });
            nm.OnClientDisconnectCallback += OnDisconnect;
        }

        public static async Task<NetHostLink> OpenAsync(string name, CharacterId character)
        {
            await Services.EnsureSignedInAsync();
            var options = new SessionOptions { MaxPlayers = 2, IsPrivate = true }.WithRelayNetwork();
            var session = await MultiplayerService.Instance.CreateSessionAsync(options);
            var link = new NetHostLink(session);
            link.Match.AddPlayer(link.MyId, name, character);
            return link;
        }

        /// <summary>방장 기준점을 정하고 참가자에게 알린다. 방장 좌표가 곧 공유 좌표다.</summary>
        public void SetAnchor(AnchorSample a)
        {
            anchor = a;
            GuestCalibrated = false;
            RaiseGuestCalibration(false);
            SendTo(Msg.Anchor, Wire.Anchor(a), NetworkDelivery.ReliableSequenced);
        }

        public bool HasAnchor => anchor.HasValue;

        public override void SendPose(PoseMsg pose) => SendTo(Msg.Pose, Wire.Pose(pose), NetworkDelivery.UnreliableSequenced);
        public override void SendProjectile(ProjectileMsg p) => SendTo(Msg.Projectile, Wire.Projectile(p), NetworkDelivery.Reliable);

        protected override void Emit(HitEvent e)
        {
            base.Emit(e);
            SendTo(Msg.Hit, Wire.Hit(e), NetworkDelivery.ReliableSequenced);
        }

        protected override void Broadcast()
        {
            base.Broadcast();
            SendTo(Msg.Snapshot, Wire.Snapshot(Match), NetworkDelivery.ReliableSequenced);
        }

        private void SendTo(string name, FastBufferWriter w, NetworkDelivery delivery)
        {
            using (w)
            {
                if (!guestId.HasValue) return;
                var nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsListening) return;
                nm.CustomMessagingManager.SendNamedMessage(name, guestId.Value, w, delivery);
            }
        }

        private void OnHello(ulong sender, FastBufferReader r)
        {
            var hello = Wire.ReadHello(r);
            if (!hello.HasValue || sender == MyId) return;
            if (guestId.HasValue && guestId != sender) return; // 두 명까지만
            // 경기 중에 다시 입장 메시지를 보내 HP를 되살리는 것을 막는다
            if (Match.Has(sender) && Match.Phase != Phase.Lobby) return;
            guestId = sender;
            // 새로 들어온 참가자는 아직 좌표를 맞추지 않았다
            GuestCalibrated = false;
            RaiseGuestCalibration(false);
            if (Match.Has(sender)) Match.RemovePlayer(sender);
            Match.AddPlayer(sender, hello.Value.name, hello.Value.character);
            if (anchor.HasValue) SendTo(Msg.Anchor, Wire.Anchor(anchor.Value), NetworkDelivery.ReliableSequenced);
            Broadcast();
        }

        private void OnDisconnect(ulong clientId)
        {
            if (clientId != guestId) return;
            Match.RemovePlayer(clientId);
            guestId = null;
            GuestCalibrated = false;
            RaiseGuestCalibration(false);
            if (Match.Phase != Phase.Lobby) Match.Phase = Phase.Lobby;
            RaiseStatus("상대가 나갔습니다. 다른 사람이 들어오기를 기다립니다.", false);
            Broadcast();
        }

        public override void Close()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientDisconnectCallback -= OnDisconnect;
                foreach (var n in new[] { Msg.Hello, Msg.Pose, Msg.Shot, Msg.Projectile, Msg.Calibrated })
                    nm.CustomMessagingManager?.UnregisterNamedMessageHandler(n);
            }
            _ = session.LeaveAsync();
        }
    }

    public sealed class NetGuestLink : GameLink
    {
        private readonly ISession session;
        private bool closing;

        public override bool IsHost => false;
        public override ulong MyId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        private NetGuestLink(ISession session)
        {
            this.session = session;
            var nm = NetworkManager.Singleton;
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler(Msg.Snapshot, (_, r) => RaiseSnapshot(Wire.ReadSnapshot(r)));
            cmm.RegisterNamedMessageHandler(Msg.Hit, (_, r) => RaiseHit(Wire.ReadHit(r)));
            cmm.RegisterNamedMessageHandler(Msg.Pose, (_, r) => RaisePose(Wire.ReadPose(r)));
            cmm.RegisterNamedMessageHandler(Msg.Anchor, (_, r) => RaiseAnchor(Wire.ReadAnchor(r)));
            cmm.RegisterNamedMessageHandler(Msg.Projectile, (_, r) =>
            {
                var p = Wire.ReadProjectile(r);
                if (p.HasValue) RaiseProjectile(p.Value);
            });
            nm.OnClientDisconnectCallback += OnDisconnect;
        }

        public static async Task<NetGuestLink> JoinAsync(string code, string name, CharacterId character)
        {
            await Services.EnsureSignedInAsync();
            var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            var nm = NetworkManager.Singleton;
            // 세션이 클라이언트 연결을 시작한다. 연결이 끝날 때까지 기다린다.
            var deadline = Time.realtimeSinceStartup + 15f;
            while (!nm.IsConnectedClient)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    _ = session.LeaveAsync();
                    throw new TimeoutException("방장에게 연결하지 못했습니다. 잠시 뒤 다시 시도해 주세요.");
                }
                await Task.Yield();
            }
            var link = new NetGuestLink(session);
            using (var w = Wire.Hello(name, character))
                nm.CustomMessagingManager.SendNamedMessage(Msg.Hello, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
            return link;
        }

        public override void Shoot(ShotReport shot) => SendToHost(Msg.Shot, Wire.Shot(shot), NetworkDelivery.ReliableSequenced);
        public override void SendPose(PoseMsg pose) => SendToHost(Msg.Pose, Wire.Pose(pose), NetworkDelivery.UnreliableSequenced);
        public override void SendProjectile(ProjectileMsg p) => SendToHost(Msg.Projectile, Wire.Projectile(p), NetworkDelivery.Reliable);

        public void SendCalibrated(bool ok)
        {
            var w = new FastBufferWriter(4, Unity.Collections.Allocator.Temp);
            w.WriteValueSafe(ok);
            SendToHost(Msg.Calibrated, w, NetworkDelivery.ReliableSequenced);
        }

        private static void SendToHost(string name, FastBufferWriter w, NetworkDelivery delivery)
        {
            using (w)
            {
                var nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsConnectedClient) return;
                nm.CustomMessagingManager.SendNamedMessage(name, NetworkManager.ServerClientId, w, delivery);
            }
        }

        private void OnDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (closing || nm == null || (clientId != nm.LocalClientId && clientId != NetworkManager.ServerClientId)) return;
            RaiseStatus("방장과 연결이 끊겼습니다.", true);
        }

        public override void Close()
        {
            closing = true;
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientDisconnectCallback -= OnDisconnect;
                foreach (var n in new[] { Msg.Snapshot, Msg.Hit, Msg.Pose, Msg.Anchor, Msg.Projectile })
                    nm.CustomMessagingManager?.UnregisterNamedMessageHandler(n);
            }
            _ = session.LeaveAsync();
        }
    }
}
