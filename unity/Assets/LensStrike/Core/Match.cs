#nullable enable
// 경기 규칙. 방장(호스트) 기기에서만 돌며 피해·킬·효과·부활을 결정한다.
// 참가자는 "맞혔다"는 보고만 보내고, 숫자는 여기서 계산한다. (웹 버전 src/game/rules.ts와 같은 규칙)
using System;
using System.Collections.Generic;
using System.Linq;

namespace LensStrike.Core
{
    public struct ShotReport
    {
        public WeaponId Weapon;
        /// <summary>머리·몸통에 맞은 알 수(산탄이 아니면 둘 중 하나가 1)</summary>
        public int Head;
        public int Body;
        public float DistanceM;
        /// <summary>충전식 무기의 충전량 0~1</summary>
        public float Charge;
    }

    public enum HitZone : byte { Body, Head, Burn }

    public struct HitEvent
    {
        public ulong From;
        public ulong To;
        /// <summary>화상 지속 피해면 null</summary>
        public WeaponId? Weapon;
        public float Damage;
        public HitZone Zone;
        public bool Killed;
        public EffectKind Effect;
    }

    public sealed class PlayerState
    {
        public ulong Id;
        public string Name = "";
        public CharacterId Character;
        public float Hp;
        public float MaxHp;
        public int Kills;
        public int Deaths;
        public bool Alive = true;
        public float RespawnMs;
        public float ProtectMs;
        public float BurnMs;
        public float BurnDps;
        public ulong? BurnFrom;
        public float SoakMs;
        public float SoakFactor = 1f;
        public float ShockMs;

        public PlayerState Clone() => (PlayerState)MemberwiseClone();
    }

    public enum Phase : byte { Lobby, Playing, Ended }

    public sealed class MatchOptions
    {
        /// <summary>0이면 승패 없음(연습 모드)</summary>
        public int KillTarget = 5;
        public float RespawnMs = 4000;
        /// <summary>부활 직후 무적 시간</summary>
        public float ProtectMs = 1500;
    }

    public sealed class Match
    {
        public Phase Phase { get; set; } = Phase.Lobby;
        public ulong? WinnerId { get; private set; }
        public MatchOptions Options { get; }

        private readonly Dictionary<ulong, PlayerState> players = new Dictionary<ulong, PlayerState>();
        private readonly Dictionary<(ulong, WeaponId), (float tokens, float at)> buckets = new Dictionary<(ulong, WeaponId), (float, float)>();

        public Match(MatchOptions? options = null)
        {
            Options = options ?? new MatchOptions();
        }

        public IReadOnlyCollection<PlayerState> Players => players.Values;
        public int Count => players.Count;
        public bool Has(ulong id) => players.ContainsKey(id);
        public PlayerState? Get(ulong id) => players.TryGetValue(id, out var p) ? p : null;

        public PlayerState AddPlayer(ulong id, string name, CharacterId character)
        {
            var maxHp = Defs.Characters[character].MaxHp;
            var p = new PlayerState { Id = id, Name = name, Character = character, Hp = maxHp, MaxHp = maxHp };
            players[id] = p;
            return p;
        }

        public void RemovePlayer(ulong id)
        {
            players.Remove(id);
            foreach (var key in buckets.Keys.Where(k => k.Item1 == id).ToList()) buckets.Remove(key);
        }

        public void Start()
        {
            Phase = Phase.Playing;
            WinnerId = null;
            buckets.Clear();
            foreach (var p in players.Values) Revive(p, resetScore: true);
        }

        /// <summary>
        /// 맞힌 보고를 검증하고 피해를 적용한다. 받아들이지 않으면 null.
        /// 무기 소유·생존·무적·사거리·연사 속도(토큰 버킷)를 확인한다.
        /// </summary>
        public HitEvent? ApplyShot(ulong shooterId, ulong targetId, ShotReport shot, float nowMs)
        {
            if (Phase != Phase.Playing || shooterId == targetId) return null;
            if (!players.TryGetValue(shooterId, out var shooter) || !players.TryGetValue(targetId, out var target)) return null;
            if (!shooter.Alive || !target.Alive || target.ProtectMs > 0) return null;
            if (!Defs.Weapons.TryGetValue(shot.Weapon, out var weapon)) return null;
            if (Array.IndexOf(Defs.Characters[shooter.Character].Weapons, weapon.Id) < 0) return null;

            var maxHits = weapon.Pellets > 0 ? weapon.Pellets : 1;
            var head = Math.Clamp(shot.Head, 0, maxHits);
            var body = Math.Clamp(shot.Body, 0, maxHits - head);
            if (head + body == 0) return null;

            var range = Defs.RangeFactor(weapon, shot.DistanceM);
            if (range == 0) return null;
            if (!TakeToken(shooterId, weapon, nowMs)) return null;

            var charge = weapon.Mode == FireMode.Charge
                ? (float.IsNaN(shot.Charge) ? weapon.MinCharge : Math.Clamp(shot.Charge, weapon.MinCharge, 1f))
                : 1f;
            var damage = Math.Max(1f, MathF.Round((body + head * weapon.HeadMultiplier) * weapon.Damage * charge * range));

            var effect = weapon.Effect;
            if (effect != null)
            {
                switch (effect.Kind)
                {
                    case EffectKind.Burn:
                        target.BurnMs = effect.DurationMs;
                        target.BurnDps = effect.Dps;
                        target.BurnFrom = shooterId;
                        break;
                    case EffectKind.Soak:
                        target.SoakMs = effect.DurationMs;
                        target.SoakFactor = effect.FireRateFactor;
                        break;
                    case EffectKind.Shock:
                        target.ShockMs = effect.DurationMs;
                        break;
                }
            }

            var killed = Damage(target, damage, shooter);
            return new HitEvent
            {
                From = shooterId,
                To = targetId,
                Weapon = weapon.Id,
                Damage = damage,
                Zone = head > 0 ? HitZone.Head : HitZone.Body,
                Killed = killed,
                Effect = effect?.Kind ?? EffectKind.None,
            };
        }

