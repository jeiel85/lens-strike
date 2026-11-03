#nullable enable
// 내 기기의 방아쇠 상태: 탄약, 재장전, 연사·점사·충전. 시간을 인자로 받아 테스트할 수 있게 한다.
// (웹 버전 src/game/gun.ts와 같은 동작)
using System;
using System.Collections.Generic;

namespace LensStrike.Core
{
    public enum GunEventKind : byte { Fire, Empty, ReloadStart, ReloadDone }

    public struct GunEvent
    {
        public GunEventKind Kind;
        public WeaponId Weapon;
        public float Charge;

        public override string ToString() => $"{Kind}:{Weapon}:{Charge:0.##}";
    }

    public struct GunConditions
    {
        /// <summary>젖음 등으로 느려진 사격 간격 배율</summary>
        public float SlowFactor;
        /// <summary>감전·사망 등으로 쏠 수 없음</summary>
        public bool Blocked;

        public static GunConditions Normal => new GunConditions { SlowFactor = 1f, Blocked = false };
    }

    public sealed class Gun
    {
        public WeaponId Current { get; private set; }
        public IReadOnlyList<WeaponId> Weapons { get; }
        private readonly Dictionary<WeaponId, int> ammo = new Dictionary<WeaponId, int>();
        private float reloadUntil;
        private float reloadStartedAt;
        private float nextShotAt;
        private bool triggerDown;
        private float chargeStart = -1;
        private int burstLeft;
        private float burstNextAt;

        public Gun(IReadOnlyList<WeaponId> weapons)
        {
            if (weapons.Count == 0) throw new ArgumentException("무기가 하나 이상 있어야 합니다.", nameof(weapons));
            Weapons = weapons;
            Current = weapons[0];
            foreach (var w in weapons) ammo[w] = Defs.Weapons[w].Magazine;
        }

        public WeaponDef Def => Defs.Weapons[Current];
        public int Ammo(WeaponId id) => ammo[id];

        public bool IsReloading(float now) => now < reloadUntil;

        public float ReloadProgress(float now) =>
            IsReloading(now) ? (now - reloadStartedAt) / (reloadUntil - reloadStartedAt) : 1f;

        /// <summary>충전식 무기의 현재 충전량 0~1. 충전 중이 아니면 0</summary>
        public float ChargeLevel(float now) =>
            chargeStart < 0 || Def.ChargeMs <= 0 ? 0f : Math.Min(1f, (now - chargeStart) / Def.ChargeMs);

        public void Select(WeaponId id)
        {
            if (id == Current || !ammo.ContainsKey(id)) return;
            Current = id;
            triggerDown = false;
            chargeStart = -1;
            burstLeft = 0;
            reloadUntil = 0; // 무기를 바꾸면 재장전은 취소된다
        }

        public List<GunEvent> Reload(float now)
        {
            var events = new List<GunEvent>();
            var w = Def;
            if (IsReloading(now) || ammo[w.Id] >= w.Magazine) return events;
            reloadStartedAt = now;
            reloadUntil = now + w.ReloadMs;
            chargeStart = -1;
            burstLeft = 0;
            events.Add(new GunEvent { Kind = GunEventKind.ReloadStart, Weapon = w.Id });
            return events;
        }

        public List<GunEvent> Press(float now, GunConditions c)
        {
            triggerDown = true;
            var events = new List<GunEvent>();
            var w = Def;
            if (c.Blocked || IsReloading(now) || now < nextShotAt || burstLeft > 0) return events;
            if (ammo[w.Id] <= 0)
            {
                events.Add(new GunEvent { Kind = GunEventKind.Empty, Weapon = w.Id });
                events.AddRange(Reload(now));
                return events;
            }
            if (w.Mode == FireMode.Charge)
            {
                chargeStart = now;
                return events;
            }
            events.Add(Fire(now, c, 1f));
            if (w.Mode == FireMode.Burst)
            {
                burstLeft = Math.Min(w.BurstCount - 1, ammo[w.Id]);
                burstNextAt = now + w.BurstGapMs;
            }
            return events;
        }

        public List<GunEvent> Release(float now, GunConditions c)
        {
            triggerDown = false;
            var events = new List<GunEvent>();
            if (chargeStart < 0) return events;
            var charge = Math.Max(Def.MinCharge, ChargeLevel(now));
            chargeStart = -1;
            if (c.Blocked || IsReloading(now)) return events;
            events.Add(Fire(now, c, charge));
            return events;
        }

        /// <summary>매 프레임 호출: 연사·점사 이어 쏘기, 재장전 완료, 탄약이 떨어지면 자동 재장전</summary>
        public List<GunEvent> Update(float now, GunConditions c)
        {
            var w = Def;
            var events = new List<GunEvent>();
            if (reloadUntil > 0 && now >= reloadUntil)
            {
                reloadUntil = 0;
                ammo[w.Id] = w.Magazine;
                events.Add(new GunEvent { Kind = GunEventKind.ReloadDone, Weapon = w.Id });
            }
            if (c.Blocked)
            {
                burstLeft = 0;
                chargeStart = -1;
                return events;
            }
            if (burstLeft > 0 && now >= burstNextAt)
            {
                burstLeft -= 1;
                burstNextAt = now + w.BurstGapMs;
                events.Add(Fire(now, c, 1f));
            }
            else if (w.Mode == FireMode.Auto && triggerDown && !IsReloading(now) && now >= nextShotAt && ammo[w.Id] > 0)
            {
                events.Add(Fire(now, c, 1f));
            }
            if (ammo[w.Id] <= 0 && !IsReloading(now) && burstLeft == 0) events.AddRange(Reload(now));
            return events;
        }

        private GunEvent Fire(float now, GunConditions c, float charge)
        {
            var w = Def;
            ammo[w.Id] -= 1;
            nextShotAt = now + w.FireIntervalMs * Math.Max(1f, c.SlowFactor);
            return new GunEvent { Kind = GunEventKind.Fire, Weapon = w.Id, Charge = charge };
        }
    }
}
