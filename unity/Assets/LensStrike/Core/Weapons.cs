#nullable enable
// 캐릭터·무기 수치. 웹 버전(src/config/weapons.ts)과 같은 수치를 쓰되,
// AR에서는 화면 비율 대신 조준 원뿔 각도(spreadDeg)와 투사체 속도로 판정한다.
using System;
using System.Collections.Generic;

namespace LensStrike.Core
{
    public enum WeaponId : byte { M16, K2, Shotgun, Plasma, Fireball, Waterball }

    public enum CharacterId : byte { Ranger, Trooper, Mage }

    public enum EffectKind : byte { None, Burn, Soak, Shock }

    public enum FireMode : byte { Semi, Auto, Burst, Charge }

    public sealed class WeaponEffect
    {
        public EffectKind Kind;
        public float DurationMs;
        /// <summary>화상: 초당 지속 피해</summary>
        public float Dps;
        /// <summary>젖음: 맞은 쪽 사격 간격 배율</summary>
        public float FireRateFactor = 1f;
    }

    public sealed class WeaponDef
    {
        public WeaponId Id;
        public string Name = "";
        public FireMode Mode;
        /// <summary>1발(산탄은 1알) 기준 몸통 피해</summary>
        public float Damage;
        public float HeadMultiplier;
        public float FireIntervalMs;
        public int BurstCount = 1;
        public float BurstGapMs;
        /// <summary>산탄 알 수. 0이면 단일 탄</summary>
        public int Pellets;
        public int Magazine;
        public float ReloadMs;
        /// <summary>조준 허용 원뿔의 반각(도). 산탄은 알이 퍼지는 각도</summary>
        public float SpreadDeg;
        public float OptimalRangeM;
        public float MaxRangeM;
        /// <summary>투사체 속도(m/s). 0이면 즉시 명중(히트스캔)</summary>
        public float ProjectileSpeed;
        /// <summary>투사체 반지름(m)</summary>
        public float ProjectileRadius;
        public float ChargeMs;
        public float MinCharge;
        public WeaponEffect? Effect;
        public string ColorHex = "#ffffff";
    }

    public sealed class CharacterDef
    {
        public CharacterId Id;
        public string Name = "";
        public string Tagline = "";
        public float MaxHp;
        public WeaponId[] Weapons = Array.Empty<WeaponId>();
        public string ColorHex = "#ffffff";
    }

    public static class Defs
    {
        public static readonly IReadOnlyDictionary<WeaponId, WeaponDef> Weapons = new Dictionary<WeaponId, WeaponDef>
        {
            [WeaponId.M16] = new WeaponDef
            {
                Id = WeaponId.M16, Name = "M16형 소총", Mode = FireMode.Burst, Damage = 12, HeadMultiplier = 2,
                FireIntervalMs = 450, BurstCount = 3, BurstGapMs = 80, Magazine = 30, ReloadMs = 1800,
                SpreadDeg = 0.8f, OptimalRangeM = 25, MaxRangeM = 60, ColorHex = "#ffd166",
            },
            [WeaponId.K2] = new WeaponDef
            {
                Id = WeaponId.K2, Name = "K2형 소총", Mode = FireMode.Auto, Damage = 8, HeadMultiplier = 2,
                FireIntervalMs = 110, Magazine = 30, ReloadMs = 1600,
                SpreadDeg = 1.0f, OptimalRangeM = 20, MaxRangeM = 50, ColorHex = "#f4a261",
            },
            [WeaponId.Shotgun] = new WeaponDef
            {
                Id = WeaponId.Shotgun, Name = "샷건", Mode = FireMode.Semi, Damage = 6, HeadMultiplier = 1.5f,
                FireIntervalMs = 900, Pellets = 8, Magazine = 6, ReloadMs = 2200,
                SpreadDeg = 5.5f, OptimalRangeM = 4, MaxRangeM = 12, ColorHex = "#e76f51",
            },
            [WeaponId.Plasma] = new WeaponDef
            {
                Id = WeaponId.Plasma, Name = "플라즈마 건", Mode = FireMode.Charge, Damage = 45, HeadMultiplier = 1.5f,
                FireIntervalMs = 350, ChargeMs = 900, MinCharge = 0.2f, Magazine = 12, ReloadMs = 2400,
                SpreadDeg = 1.5f, OptimalRangeM = 15, MaxRangeM = 40, ColorHex = "#4cc9f0",
                Effect = new WeaponEffect { Kind = EffectKind.Shock, DurationMs = 600 },
            },
            [WeaponId.Fireball] = new WeaponDef
            {
                Id = WeaponId.Fireball, Name = "파이어볼", Mode = FireMode.Semi, Damage = 28, HeadMultiplier = 1.5f,
                FireIntervalMs = 1100, Magazine = 5, ReloadMs = 2600,
                SpreadDeg = 0, ProjectileSpeed = 22, ProjectileRadius = 0.22f, OptimalRangeM = 12, MaxRangeM = 25, ColorHex = "#ff6b35",
                Effect = new WeaponEffect { Kind = EffectKind.Burn, DurationMs = 3000, Dps = 4 },
            },
            [WeaponId.Waterball] = new WeaponDef
            {
                Id = WeaponId.Waterball, Name = "워터볼", Mode = FireMode.Semi, Damage = 18, HeadMultiplier = 1.5f,
                FireIntervalMs = 800, Magazine = 6, ReloadMs = 2200,
                SpreadDeg = 0, ProjectileSpeed = 26, ProjectileRadius = 0.28f, OptimalRangeM = 12, MaxRangeM = 25, ColorHex = "#3a86ff",
                Effect = new WeaponEffect { Kind = EffectKind.Soak, DurationMs = 3500, FireRateFactor = 1.6f },
            },
        };

        public static readonly IReadOnlyDictionary<CharacterId, CharacterDef> Characters = new Dictionary<CharacterId, CharacterDef>
        {
            [CharacterId.Ranger] = new CharacterDef
            {
                Id = CharacterId.Ranger, Name = "레인저", Tagline = "클래식 화기 3종. 거리에 맞춰 무기를 바꿔 쓴다.",
                MaxHp = 100, Weapons = new[] { WeaponId.M16, WeaponId.K2, WeaponId.Shotgun }, ColorHex = "#ffd166",
            },
            [CharacterId.Trooper] = new CharacterDef
            {
                Id = CharacterId.Trooper, Name = "사이버 트루퍼", Tagline = "충전식 플라즈마 건과 두꺼운 장갑. 맞으면 상대가 감전된다.",
                MaxHp = 120, Weapons = new[] { WeaponId.Plasma }, ColorHex = "#4cc9f0",
            },
            [CharacterId.Mage] = new CharacterDef
            {
                Id = CharacterId.Mage, Name = "마법사", Tagline = "날아가는 마법탄. 몸을 피하면 빗나간다. 불태우거나 흠뻑 적신다.",
                MaxHp = 100, Weapons = new[] { WeaponId.Fireball, WeaponId.Waterball }, ColorHex = "#c77dff",
            },
        };

        /// <summary>거리에 따른 피해 배율. 최대 사거리를 넘으면 0(빗나감).</summary>
        public static float RangeFactor(WeaponDef weapon, float distanceM)
        {
            if (float.IsNaN(distanceM) || distanceM <= weapon.OptimalRangeM) return 1f;
            if (distanceM > weapon.MaxRangeM) return 0f;
            var t = (distanceM - weapon.OptimalRangeM) / (weapon.MaxRangeM - weapon.OptimalRangeM);
            return 1f - 0.6f * t;
        }
    }
}