        /// <summary>시간 경과: 화상 피해, 효과 만료, 부활. 화상 피해 이벤트를 돌려준다.</summary>
        public List<HitEvent> Tick(float dtMs)
        {
            var events = new List<HitEvent>();
            if (Phase != Phase.Playing) return events;
            foreach (var p in players.Values)
            {
                if (!p.Alive)
                {
                    p.RespawnMs -= dtMs;
                    if (p.RespawnMs <= 0) Revive(p, resetScore: false);
                    continue;
                }
                p.ProtectMs = Math.Max(0, p.ProtectMs - dtMs);
                p.SoakMs = Math.Max(0, p.SoakMs - dtMs);
                p.ShockMs = Math.Max(0, p.ShockMs - dtMs);
                if (p.SoakMs == 0) p.SoakFactor = 1f;
                if (p.BurnMs > 0)
                {
                    var step = Math.Min(dtMs, p.BurnMs);
                    p.BurnMs -= step;
                    var amount = p.BurnDps * step / 1000f;
                    var from = p.BurnFrom;
                    var attacker = from.HasValue ? Get(from.Value) : null;
                    var killed = Damage(p, amount, attacker);
                    events.Add(new HitEvent { From = from ?? 0, To = p.Id, Weapon = null, Damage = amount, Zone = HitZone.Burn, Killed = killed });
                    if (p.BurnMs <= 0) p.BurnFrom = null;
                    if (Phase != Phase.Playing) break;
                }
            }
            return events;
        }

        private bool Damage(PlayerState target, float amount, PlayerState? attacker)
        {
            target.Hp = Math.Max(0, target.Hp - amount);
            if (target.Hp > 0) return false;
            target.Alive = false;
            target.Deaths += 1;
            target.RespawnMs = Options.RespawnMs;
            target.BurnMs = 0;
            target.BurnFrom = null;
            target.SoakMs = 0;
            target.SoakFactor = 1f;
            target.ShockMs = 0;
            if (attacker != null && attacker.Id != target.Id)
            {
                attacker.Kills += 1;
                if (Options.KillTarget > 0 && attacker.Kills >= Options.KillTarget)
                {
                    Phase = Phase.Ended;
                    WinnerId = attacker.Id;
                }
            }
            return true;
        }

        private void Revive(PlayerState p, bool resetScore)
        {
            p.Hp = p.MaxHp;
            p.Alive = true;
            p.RespawnMs = 0;
            p.ProtectMs = resetScore ? 0 : Options.ProtectMs;
            p.BurnMs = 0;
            p.BurnDps = 0;
            p.BurnFrom = null;
            p.SoakMs = 0;
            p.SoakFactor = 1f;
            p.ShockMs = 0;
            if (resetScore)
            {
                p.Kills = 0;
                p.Deaths = 0;
            }
        }

        /// <summary>
        /// 무기별 연사 한도. 한 사이클(점사 포함)에 낼 수 있는 발수만큼 다시 차오르고,
        /// 네트워크 지연으로 보고가 몰려 오는 경우를 위해 한 발 여유를 둔다.
        /// </summary>
        private bool TakeToken(ulong shooterId, WeaponDef w, float nowMs)
        {
            var perCycle = Math.Max(1, w.BurstCount);
            var cycleMs = w.FireIntervalMs + (perCycle - 1) * w.BurstGapMs + w.ChargeMs * w.MinCharge;
            var capacity = perCycle + 1f;
            var key = (shooterId, w.Id);
            var (tokens, at) = buckets.TryGetValue(key, out var b) ? b : (capacity, nowMs);
            tokens = Math.Min(capacity, tokens + (nowMs - at) * perCycle / cycleMs);
            if (tokens < 1)
            {
                buckets[key] = (tokens, nowMs);
                return false;
            }
            buckets[key] = (tokens - 1, nowMs);
            return true;
        }
    }
}
