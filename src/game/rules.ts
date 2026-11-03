// 경기 규칙. 방장 기기에서만 돌며(host-authoritative), 피해·킬·효과·부활을 결정한다.
// 상대 기기는 "맞혔다"는 보고만 보내고, 숫자는 여기서 계산한다.

import { CHARACTERS, WEAPONS, rangeFactor, type CharacterId, type EffectKind, type WeaponId } from '../config/weapons';

export interface ShotReport {
  weapon: WeaponId;
  /** 머리·몸통에 맞은 알 수(산탄이 아니면 둘 중 하나가 1) */
  head: number;
  body: number;
  distanceM: number;
  /** 충전식 무기의 충전량 0~1 */
  charge?: number;
}

export interface PlayerState {
  id: string;
  name: string;
  character: CharacterId;
  hp: number;
  maxHp: number;
  kills: number;
  deaths: number;
  alive: boolean;
  respawnMs: number;
  protectMs: number;
  burnMs: number;
  burnDps: number;
  burnFrom: string | null;
  soakMs: number;
  soakFactor: number;
  shockMs: number;
}

export type Phase = 'lobby' | 'playing' | 'ended';

export interface MatchSnapshot {
  phase: Phase;
  killTarget: number;
  winnerId: string | null;
  players: PlayerState[];
}

export interface HitEvent {
  from: string;
  to: string;
  weapon: WeaponId | 'burn';
  damage: number;
  zone: 'head' | 'body' | 'burn';
  killed: boolean;
  effect?: EffectKind;
}

export interface MatchOptions {
  /** 0이면 승패 없음(연습 모드) */
  killTarget: number;
  respawnMs: number;
  /** 부활 직후 무적 시간 */
  protectMs: number;
}

export const DEFAULT_MATCH: MatchOptions = { killTarget: 5, respawnMs: 4000, protectMs: 1500 };

interface Bucket {
  tokens: number;
  at: number;
}

export class Match {
  phase: Phase = 'lobby';
  winnerId: string | null = null;
  private players = new Map<string, PlayerState>();
  private buckets = new Map<string, Bucket>();

  constructor(readonly options: MatchOptions = DEFAULT_MATCH) {}

  addPlayer(id: string, name: string, character: CharacterId): PlayerState {
    const maxHp = CHARACTERS[character].maxHp;
    const p: PlayerState = {
      id, name, character, hp: maxHp, maxHp, kills: 0, deaths: 0, alive: true,
      respawnMs: 0, protectMs: 0, burnMs: 0, burnDps: 0, burnFrom: null, soakMs: 0, soakFactor: 1, shockMs: 0,
    };
    this.players.set(id, p);
    return p;
  }

  removePlayer(id: string): void {
    this.players.delete(id);
    for (const key of this.buckets.keys()) if (key.startsWith(`${id}|`)) this.buckets.delete(key);
  }

  has(id: string): boolean {
    return this.players.has(id);
  }

  get(id: string): PlayerState | undefined {
    return this.players.get(id);
  }

  get size(): number {
    return this.players.size;
  }

  /** 상대 1명(1대1 전제). 연습 모드에서는 훈련 표적. */
  opponentOf(id: string): PlayerState | undefined {
    for (const p of this.players.values()) if (p.id !== id) return p;
    return undefined;
  }

  start(): void {
    this.phase = 'playing';
    this.winnerId = null;
    this.buckets.clear();
    for (const p of this.players.values()) this.revive(p, true);
  }

  /**
   * 맞힌 보고를 검증하고 피해를 적용한다. 받아들이지 않으면 null.
   * 무기 소유·생존·무적·사거리·연사 속도(토큰 버킷)를 확인한다.
   */
  applyShot(shooterId: string, targetId: string, shot: ShotReport, nowMs: number): HitEvent | null {
    if (this.phase !== 'playing' || shooterId === targetId) return null;
    const shooter = this.players.get(shooterId);
    const target = this.players.get(targetId);
    if (!shooter || !target || !shooter.alive || !target.alive || target.protectMs > 0) return null;

    const weapon = WEAPONS[shot.weapon];
    if (!weapon || !CHARACTERS[shooter.character].weapons.includes(weapon.id)) return null;

    const maxHits = weapon.pellets ?? 1;
    const head = clampInt(shot.head, 0, maxHits);
    const body = clampInt(shot.body, 0, maxHits - head);
    if (head + body === 0) return null;

    const range = rangeFactor(weapon, shot.distanceM);
    if (range === 0) return null;
    if (!this.takeToken(shooterId, weapon.id, nowMs)) return null;

    const charge = weapon.mode === 'charge' ? clamp(shot.charge ?? 0, weapon.minCharge ?? 0, 1) : 1;
    const damage = Math.max(1, Math.round((body + head * weapon.headMultiplier) * weapon.damage * charge * range));

    if (weapon.effect) {
      const e = weapon.effect;
      if (e.kind === 'burn') {
        target.burnMs = e.durationMs;
        target.burnDps = e.dps ?? 0;
        target.burnFrom = shooterId;
      } else if (e.kind === 'soak') {
        target.soakMs = e.durationMs;
        target.soakFactor = e.fireRateFactor ?? 1;
      } else {
        target.shockMs = e.durationMs;
      }
    }

    const killed = this.damage(target, damage, shooter);
    return {
      from: shooterId,
      to: targetId,
      weapon: weapon.id,
      damage,
      zone: head > 0 ? 'head' : 'body',
      killed,
      effect: weapon.effect?.kind,
    };
  }

