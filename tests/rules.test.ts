import { describe, expect, it } from 'vitest';
import { Match, type ShotReport } from '../src/game/rules';

const rifleBody: ShotReport = { weapon: 'k2', head: 0, body: 1, distanceM: 5 };

function duel(killTarget = 2) {
  const m = new Match({ killTarget, respawnMs: 1000, protectMs: 500 });
  m.addPlayer('a', 'A', 'ranger');
  m.addPlayer('b', 'B', 'mage');
  m.start();
  return m;
}

describe('Match.applyShot', () => {
  it('시작 전에는 맞아도 피해가 없다', () => {
    const m = new Match();
    m.addPlayer('a', 'A', 'ranger');
    m.addPlayer('b', 'B', 'mage');
    expect(m.applyShot('a', 'b', rifleBody, 0)).toBeNull();
  });

  it('몸통·머리 피해를 계산한다', () => {
    const m = duel();
    expect(m.applyShot('a', 'b', rifleBody, 0)?.damage).toBe(8);
    expect(m.applyShot('a', 'b', { ...rifleBody, head: 1, body: 0 }, 1000)?.damage).toBe(16);
    expect(m.get('b')!.hp).toBe(76);
  });

  it('자기 캐릭터 무기가 아니면 거부한다', () => {
    const m = duel();
    expect(m.applyShot('a', 'b', { ...rifleBody, weapon: 'fireball' }, 0)).toBeNull();
  });

  it('사거리를 넘으면 거부하고, 사거리 안 먼 거리는 피해가 줄어든다', () => {
    const m = duel();
    expect(m.applyShot('a', 'b', { ...rifleBody, distanceM: 51 }, 0)).toBeNull();
    expect(m.applyShot('a', 'b', { ...rifleBody, distanceM: 50 }, 0)?.damage).toBe(Math.round(8 * 0.4));
  });

  it('산탄 알 수는 무기 한도로 자른다', () => {
    const m = duel();
    const hit = m.applyShot('a', 'b', { weapon: 'shotgun', head: 0, body: 50, distanceM: 2 }, 0);
    expect(hit?.damage).toBe(8 * 6);
  });

  it('연사 속도를 넘는 보고는 거부한다', () => {
    const m = duel(99);
    // 버킷 용량(1+1)만큼은 바로 받아들이고 그다음은 거부
    expect(m.applyShot('a', 'b', rifleBody, 0)).not.toBeNull();
    expect(m.applyShot('a', 'b', rifleBody, 0)).not.toBeNull();
    expect(m.applyShot('a', 'b', rifleBody, 0)).toBeNull();
    // 연사 간격(110ms)이 지나면 다시 받는다
    expect(m.applyShot('a', 'b', rifleBody, 110)).not.toBeNull();
  });

  it('점사 무기는 한 사이클에 3발을 받는다', () => {
    const m = duel(99);
    const burst: ShotReport = { weapon: 'm16', head: 0, body: 1, distanceM: 5 };
    // 점사 3발 + 여유 1발까지는 한꺼번에 와도 받고, 그 이상은 거부
    const accepted = [0, 0, 0, 0, 0].filter((t) => m.applyShot('a', 'b', burst, t) !== null).length;
    expect(accepted).toBe(4);
    // 한 사이클(450 + 2×80ms)이 지나면 다시 3발
    const next = [610, 610, 610, 610].filter((t) => m.applyShot('a', 'b', burst, t) !== null).length;
    expect(next).toBe(3);
  });

  it('충전량에 비례해 피해가 커지고 최소 충전량이 보장된다', () => {
    const m = new Match({ killTarget: 0, respawnMs: 1000, protectMs: 0 });
    m.addPlayer('t', 'T', 'trooper');
    m.addPlayer('d', 'D', 'ranger');
    m.start();
    expect(m.applyShot('t', 'd', { weapon: 'plasma', head: 0, body: 1, distanceM: 3, charge: 1 }, 0)?.damage).toBe(45);
    expect(m.applyShot('t', 'd', { weapon: 'plasma', head: 0, body: 1, distanceM: 3, charge: 0 }, 5000)?.damage).toBe(9);
    expect(m.get('d')!.shockMs).toBe(600);
  });
});

describe('Match 킬·부활·승리', () => {
  it('HP가 0이 되면 킬이 오르고, 목표 킬에 닿으면 끝난다', () => {
    const m = duel(1);
    let t = 0;
    let hit = null;
    while (m.phase === 'playing') {
      hit = m.applyShot('a', 'b', { ...rifleBody, head: 1, body: 0 }, (t += 500));
    }
    expect(hit?.killed).toBe(true);
    expect(m.get('a')!.kills).toBe(1);
    expect(m.phase).toBe('ended');
    expect(m.winnerId).toBe('a');
  });

  it('죽은 뒤 부활하고, 부활 직후 무적이다', () => {
    const m = duel(5);
    let t = 0;
    while (m.get('b')!.alive) m.applyShot('a', 'b', { ...rifleBody, head: 1, body: 0 }, (t += 500));
    expect(m.applyShot('a', 'b', rifleBody, (t += 500))).toBeNull();
    m.tick(1000);
    expect(m.get('b')!.alive).toBe(true);
    expect(m.get('b')!.hp).toBe(100);
    expect(m.applyShot('a', 'b', rifleBody, (t += 500))).toBeNull();
    m.tick(500);
    expect(m.applyShot('a', 'b', rifleBody, (t += 500))).not.toBeNull();
  });
});

describe('Match 효과', () => {
  it('화상은 시간에 따라 피해를 주고 화상을 건 사람이 킬을 가져간다', () => {
    const m = new Match({ killTarget: 1, respawnMs: 1000, protectMs: 0 });
    m.addPlayer('mage', 'M', 'mage');
    m.addPlayer('r', 'R', 'ranger');
    m.start();
    m.applyShot('mage', 'r', { weapon: 'fireball', head: 0, body: 1, distanceM: 3 }, 0);
    expect(m.get('r')!.hp).toBe(72);
    const events = m.tick(1000);
    expect(events[0]).toMatchObject({ weapon: 'burn', damage: 4, from: 'mage' });
    expect(m.get('r')!.hp).toBe(68);
    m.tick(5000);
    expect(m.get('r')!.hp).toBe(60); // 3초 × 4
    expect(m.get('r')!.burnMs).toBe(0);

    m.get('r')!.hp = 2;
    m.applyShot('mage', 'r', { weapon: 'fireball', head: 0, body: 0, distanceM: 3 }, 9000); // 빗나감 보고는 무시
    m.get('r')!.burnMs = 1000;
    m.get('r')!.burnDps = 4;
    m.get('r')!.burnFrom = 'mage';
    m.tick(1000);
    expect(m.phase).toBe('ended');
    expect(m.winnerId).toBe('mage');
  });

  it('젖음은 사격 간격 배율을 걸고 끝나면 풀린다', () => {
    const m = duel(5);
    m.applyShot('b', 'a', { weapon: 'waterball', head: 0, body: 1, distanceM: 3 }, 0);
    expect(m.get('a')!.soakFactor).toBe(1.6);
    m.tick(3500);
    expect(m.get('a')!.soakMs).toBe(0);
    expect(m.get('a')!.soakFactor).toBe(1);
  });
});
