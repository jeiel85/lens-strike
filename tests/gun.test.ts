import { describe, expect, it } from 'vitest';
import { Gun, type GunEvent } from '../src/game/gun';

const ok = { slowFactor: 1, blocked: false };
const fires = (events: GunEvent[]) => events.filter((e) => e.kind === 'fire').length;

describe('Gun', () => {
  it('단발은 사격 간격 전에는 다시 쏘지 않는다', () => {
    const g = new Gun(['shotgun']);
    expect(fires(g.press(0, ok))).toBe(1);
    g.release(10, ok);
    expect(fires(g.press(500, ok))).toBe(0);
    expect(fires(g.press(900, ok))).toBe(1);
    expect(g.ammo.shotgun).toBe(4);
  });

  it('점사는 탭 한 번에 3발', () => {
    const g = new Gun(['m16', 'k2', 'shotgun']);
    let n = fires(g.press(0, ok));
    g.release(5, ok);
    for (let t = 10; t <= 300; t += 10) n += fires(g.update(t, ok));
    expect(n).toBe(3);
    expect(g.ammo.m16).toBe(27);
  });

  it('연사는 누르고 있는 동안 계속 쏜다', () => {
    const g = new Gun(['k2']);
    let n = fires(g.press(0, ok));
    for (let t = 10; t < 1100; t += 10) n += fires(g.update(t, ok));
    expect(n).toBe(10); // 110ms 간격
    g.release(1100, ok);
    expect(fires(g.update(1300, ok))).toBe(0);
  });

  it('젖음 상태면 사격 간격이 늘어난다', () => {
    const g = new Gun(['k2']);
    const slow = { slowFactor: 2, blocked: false };
    let n = fires(g.press(0, slow));
    for (let t = 10; t < 1100; t += 10) n += fires(g.update(t, slow));
    expect(n).toBe(5);
  });

  it('충전식은 뗄 때 충전량과 함께 쏜다', () => {
    const g = new Gun(['plasma']);
    expect(g.press(0, ok)).toEqual([]);
    expect(g.chargeLevel(450)).toBeCloseTo(0.5);
    expect(g.release(450, ok)).toEqual([{ kind: 'fire', weapon: 'plasma', charge: 0.5 }]);
    g.press(1000, ok);
    expect(g.release(1010, ok)[0]).toMatchObject({ charge: 0.2 }); // 최소 충전량
  });

  it('탄약이 떨어지면 자동 재장전하고 끝나면 채운다', () => {
    const g = new Gun(['fireball']);
    let t = 0;
    for (let i = 0; i < 5; i++) {
      g.press(t, ok);
      g.release(t, ok);
      t += 1100;
    }
    expect(g.ammo.fireball).toBe(0);
    expect(g.update(t, ok)).toContainEqual({ kind: 'reloadStart', weapon: 'fireball' });
    expect(g.isReloading(t + 100)).toBe(true);
    expect(g.update(t + 2600, ok)).toContainEqual({ kind: 'reloadDone', weapon: 'fireball' });
    expect(g.ammo.fireball).toBe(5);
  });

  it('감전 중에는 쏠 수 없다', () => {
    const g = new Gun(['k2']);
    expect(g.press(0, { slowFactor: 1, blocked: true })).toEqual([]);
  });
});
