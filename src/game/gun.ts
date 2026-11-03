// 내 기기의 방아쇠 상태: 탄약, 재장전, 연사·점사·충전. 시간을 인자로 받아 테스트할 수 있게 한다.

import { WEAPONS, type WeaponId } from '../config/weapons';

export type GunEvent =
  | { kind: 'fire'; weapon: WeaponId; charge: number }
  | { kind: 'empty' }
  | { kind: 'reloadStart'; weapon: WeaponId }
  | { kind: 'reloadDone'; weapon: WeaponId };

export interface GunConditions {
  /** 젖음 등으로 느려진 사격 간격 배율 */
  slowFactor: number;
  /** 감전·사망 등으로 쏠 수 없음 */
  blocked: boolean;
}

export class Gun {
  current: WeaponId;
  readonly ammo = {} as Record<WeaponId, number>;
  private reloadUntil = 0;
  private reloadStartedAt = 0;
  private nextShotAt = 0;
  private triggerDown = false;
  private chargeStart = -1;
  private burstLeft = 0;
  private burstNextAt = 0;

  constructor(readonly weapons: WeaponId[]) {
    this.current = weapons[0];
    for (const w of weapons) this.ammo[w] = WEAPONS[w].magazine;
  }

  get def() {
    return WEAPONS[this.current];
  }

  isReloading(now: number): boolean {
    return now < this.reloadUntil;
  }

  reloadProgress(now: number): number {
    if (!this.isReloading(now)) return 1;
    return (now - this.reloadStartedAt) / (this.reloadUntil - this.reloadStartedAt);
  }

  /** 충전식 무기의 현재 충전량 0~1. 충전 중이 아니면 0 */
  chargeLevel(now: number): number {
    if (this.chargeStart < 0 || !this.def.chargeMs) return 0;
    return Math.min(1, (now - this.chargeStart) / this.def.chargeMs);
  }

  select(id: WeaponId): void {
    if (!this.weapons.includes(id) || id === this.current) return;
    this.current = id;
    this.triggerDown = false;
    this.chargeStart = -1;
    this.burstLeft = 0;
    this.reloadUntil = 0; // 무기를 바꾸면 재장전은 취소된다
  }

  reload(now: number): GunEvent[] {
    const w = this.def;
    if (this.isReloading(now) || this.ammo[w.id] >= w.magazine) return [];
    this.reloadStartedAt = now;
    this.reloadUntil = now + w.reloadMs;
    this.chargeStart = -1;
    this.burstLeft = 0;
    return [{ kind: 'reloadStart', weapon: w.id }];
  }

  press(now: number, c: GunConditions): GunEvent[] {
    this.triggerDown = true;
    const w = this.def;
    if (c.blocked || this.isReloading(now) || now < this.nextShotAt || this.burstLeft > 0) return [];
    if (this.ammo[w.id] <= 0) return [{ kind: 'empty' }, ...this.reload(now)];
    if (w.mode === 'charge') {
      this.chargeStart = now;
      return [];
    }
    const events = this.fire(now, c, 1);
    if (w.mode === 'burst') {
      this.burstLeft = Math.min((w.burstCount ?? 1) - 1, this.ammo[w.id]);
      this.burstNextAt = now + (w.burstGapMs ?? 0);
    }
    return events;
  }

  release(now: number, c: GunConditions): GunEvent[] {
    this.triggerDown = false;
    if (this.chargeStart < 0) return [];
    const charge = Math.max(this.def.minCharge ?? 0, this.chargeLevel(now));
    this.chargeStart = -1;
    if (c.blocked || this.isReloading(now)) return [];
    return this.fire(now, c, charge);
  }

  /** 매 프레임 호출: 연사·점사 이어 쏘기, 재장전 완료, 탄약이 떨어지면 자동 재장전 */
  update(now: number, c: GunConditions): GunEvent[] {
    const w = this.def;
    const events: GunEvent[] = [];
    if (this.reloadUntil > 0 && now >= this.reloadUntil) {
      this.reloadUntil = 0;
      this.ammo[w.id] = w.magazine;
      events.push({ kind: 'reloadDone', weapon: w.id });
    }
    if (c.blocked) {
      this.burstLeft = 0;
      this.chargeStart = -1;
      return events;
    }
    if (this.burstLeft > 0 && now >= this.burstNextAt) {
      this.burstLeft -= 1;
      this.burstNextAt = now + (w.burstGapMs ?? 0);
      events.push(...this.fire(now, c, 1));
    } else if (w.mode === 'auto' && this.triggerDown && !this.isReloading(now) && now >= this.nextShotAt) {
      if (this.ammo[w.id] > 0) events.push(...this.fire(now, c, 1));
    }
    if (this.ammo[w.id] <= 0 && !this.isReloading(now) && this.burstLeft === 0) events.push(...this.reload(now));
    return events;
  }

  private fire(now: number, c: GunConditions, charge: number): GunEvent[] {
    const w = this.def;
    this.ammo[w.id] -= 1;
    this.nextShotAt = now + w.fireIntervalMs * Math.max(1, c.slowFactor);
    return [{ kind: 'fire', weapon: w.id, charge }];
  }
}
