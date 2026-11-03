export type WeaponId = 'm16' | 'k2' | 'shotgun' | 'plasma' | 'fireball' | 'waterball';
export type CharacterId = 'ranger' | 'trooper' | 'mage';
export type EffectKind = 'burn' | 'soak' | 'shock';

export interface WeaponEffect {
  kind: EffectKind;
  durationMs: number;
  /** burn: 초당 지속 피해 */
  dps?: number;
  /** soak: 맞은 쪽 사격 간격 배율 */
  fireRateFactor?: number;
}

export interface WeaponDef {
  id: WeaponId;
  name: string;
  /** semi: 탭 1발, auto: 누르는 동안 연사, burst: 탭 한 번에 점사, charge: 누르고 있다가 떼면 발사 */
  mode: 'semi' | 'auto' | 'burst' | 'charge';
  /** 1발(산탄은 1알) 기준 몸통 피해 */
  damage: number;
  headMultiplier: number;
  /** 방아쇠(또는 연사 1발) 사이 최소 간격 */
  fireIntervalMs: number;
  burstCount?: number;
  burstGapMs?: number;
  /** 산탄 알 수. 조준 원 안에 무작위로 흩어진다. */
  pellets?: number;
  magazine: number;
  reloadMs: number;
  /** 조준 원 반지름. 화면 짧은 변에 대한 비율 */
  aimRadius: number;
  /** 이 거리까지는 피해 100% */
  optimalRangeM: number;
  /** 이 거리를 넘으면 빗나감. optimal~max 사이는 40%까지 선형 감소 */
  maxRangeM: number;
  /** 투사체 비행 시간. 도착 순간에 조준점 안에 상대가 있어야 맞는다. */
  travelMs?: number;
  chargeMs?: number;
  minCharge?: number;
  effect?: WeaponEffect;
  color: string;
}

export interface CharacterDef {
  id: CharacterId;
  name: string;
  tagline: string;
  maxHp: number;
  weapons: WeaponId[];
  color: string;
}

export const WEAPONS: Record<WeaponId, WeaponDef> = {
  m16: {
    id: 'm16',
    name: 'M16형 소총',
    mode: 'burst',
    damage: 12,
    headMultiplier: 2,
    fireIntervalMs: 450,
    burstCount: 3,
    burstGapMs: 80,
    magazine: 30,
    reloadMs: 1800,
    aimRadius: 0.012,
    optimalRangeM: 25,
    maxRangeM: 60,
    color: '#ffd166',
  },
  k2: {
    id: 'k2',
    name: 'K2형 소총',
    mode: 'auto',
    damage: 8,
    headMultiplier: 2,
    fireIntervalMs: 110,
    magazine: 30,
    reloadMs: 1600,
    aimRadius: 0.015,
    optimalRangeM: 20,
    maxRangeM: 50,
    color: '#f4a261',
  },
  shotgun: {
    id: 'shotgun',
    name: '샷건',
    mode: 'semi',
    damage: 6,
    headMultiplier: 1.5,
    fireIntervalMs: 900,
    pellets: 8,
    magazine: 6,
    reloadMs: 2200,
    aimRadius: 0.09,
    optimalRangeM: 4,
    maxRangeM: 12,
    color: '#e76f51',
  },
  plasma: {
    id: 'plasma',
    name: '플라즈마 건',
    mode: 'charge',
    damage: 45,
    headMultiplier: 1.5,
    fireIntervalMs: 350,
    chargeMs: 900,
    minCharge: 0.2,
    magazine: 12,
    reloadMs: 2400,
    aimRadius: 0.025,
    optimalRangeM: 15,
    maxRangeM: 40,
    effect: { kind: 'shock', durationMs: 600 },
    color: '#4cc9f0',
  },
  fireball: {
    id: 'fireball',
    name: '파이어볼',
    mode: 'semi',
    damage: 28,
    headMultiplier: 1.5,
    fireIntervalMs: 1100,
    magazine: 5,
    reloadMs: 2600,
    aimRadius: 0.05,
    optimalRangeM: 12,
    maxRangeM: 25,
    travelMs: 450,
    effect: { kind: 'burn', durationMs: 3000, dps: 4 },
    color: '#ff6b35',
  },
  waterball: {
    id: 'waterball',
    name: '워터볼',
    mode: 'semi',
    damage: 18,
    headMultiplier: 1.5,
    fireIntervalMs: 800,
    magazine: 6,
    reloadMs: 2200,
    aimRadius: 0.06,
    optimalRangeM: 12,
    maxRangeM: 25,
    travelMs: 380,
    effect: { kind: 'soak', durationMs: 3500, fireRateFactor: 1.6 },
    color: '#3a86ff',
  },
};

export const CHARACTERS: Record<CharacterId, CharacterDef> = {
  ranger: {
    id: 'ranger',
    name: '레인저',
    tagline: '클래식 화기 3종. 거리에 맞춰 무기를 바꿔 쓴다.',
    maxHp: 100,
    weapons: ['m16', 'k2', 'shotgun'],
    color: '#ffd166',
  },
  trooper: {
    id: 'trooper',
    name: '사이버 트루퍼',
    tagline: '충전식 플라즈마 건 하나와 두꺼운 장갑. 맞으면 상대가 감전된다.',
    maxHp: 120,
    weapons: ['plasma'],
    color: '#4cc9f0',
  },
  mage: {
    id: 'mage',
    name: '마법사',
    tagline: '날아가는 마법탄. 상대를 불태우거나 흠뻑 적셔 느리게 만든다.',
    maxHp: 100,
    weapons: ['fireball', 'waterball'],
    color: '#c77dff',
  },
};

export const CHARACTER_IDS = Object.keys(CHARACTERS) as CharacterId[];

export function isWeaponId(v: unknown): v is WeaponId {
  return typeof v === 'string' && Object.prototype.hasOwnProperty.call(WEAPONS, v);
}

export function isCharacterId(v: unknown): v is CharacterId {
  return typeof v === 'string' && Object.prototype.hasOwnProperty.call(CHARACTERS, v);
}

/** 거리에 따른 피해 배율. maxRange를 넘으면 0(빗나감). */
export function rangeFactor(weapon: WeaponDef, distanceM: number): number {
  if (!Number.isFinite(distanceM) || distanceM <= weapon.optimalRangeM) return 1;
  if (distanceM > weapon.maxRangeM) return 0;
  const t = (distanceM - weapon.optimalRangeM) / (weapon.maxRangeM - weapon.optimalRangeM);
  return 1 - 0.6 * t;
}