  /** 시간 경과: 화상 피해, 효과 만료, 부활. 화상 피해 이벤트를 돌려준다. */
  tick(dtMs: number): HitEvent[] {
    if (this.phase !== 'playing') return [];
    const events: HitEvent[] = [];
    for (const p of this.players.values()) {
      if (!p.alive) {
        p.respawnMs -= dtMs;
        if (p.respawnMs <= 0) this.revive(p, false);
        continue;
      }
      p.protectMs = Math.max(0, p.protectMs - dtMs);
      p.soakMs = Math.max(0, p.soakMs - dtMs);
      p.shockMs = Math.max(0, p.shockMs - dtMs);
      if (p.soakMs === 0) p.soakFactor = 1;
      if (p.burnMs > 0) {
        const step = Math.min(dtMs, p.burnMs);
        p.burnMs -= step;
        const amount = (p.burnDps * step) / 1000;
        const from = p.burnFrom ? this.players.get(p.burnFrom) : undefined;
        const killed = this.damage(p, amount, from);
        events.push({ from: p.burnFrom ?? '', to: p.id, weapon: 'burn', damage: amount, zone: 'burn', killed });
        if (p.burnMs <= 0) p.burnFrom = null;
        if (this.phase !== 'playing') break;
      }
    }
    return events;
  }

  snapshot(): MatchSnapshot {
    return {
      phase: this.phase,
      killTarget: this.options.killTarget,
      winnerId: this.winnerId,
      players: [...this.players.values()].map((p) => ({ ...p })),
    };
  }

  private damage(target: PlayerState, amount: number, attacker: PlayerState | undefined): boolean {
    target.hp = Math.max(0, target.hp - amount);
    if (target.hp > 0) return false;
    target.alive = false;
    target.deaths += 1;
    target.respawnMs = this.options.respawnMs;
    target.burnMs = 0;
    target.burnFrom = null;
    target.soakMs = 0;
    target.soakFactor = 1;
    target.shockMs = 0;
    if (attacker && attacker.id !== target.id) {
      attacker.kills += 1;
      if (this.options.killTarget > 0 && attacker.kills >= this.options.killTarget) {
        this.phase = 'ended';
        this.winnerId = attacker.id;
      }
    }
    return true;
  }

  private revive(p: PlayerState, resetScore: boolean): void {
    p.hp = p.maxHp;
    p.alive = true;
    p.respawnMs = 0;
    p.protectMs = resetScore ? 0 : this.options.protectMs;
    p.burnMs = 0;
    p.burnDps = 0;
    p.burnFrom = null;
    p.soakMs = 0;
    p.soakFactor = 1;
    p.shockMs = 0;
    if (resetScore) {
      p.kills = 0;
      p.deaths = 0;
    }
  }

  /**
   * 무기별 연사 한도. 한 사이클(점사 포함)에 낼 수 있는 발수만큼 다시 차오르고,
   * 네트워크 지연으로 보고가 몰려 오는 경우를 위해 한 발 여유를 둔다.
   */
  private takeToken(shooterId: string, weaponId: WeaponId, nowMs: number): boolean {
    const w = WEAPONS[weaponId];
    const perCycle = w.burstCount ?? 1;
    const cycleMs = w.fireIntervalMs + (perCycle - 1) * (w.burstGapMs ?? 0) + (w.chargeMs ?? 0) * (w.minCharge ?? 0);
    const capacity = perCycle + 1;
    const key = `${shooterId}|${weaponId}`;
    const b = this.buckets.get(key) ?? { tokens: capacity, at: nowMs };
    b.tokens = Math.min(capacity, b.tokens + ((nowMs - b.at) * perCycle) / cycleMs);
    b.at = nowMs;
    this.buckets.set(key, b);
    if (b.tokens < 1) return false;
    b.tokens -= 1;
    return true;
  }
}

function clamp(v: number, lo: number, hi: number): number {
  return Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : lo;
}

function clampInt(v: number, lo: number, hi: number): number {
  return Math.floor(clamp(v, lo, hi));
}
