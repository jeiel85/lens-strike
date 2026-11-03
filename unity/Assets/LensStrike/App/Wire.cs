#nullable enable
// 기기 사이 메시지(NGO 이름 있는 메시지). 방장이 경기 규칙을 돌리고, 참가자는 보고만 보낸다.
// 좌표는 모두 "공유 좌표"(방장 AR 세션 기준)로 주고받는다.
using System.Collections.Generic;
using System.Numerics;
using LensStrike.Core;
using Unity.Collections;
using Unity.Netcode;

namespace LensStrike.App
{
    public static class Msg
    {
        public const string Hello = "ls.hello";       // 참가자→방장: 이름·캐릭터
        public const string Pose = "ls.pose";         // 양방향: 내 폰 위치·정면(공유 좌표)
        public const string Shot = "ls.shot";         // 참가자→방장: 맞혔다는 보고
        public const string Anchor = "ls.anchor";     // 방장→참가자: 방장 기준점
        public const string Snapshot = "ls.snap";     // 방장→참가자: 경기 상태
        public const string Hit = "ls.hit";           // 방장→참가자: 명중 이벤트
        public const string Projectile = "ls.proj";   // 양방향: 날아가는 마법탄(연출용)
        public const string Calibrated = "ls.calib";  // 참가자→방장: 좌표 맞추기 완료 여부
    }

    public struct PoseMsg
    {
        public Vector3 Position;
        public Vector3 Forward;
        public bool Valid;
    }

    public struct ProjectileMsg
    {
        public WeaponId Weapon;
        public Vector3 Origin;
        public Vector3 Direction;
    }

    public static class Wire
    {
        public static void Write(FastBufferWriter w, Vector3 v)
        {
            w.WriteValueSafe(v.X);
            w.WriteValueSafe(v.Y);
            w.WriteValueSafe(v.Z);
        }

        public static Vector3 ReadVec(FastBufferReader r)
        {
            r.ReadValueSafe(out float x);
            r.ReadValueSafe(out float y);
            r.ReadValueSafe(out float z);
            return new Vector3(x, y, z);
        }

        public static FastBufferWriter Pose(PoseMsg p)
        {
            var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(p.Valid);
            Write(w, p.Position);
            Write(w, p.Forward);
            return w;
        }

        public static PoseMsg ReadPose(FastBufferReader r)
        {
            r.ReadValueSafe(out bool valid);
            return new PoseMsg { Valid = valid, Position = ReadVec(r), Forward = ReadVec(r) };
        }

        public static FastBufferWriter Hello(string name, CharacterId character)
        {
            var w = new FastBufferWriter(256, Allocator.Temp);
            w.WriteValueSafe(name);
            w.WriteValueSafe((byte)character);
            return w;
        }

        public static (string name, CharacterId character)? ReadHello(FastBufferReader r)
        {
            r.ReadValueSafe(out string name);
            r.ReadValueSafe(out byte c);
            if (c > (byte)CharacterId.Mage) return null;
            return (CleanName(name), (CharacterId)c);
        }

        public static FastBufferWriter Shot(ShotReport s)
        {
            var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe((byte)s.Weapon);
            w.WriteValueSafe(s.Head);
            w.WriteValueSafe(s.Body);
            w.WriteValueSafe(s.DistanceM);
            w.WriteValueSafe(s.Charge);
            return w;
        }

        public static ShotReport? ReadShot(FastBufferReader r)
        {
            r.ReadValueSafe(out byte weapon);
            r.ReadValueSafe(out int head);
            r.ReadValueSafe(out int body);
            r.ReadValueSafe(out float dist);
            r.ReadValueSafe(out float charge);
            if (weapon > (byte)WeaponId.Waterball) return null;
            return new ShotReport { Weapon = (WeaponId)weapon, Head = head, Body = body, DistanceM = dist, Charge = charge };
        }

        public static FastBufferWriter Anchor(AnchorSample a)
        {
            var w = new FastBufferWriter(32, Allocator.Temp);
            Write(w, a.Position);
            Write(w, a.Forward);
            return w;
        }

        public static AnchorSample ReadAnchor(FastBufferReader r) => new AnchorSample { Position = ReadVec(r), Forward = ReadVec(r) };

        public static FastBufferWriter Projectile(ProjectileMsg p)
        {
            var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe((byte)p.Weapon);
            Write(w, p.Origin);
            Write(w, p.Direction);
            return w;
        }

        public static ProjectileMsg? ReadProjectile(FastBufferReader r)
        {
            r.ReadValueSafe(out byte weapon);
            var o = ReadVec(r);
            var d = ReadVec(r);
            if (weapon > (byte)WeaponId.Waterball) return null;
            return new ProjectileMsg { Weapon = (WeaponId)weapon, Origin = o, Direction = d };
        }

