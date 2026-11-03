// 사람 인식 결과(MediaPipe Pose 33개 관절)를 화면 위의 몸 모양으로 바꾸고, 조준점이 어디에 맞았는지 판정한다.
// 순수 함수만 두어 테스트에서 그대로 검증한다.

import type { WeaponDef } from '../config/weapons';

export interface Pt {
  x: number;
  y: number;
}

/** MediaPipe NormalizedLandmark와 같은 모양(영상 기준 0~1 좌표) */
export interface Landmark {
  x: number;
  y: number;
  visibility?: number;
}

export interface BodyShape {
  head: { c: Pt; r: number } | null;
  torso: Pt[] | null;
  segments: [Pt, Pt][];
  /** 팔다리·목 선분의 반지름(px) */
  thickness: number;
  /** 카메라에서 떨어진 추정 거리(m). 추정할 수 없으면 NaN */
  distanceM: number;
}

export type Zone = 'head' | 'body';

export interface ShotOutcome {
  /** 맞은 몸 번호(bodies 배열 기준) */
  bodyIndex: number;
  head: number;
  body: number;
  distanceM: number;
}

/** 영상 프레임이 object-fit: cover로 화면을 채울 때의 좌표 변환 */
export interface CoverView {
  videoW: number;
  videoH: number;
  viewW: number;
  viewH: number;
}

export function coverMapper(v: CoverView): (nx: number, ny: number) => Pt {
  const scale = Math.max(v.viewW / v.videoW, v.viewH / v.videoH);
  const dx = (v.viewW - v.videoW * scale) / 2;
  const dy = (v.viewH - v.videoH * scale) / 2;
  return (nx, ny) => ({ x: nx * v.videoW * scale + dx, y: ny * v.videoH * scale + dy });
}

// MediaPipe Pose 관절 번호
const NOSE = 0;
const FACE = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
const L_EAR = 7;
const R_EAR = 8;
const L_SHOULDER = 11;
const R_SHOULDER = 12;
const L_HIP = 23;
const R_HIP = 24;
const LIMBS: [number, number][] = [
  [11, 13], [13, 15], [12, 14], [14, 16],
  [23, 25], [25, 27], [24, 26], [26, 28],
];

const VISIBLE = 0.5;
// 성인 평균 치수(m). 거리 추정용 대략값이다.
const SHOULDER_WIDTH_M = 0.38;
const TORSO_HEIGHT_M = 0.5;
const EAR_WIDTH_M = 0.15;
/** 폰 기본 카메라의 대각선 화각(대략 26mm 환산). 기기마다 다르지만 사거리 감쇠에만 쓰므로 충분하다. */
export const DEFAULT_DIAGONAL_FOV_DEG = 78;

const dist = (a: Pt, b: Pt) => Math.hypot(a.x - b.x, a.y - b.y);
const mid = (a: Pt, b: Pt): Pt => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });

export function distToSegment(p: Pt, a: Pt, b: Pt): number {
  const vx = b.x - a.x;
  const vy = b.y - a.y;
  const len2 = vx * vx + vy * vy;
  const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * vx + (p.y - a.y) * vy) / len2));
  return Math.hypot(p.x - (a.x + t * vx), p.y - (a.y + t * vy));
}

export function pointInPolygon(p: Pt, poly: Pt[]): boolean {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const a = poly[i];
    const b = poly[j];
    if (a.y > p.y !== b.y > p.y && p.x < ((b.x - a.x) * (p.y - a.y)) / (b.y - a.y) + a.x) inside = !inside;
  }
  return inside;
}

/**
 * 관절 좌표로 거리(m)를 추정한다. 어깨너비·몸통 길이 중 화면에서 더 크게 보이는 쪽을 쓴다
 * (옆으로 서면 어깨너비가 줄어 거리를 부풀리기 때문). 둘 다 없으면 귀 사이 거리를 쓴다.
 */
export function estimateDistanceM(lm: Landmark[], videoW: number, videoH: number, diagFovDeg = DEFAULT_DIAGONAL_FOV_DEG): number {
  const px = (i: number): Pt | null => {
    const l = lm[i];
    return l && (l.visibility ?? 1) >= VISIBLE ? { x: l.x * videoW, y: l.y * videoH } : null;
  };
  const focalPx = Math.hypot(videoW, videoH) / (2 * Math.tan((diagFovDeg * Math.PI) / 360));
  const ls = px(L_SHOULDER);
  const rs = px(R_SHOULDER);
  const lh = px(L_HIP);
  const rh = px(R_HIP);
  let pxPerM = 0;
  if (ls && rs) pxPerM = Math.max(pxPerM, dist(ls, rs) / SHOULDER_WIDTH_M);
  if (ls && rs && lh && rh) pxPerM = Math.max(pxPerM, dist(mid(ls, rs), mid(lh, rh)) / TORSO_HEIGHT_M);
  if (pxPerM === 0) {
    const le = px(L_EAR);
    const re = px(R_EAR);
    if (le && re) pxPerM = dist(le, re) / EAR_WIDTH_M;
  }
  return pxPerM > 0 ? focalPx / pxPerM : NaN;
}

