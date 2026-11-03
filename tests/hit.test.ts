import { describe, expect, it } from 'vitest';
import { WEAPONS } from '../src/config/weapons';
import {
  bodyFromLandmarks,
  classifyPoint,
  coverMapper,
  estimateDistanceM,
  evaluateShot,
  type BodyShape,
  type Landmark,
} from '../src/game/hit';

/** 정면으로 선 사람. 화면 가운데(0.5, 0.5) 근처가 몸통 중앙이고, s배로 크게(가깝게) 보인다 */
function standingPose(cx = 0.5, s = 1): Landmark[] {
  const lm: Landmark[] = Array.from({ length: 33 }, () => ({ x: 0, y: 0, visibility: 0 }));
  const set = (i: number, dx: number, y: number) => (lm[i] = { x: cx + dx * s, y: 0.5 + (y - 0.5) * s, visibility: 0.99 });
  // 얼굴
  set(0, 0, 0.22);
  for (let i = 1; i <= 6; i++) set(i, (i <= 3 ? -1 : 1) * 0.02, 0.2);
  set(7, -0.04, 0.21);
  set(8, 0.04, 0.21);
  set(9, -0.015, 0.25);
  set(10, 0.015, 0.25);
  // 어깨·팔
  set(11, -0.1, 0.32);
  set(12, 0.1, 0.32);
  set(13, -0.13, 0.45);
  set(14, 0.13, 0.45);
  set(15, -0.14, 0.58);
  set(16, 0.14, 0.58);
  // 골반·다리
  set(23, -0.07, 0.62);
  set(24, 0.07, 0.62);
  set(25, -0.07, 0.78);
  set(26, 0.07, 0.78);
  set(27, -0.07, 0.95);
  set(28, 0.07, 0.95);
  return lm;
}

const view = { videoW: 720, videoH: 1280, viewW: 720, viewH: 1280 };
const center = { x: 360, y: 640 };

describe('coverMapper', () => {
  it('세로 화면에 가로 영상을 채우면 좌우가 잘린다', () => {
    const map = coverMapper({ videoW: 1280, videoH: 720, viewW: 720, viewH: 1280 });
    // 높이 기준으로 1280/720배 확대 → 영상 가운데가 화면 가운데
    expect(map(0.5, 0.5)).toEqual({ x: 360, y: 640 });
    expect(map(0, 0).y).toBeCloseTo(0);
    expect(map(0, 0).x).toBeLessThan(0);
  });
});

describe('bodyFromLandmarks / classifyPoint', () => {
  const body = bodyFromLandmarks(standingPose(), view) as BodyShape;

  it('몸 모양을 만든다', () => {
    expect(body).not.toBeNull();
    expect(body.head).not.toBeNull();
    expect(body.torso).toHaveLength(4);
  });

  it('몸통 가운데는 body, 얼굴은 head, 빈 곳은 빗나감', () => {
    expect(classifyPoint(body, { x: 360, y: 0.45 * 1280 })).toBe('body');
    expect(classifyPoint(body, { x: 360, y: 0.21 * 1280 })).toBe('head');
    expect(classifyPoint(body, { x: 30, y: 640 })).toBeNull();
  });

  it('허용 반경 안이면 가장자리 근처도 맞는다', () => {
    // 오른팔 바깥쪽으로 약 37px 떨어진 점
    const nearEdge = { x: 360 + 0.17 * 720, y: 0.4 * 1280 };
    expect(classifyPoint(body, nearEdge, 0)).toBeNull();
    expect(classifyPoint(body, nearEdge, 30)).toBe('body');
  });

  it('보이지 않는 관절만 있으면 몸을 만들지 않는다', () => {
    const lm = standingPose().map((l) => ({ ...l, visibility: 0.1 }));
    expect(bodyFromLandmarks(lm, view)).toBeNull();
  });
});

describe('estimateDistanceM', () => {
  it('가까울수록(크게 보일수록) 거리가 짧다', () => {
    const near = estimateDistanceM(standingPose(0.5, 1), 720, 1280);
    const far = estimateDistanceM(standingPose(0.5, 0.25), 720, 1280);
    expect(near).toBeLessThan(far);
    expect(near).toBeGreaterThan(0.3);
    expect(far).toBeLessThan(20);
  });

  it('관절이 없으면 NaN', () => {
    expect(estimateDistanceM([], 720, 1280)).toBeNaN();
  });
});

describe('evaluateShot', () => {
  const minDim = 720;

  it('소총은 조준점이 몸 위에 있을 때만 맞는다', () => {
    const body = bodyFromLandmarks(standingPose(), view) as BodyShape;
    expect(evaluateShot([body], center, WEAPONS.m16, minDim)).toMatchObject({ bodyIndex: 0, body: 1, head: 0 });
    const offside = bodyFromLandmarks(standingPose(0.1), view) as BodyShape;
    expect(evaluateShot([offside], center, WEAPONS.m16, minDim)).toBeNull();
  });

  it('산탄은 알마다 따로 판정한다', () => {
    const body = bodyFromLandmarks(standingPose(), view) as BodyShape;
    let seed = 1;
    const rand = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
    const out = evaluateShot([body], center, WEAPONS.shotgun, minDim, rand);
    expect(out).not.toBeNull();
    expect(out!.head + out!.body).toBeGreaterThan(0);
    expect(out!.head + out!.body).toBeLessThanOrEqual(WEAPONS.shotgun.pellets!);
  });

  it('사거리 밖이면 빗나감', () => {
    const body = { ...(bodyFromLandmarks(standingPose(), view) as BodyShape), distanceM: 100 };
    expect(evaluateShot([body], center, WEAPONS.shotgun, minDim)).toBeNull();
  });

  it('여러 명이면 머리를 맞힌 쪽을 고른다', () => {
    const bodyShot = bodyFromLandmarks(standingPose(), view) as BodyShape;
    const headShot: BodyShape = { ...bodyShot, head: { c: center, r: 20 } };
    expect(evaluateShot([bodyShot, headShot], center, WEAPONS.k2, minDim)).toMatchObject({ bodyIndex: 1, head: 1 });
  });
});