        public static FastBufferWriter Hit(HitEvent e)
        {
            var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(e.From);
            w.WriteValueSafe(e.To);
            w.WriteValueSafe(e.Weapon.HasValue ? (byte)e.Weapon.Value : byte.MaxValue);
            w.WriteValueSafe(e.Damage);
            w.WriteValueSafe((byte)e.Zone);
            w.WriteValueSafe(e.Killed);
            w.WriteValueSafe((byte)e.Effect);
            return w;
        }

        public static HitEvent ReadHit(FastBufferReader r)
        {
            r.ReadValueSafe(out ulong from);
            r.ReadValueSafe(out ulong to);
            r.ReadValueSafe(out byte weapon);
            r.ReadValueSafe(out float damage);
            r.ReadValueSafe(out byte zone);
            r.ReadValueSafe(out bool killed);
            r.ReadValueSafe(out byte effect);
            return new HitEvent
            {
                From = from, To = to, Weapon = weapon == byte.MaxValue ? (WeaponId?)null : (WeaponId)weapon,
                Damage = damage, Zone = (HitZone)zone, Killed = killed, Effect = (EffectKind)effect,
            };
        }

        public static FastBufferWriter Snapshot(Match m)
        {
            var w = new FastBufferWriter(1024, Allocator.Temp);
            w.WriteValueSafe((byte)m.Phase);
            w.WriteValueSafe(m.Options.KillTarget);
            w.WriteValueSafe(m.WinnerId ?? ulong.MaxValue);
            w.WriteValueSafe(m.Count);
            foreach (var p in m.Players)
            {
                w.WriteValueSafe(p.Id);
                w.WriteValueSafe(p.Name);
                w.WriteValueSafe((byte)p.Character);
                w.WriteValueSafe(p.Hp);
                w.WriteValueSafe(p.MaxHp);
                w.WriteValueSafe(p.Kills);
                w.WriteValueSafe(p.Deaths);
                w.WriteValueSafe(p.Alive);
                w.WriteValueSafe(p.RespawnMs);
                w.WriteValueSafe(p.ProtectMs);
                w.WriteValueSafe(p.BurnMs);
                w.WriteValueSafe(p.SoakMs);
                w.WriteValueSafe(p.SoakFactor);
                w.WriteValueSafe(p.ShockMs);
            }
            return w;
        }

        public static Snapshot ReadSnapshot(FastBufferReader r)
        {
            r.ReadValueSafe(out byte phase);
            r.ReadValueSafe(out int killTarget);
            r.ReadValueSafe(out ulong winner);
            r.ReadValueSafe(out int count);
            var s = new Snapshot { Phase = (Phase)phase, KillTarget = killTarget, WinnerId = winner == ulong.MaxValue ? (ulong?)null : winner };
            for (var i = 0; i < count && i < 8; i++)
            {
                var p = new PlayerState();
                r.ReadValueSafe(out p.Id);
                r.ReadValueSafe(out p.Name);
                r.ReadValueSafe(out byte c);
                p.Character = (CharacterId)c;
                r.ReadValueSafe(out p.Hp);
                r.ReadValueSafe(out p.MaxHp);
                r.ReadValueSafe(out p.Kills);
                r.ReadValueSafe(out p.Deaths);
                r.ReadValueSafe(out p.Alive);
                r.ReadValueSafe(out p.RespawnMs);
                r.ReadValueSafe(out p.ProtectMs);
                r.ReadValueSafe(out p.BurnMs);
                r.ReadValueSafe(out p.SoakMs);
                r.ReadValueSafe(out p.SoakFactor);
                r.ReadValueSafe(out p.ShockMs);
                s.Players.Add(p);
            }
            return s;
        }

        public static string CleanName(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "이름없음";
            var chars = new List<char>();
            foreach (var ch in v!) if (!char.IsControl(ch)) chars.Add(ch);
            var s = new string(chars.ToArray()).Trim();
            if (s.Length > 12) s = s.Substring(0, 12);
            return s.Length == 0 ? "이름없음" : s;
        }
    }

    public sealed class Snapshot
    {
        public Phase Phase;
        public int KillTarget;
        public ulong? WinnerId;
        public readonly List<PlayerState> Players = new List<PlayerState>();

        public static Snapshot From(Match m)
        {
            var s = new Snapshot { Phase = m.Phase, KillTarget = m.Options.KillTarget, WinnerId = m.WinnerId };
            foreach (var p in m.Players) s.Players.Add(p.Clone());
            return s;
        }

        public PlayerState? Find(ulong id)
        {
            foreach (var p in Players) if (p.Id == id) return p;
            return null;
        }

        public PlayerState? Other(ulong id)
        {
            foreach (var p in Players) if (p.Id != id) return p;
            return null;
        }
    }
}