export function bodyFromLandmarks(lm: Landmark[], view: CoverView, diagFovDeg = DEFAULT_DIAGONAL_FOV_DEG): BodyShape | null {
  const map = coverMapper(view);
  const pt = (i: number): Pt | null => {
    const l = lm[i];
    return l && (l.visibility ?? 1) >= VISIBLE ? map(l.x, l.y) : null;
  };

  const ls = pt(L_SHOULDER);
  const rs = pt(R_SHOULDER);
  const lh = pt(L_HIP);
  const rh = pt(R_HIP);
  const face = FACE.map(pt).filter((p): p is Pt => p !== null);
  const le = pt(L_EAR);
  const re = pt(R_EAR);

  const shoulderPx = ls && rs ? dist(ls, rs) : 0;
  const earPx = le && re ? dist(le, re) : 0;
  // 기준 크기: 어깨너비, 없으면 귀 사이 거리로 대신한다
  const unit = shoulderPx || earPx * 2.5;
  if (unit <= 0 && face.length < 3) return null;

  let head: BodyShape['head'] = null;
  if (face.length >= 3) {
    const c = {
      x: face.reduce((s, p) => s + p.x, 0) / face.length,
      y: face.reduce((s, p) => s + p.y, 0) / face.length,
    };
    const r = earPx > 0 ? earPx * 0.75 : shoulderPx > 0 ? shoulderPx * 0.3 : Math.max(...face.map((p) => dist(p, c))) * 1.6;
    head = { c, r };
  }

  const torso = ls && rs && rh && lh ? [ls, rs, rh, lh] : null;
  const segments: [Pt, Pt][] = [];
  for (const [a, b] of LIMBS) {
    const pa = pt(a);
    const pb = pt(b);
    if (pa && pb) segments.push([pa, pb]);
  }
  // 몸통 사각형이 없을 때도 어깨선·몸통 중심선으로 맞힐 수 있게 한다
  if (ls && rs) segments.push([ls, rs]);
  if (ls && lh) segments.push([ls, lh]);
  if (rs && rh) segments.push([rs, rh]);
  const nose = pt(NOSE);
  if (nose && ls && rs) segments.push([nose, mid(ls, rs)]);

  return {
    head,
    torso,
    segments,
    thickness: Math.max(unit * 0.12, 6),
    distanceM: estimateDistanceM(lm, view.videoW, view.videoH, diagFovDeg),
  };
}

/** 조준점 p(허용 반경 tol)가 몸의 어디에 닿는지. 머리가 우선한다. */
export function classifyPoint(body: BodyShape, p: Pt, tol = 0): Zone | null {
  if (body.head && dist(p, body.head.c) <= body.head.r + tol) return 'head';
  if (body.torso) {
    if (pointInPolygon(p, body.torso)) return 'body';
    for (let i = 0; i < body.torso.length; i++) {
      if (distToSegment(p, body.torso[i], body.torso[(i + 1) % body.torso.length]) <= tol) return 'body';
    }
  }
  for (const [a, b] of body.segments) {
    if (distToSegment(p, a, b) <= body.thickness + tol) return 'body';
  }
  return null;
}

/** 산탄 한 알의 위치: 조준 원 안에 고르게 흩어진다 */
export function pelletPoint(aim: Pt, radiusPx: number, rand: () => number): Pt {
  const r = radiusPx * Math.sqrt(rand());
  const a = rand() * Math.PI * 2;
  return { x: aim.x + r * Math.cos(a), y: aim.y + r * Math.sin(a) };
}

/**
 * 한 발(산탄이면 한 방)의 결과. 여러 사람이 보이면 가장 많이 맞은(같으면 머리를 맞은) 사람을 고른다.
 * 사거리 밖이면 빗나감으로 본다.
 */
export function evaluateShot(
  bodies: BodyShape[],
  aim: Pt,
  weapon: WeaponDef,
  minDimPx: number,
  rand: () => number = Math.random,
): ShotOutcome | null {
  const radiusPx = weapon.aimRadius * minDimPx;
  const points = weapon.pellets ? Array.from({ length: weapon.pellets }, () => pelletPoint(aim, radiusPx, rand)) : null;

  let best: ShotOutcome | null = null;
  const score = (o: ShotOutcome) => (o.head + o.body) * 10 + o.head;
  bodies.forEach((body, bodyIndex) => {
    if (Number.isFinite(body.distanceM) && body.distanceM > weapon.maxRangeM) return;
    const outcome: ShotOutcome = { bodyIndex, head: 0, body: 0, distanceM: body.distanceM };
    if (points) {
      for (const p of points) {
        const z = classifyPoint(body, p);
        if (z) outcome[z] += 1;
      }
    } else {
      const z = classifyPoint(body, aim, radiusPx);
      if (z) outcome[z] = 1;
    }
    if (outcome.head + outcome.body > 0 && (!best || score(outcome) > score(best))) best = outcome;
  });
  return best;
}
