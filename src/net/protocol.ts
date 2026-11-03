// 방장(host) ↔ 참가자(guest) 메시지. 방장 기기가 경기 규칙(Match)을 돌린다.

import { isCharacterId, isWeaponId, type CharacterId } from '../config/weapons';
import type { HitEvent, MatchSnapshot, ShotReport } from '../game/rules';

export type ToHost =
  | { t: 'hello'; name: string; character: CharacterId }
  | { t: 'shot'; shot: ShotReport };

export type ToGuest =
  | { t: 'welcome'; id: string }
  | { t: 'full' }
  | { t: 'snap'; s: MatchSnapshot }
  | { t: 'hit'; e: HitEvent };

const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);

export function cleanName(v: unknown): string {
  const s = typeof v === 'string' ? v.replace(/[\u0000-\u001f]/g, '').trim().slice(0, 12) : '';
  return s || '이름없음';
}

/** 참가자가 보낸 메시지를 검사한다. 모양이 틀리면 null. */
export function parseToHost(raw: unknown): ToHost | null {
  if (!raw || typeof raw !== 'object') return null;
  const m = raw as Record<string, unknown>;
  if (m.t === 'hello' && isCharacterId(m.character)) {
    return { t: 'hello', name: cleanName(m.name), character: m.character };
  }
  if (m.t === 'shot' && m.shot && typeof m.shot === 'object') {
    const s = m.shot as Record<string, unknown>;
    if (!isWeaponId(s.weapon) || !num(s.head) || !num(s.body)) return null;
    // 거리를 못 잰 경우 NaN은 JSON에서 null이 되므로 그대로 NaN으로 되돌린다
    const distanceM = num(s.distanceM) ? s.distanceM : NaN;
    return {
      t: 'shot',
      shot: { weapon: s.weapon, head: s.head, body: s.body, distanceM, charge: num(s.charge) ? s.charge : undefined },
    };
  }
  return null;
}
