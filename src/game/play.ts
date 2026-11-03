// 한 판의 진행: 카메라 프레임마다 사람을 찾고, 방아쇠 입력을 받아 명중을 판정해 방장에게 보고하고,
// 방장이 돌려준 결과(HP·효과·킬)를 화면에 그린다. 가로·세로 회전은 매 프레임 크기를 다시 읽어 맞춘다.

import { CHARACTERS, WEAPONS, type CharacterId, type WeaponId } from '../config/weapons';
import * as sfx from '../fx/audio';
import { Overlay, type Projectile } from '../fx/overlay';
import type { GameLink } from '../net/link';
import type { PoseDetector } from '../vision/detector';
import { Gun, type GunConditions, type GunEvent } from './gun';
import { bodyFromLandmarks, classifyPoint, evaluateShot, type BodyShape } from './hit';
import type { HitEvent, MatchSnapshot, PlayerState } from './rules';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

const SKELETON_KEY = 'lens-strike:skeleton';

function loadFlag(key: string, fallback: boolean): boolean {
  try {
    const v = localStorage.getItem(key);
    return v === null ? fallback : v === '1';
  } catch {
    return fallback;
  }
}

function saveFlag(key: string, value: boolean): void {
  try {
    localStorage.setItem(key, value ? '1' : '0');
  } catch {
    // 저장소를 못 쓰는 환경(사생활 보호 모드 등)에서는 이번 실행에만 적용한다
  }
}

function vibrate(pattern: number | number[]): void {
  try {
    navigator.vibrate?.(pattern);
  } catch {
    // iOS Safari 등 진동 미지원
  }
}

export class Play {
  readonly gun: Gun;
  snapshot: MatchSnapshot | null = null;
  /** 경기 단계가 바뀔 때(대기실·진행·종료) */
  onPhase: (s: MatchSnapshot) => void = () => {};
  /** 방장이 보낸 상태를 받을 때마다 */
  onUpdate: (s: MatchSnapshot) => void = () => {};

  private overlay: Overlay;
  private bodies: BodyShape[] = [];
  private projectiles: Projectile[] = [];
  private showSkeleton = loadFlag(SKELETON_KEY, true);
  private raf = 0;
  private pointers = new Set<number>();
  private lastPhase: string | null = null;
  private wasAlive = true;
  private bannerTimer = 0;
  private hudCache = '';
  private disposers: (() => void)[] = [];

  constructor(
    readonly link: GameLink,
    character: CharacterId,
    private readonly video: HTMLVideoElement,
    private readonly detector: PoseDetector,
  ) {
    this.gun = new Gun(CHARACTERS[character].weapons);
    this.overlay = new Overlay($('fx'));
    link.onSnapshot = (s) => this.applySnapshot(s);
    link.onHit = (e) => this.applyHit(e);
    this.buildWeaponButtons();
    this.bindInput();
    $('hud').hidden = false;
    this.raf = requestAnimationFrame((t) => this.frame(t));
  }

  dispose(): void {
    cancelAnimationFrame(this.raf);
    window.clearTimeout(this.bannerTimer);
    for (const off of this.disposers) off();
    this.link.close();
    $('hud').hidden = true;
    $('dead').hidden = true;
    $('weapons').replaceChildren();
  }

  private me(): PlayerState | undefined {
    return this.snapshot?.players.find((p) => p.id === this.link.myId);
  }

  private opponent(): PlayerState | undefined {
    return this.snapshot?.players.find((p) => p.id !== this.link.myId);
  }

  private conditions(): GunConditions {
    const me = this.me();
    return {
      slowFactor: me?.soakFactor ?? 1,
      blocked: !me || !me.alive || me.shockMs > 0 || this.snapshot?.phase !== 'playing',
    };
  }

  // ---- 입력 ----

  private bindInput(): void {
    const stage = $('stage');
    const on = <K extends keyof HTMLElementEventMap>(el: HTMLElement, type: K, fn: (e: HTMLElementEventMap[K]) => void) => {
      el.addEventListener(type, fn as EventListener);
      this.disposers.push(() => el.removeEventListener(type, fn as EventListener));
    };
    on(stage, 'pointerdown', (e) => {
      e.preventDefault();
      sfx.unlockAudio();
      this.pointers.add(e.pointerId);
      stage.setPointerCapture?.(e.pointerId);
      if (this.pointers.size === 1) {
        if (this.gun.def.mode === 'charge' && !this.conditions().blocked) sfx.playCharge();
        this.handleGun(this.gun.press(performance.now(), this.conditions()));
      }
    });
    const up = (e: PointerEvent) => {
      if (!this.pointers.delete(e.pointerId)) return;
      if (this.pointers.size === 0) this.handleGun(this.gun.release(performance.now(), this.conditions()));
    };
    on(stage, 'pointerup', up);
    on(stage, 'pointercancel', up);
    on(stage, 'contextmenu', (e) => e.preventDefault());

    on($('btn-reload'), 'click', () => this.handleGun(this.gun.reload(performance.now())));
    const skel = $('btn-skeleton');
    skel.classList.toggle('on', this.showSkeleton);
    on(skel, 'click', () => {
      this.showSkeleton = !this.showSkeleton;
      saveFlag(SKELETON_KEY, this.showSkeleton);
      skel.classList.toggle('on', this.showSkeleton);
    });
  }

  private buildWeaponButtons(): void {
    const box = $('weapons');
    box.replaceChildren(
      ...this.gun.weapons.map((id) => {
        const b = document.createElement('button');
        b.className = 'weapon';
        b.dataset.weapon = id;
        b.style.setProperty('--c', WEAPONS[id].color);
        b.textContent = WEAPONS[id].name;
        b.addEventListener('click', () => {
          this.gun.select(id);
          this.syncWeaponButtons();
        });
        return b;
      }),
    );
    box.hidden = this.gun.weapons.length < 2;
    this.syncWeaponButtons();
  }

  private syncWeaponButtons(): void {
    for (const b of $('weapons').querySelectorAll<HTMLButtonElement>('.weapon')) {
      b.classList.toggle('active', b.dataset.weapon === this.gun.current);
    }
  }

  // ---- 사격 ----

  private handleGun(events: GunEvent[]): void {
    const now = performance.now();
    for (const e of events) {
      if (e.kind === 'fire') {
        const w = WEAPONS[e.weapon];
        sfx.playFire(e.weapon, e.charge);
        vibrate(w.mode === 'auto' ? 8 : 15);
        this.overlay.muzzle(e.weapon, now, e.charge);
        if (w.travelMs) this.projectiles.push({ weapon: e.weapon, charge: e.charge, start: now, arriveAt: now + w.travelMs });
        else this.resolveShot(e.weapon, e.charge);
      } else if (e.kind === 'empty') {
        sfx.playEmpty();
      } else if (e.kind === 'reloadStart') {
        sfx.playReload();
      } else if (e.kind === 'reloadDone') {
        sfx.playReload();
      }
    }
  }

  /** 지금 화면 기준으로 명중을 판정하고, 맞았으면 방장에게 보고한다 */
  private resolveShot(weapon: WeaponId, charge: number): void {
    const w = WEAPONS[weapon];
    const aim = this.overlay.aim;
    const outcome = evaluateShot(this.bodies, aim, w, this.overlay.minDim);
    if (outcome) {
      this.link.shoot({ weapon, head: outcome.head, body: outcome.body, distanceM: outcome.distanceM, charge });
      return;
    }
    const tol = w.aimRadius * this.overlay.minDim;
    if (this.bodies.some((b) => b.distanceM > w.maxRangeM && classifyPoint(b, aim, tol))) {
      this.overlay.float(performance.now(), '사거리 밖', '#bbbbbb');
    }
  }

  // ---- 방장 결과 반영 ----

  private applyHit(e: HitEvent): void {
    const now = performance.now();
    const mine = this.link.myId;
    if (e.from === mine && e.zone !== 'burn') {
      const head = e.zone === 'head';
      this.overlay.hitMarker(now, head, e.killed);
      this.overlay.float(now, `${head ? '헤드샷 ' : ''}-${Math.round(e.damage)}`, head ? '#ffd166' : '#ffffff');
      sfx.playHitMarker(head);
      if (e.killed) {
        sfx.playKill();
        this.banner('처치!', 'kill');
      }
    }
    if (e.to === mine) {
      if (e.zone !== 'burn') {
        this.overlay.hurt(now);
        sfx.playHurt();
        vibrate(70);
        if (e.effect === 'soak') {
          this.overlay.splash();
          sfx.playSplash();
        }
      }
      if (e.killed) {
        sfx.playDeath();
        vibrate([120, 60, 240]);
      }
    }
  }

  private applySnapshot(s: MatchSnapshot): void {
    this.snapshot = s;
    if (s.phase !== this.lastPhase) {
      this.lastPhase = s.phase;
      this.onPhase(s);
    }
    this.onUpdate(s);
    const me = this.me();
    if (me && me.alive !== this.wasAlive) {
      this.wasAlive = me.alive;
      document.body.classList.toggle('is-dead', !me.alive);
    }
    this.updateHud();
  }

  private updateHud(): void {
    const s = this.snapshot;
    const me = this.me();
    const opp = this.opponent();
    const key = JSON.stringify([
      s?.phase, me?.hp, me?.kills, me?.alive, Math.ceil((me?.respawnMs ?? 0) / 1000), me?.shockMs ? 1 : 0, me?.soakMs ? 1 : 0, me?.burnMs ? 1 : 0,
      opp?.id, opp?.hp, opp?.kills, opp?.name,
    ]);
    if (key === this.hudCache) return;
    this.hudCache = key;

    const bar = (prefix: string, p: PlayerState | undefined) => {
      $(`${prefix}-name`).textContent = p ? `${p.name} · ${CHARACTERS[p.character].name}` : '상대 기다리는 중';
      const ratio = p ? Math.max(0, p.hp / p.maxHp) : 0;
      $(`${prefix}-hp`).style.width = `${ratio * 100}%`;
      $(`${prefix}-hp`).classList.toggle('low', ratio < 0.3);
      $(`${prefix}-hp-text`).textContent = p ? `${Math.ceil(p.hp)}` : '';
    };
    bar('me', me);
    bar('opp', opp);

    const target = s?.killTarget ?? 0;
    $('score').textContent = target > 0 ? `${me?.kills ?? 0} : ${opp?.kills ?? 0}` : `처치 ${me?.kills ?? 0}`;
    $('score-sub').textContent = target > 0 ? `${target}킬 승리` : '연습 모드';

    const dead = !!me && !me.alive && s?.phase === 'playing';
    $('dead').hidden = !dead;
    if (dead) $('respawn').textContent = `${Math.max(1, Math.ceil(me.respawnMs / 1000))}초 뒤 부활`;

    const status: string[] = [];
    if (me?.shockMs) status.push('⚡ 감전 — 사격 불가');
    if (me?.soakMs) status.push('💧 젖음 — 연사 느려짐');
    if (me?.burnMs) status.push('🔥 화상');
    $('status').textContent = status.join('  ');
  }

  private banner(text: string, kind: string): void {
    const el = $('banner');
    el.textContent = text;
    el.className = `banner show ${kind}`;
    window.clearTimeout(this.bannerTimer);
    this.bannerTimer = window.setTimeout(() => (el.className = 'banner'), 1100);
  }

  // ---- 프레임 ----

  private frame(now: number): void {
    this.raf = requestAnimationFrame((t) => this.frame(t));
    this.overlay.fit();
    const { w, h } = this.overlay.size;
    if (this.video.videoWidth > 0 && w > 0) {
      // 회전하면 카메라 영상의 가로세로도 바뀌므로 매번 다시 읽는다
      const view = { videoW: this.video.videoWidth, videoH: this.video.videoHeight, viewW: w, viewH: h };
      this.bodies = this.detector
        .detect(this.video, now)
        .map((lm) => bodyFromLandmarks(lm, view))
        .filter((b): b is BodyShape => b !== null);
    }

    const cond = this.conditions();
    this.handleGun(this.gun.update(now, cond));
    const arrived = this.projectiles.filter((p) => now >= p.arriveAt);
    if (arrived.length) {
      this.projectiles = this.projectiles.filter((p) => now < p.arriveAt);
      // 날아가는 동안 쓰러졌거나 경기가 끝났으면 무효(감전은 이미 쏜 탄에 영향 없음)
      if (this.me()?.alive && this.snapshot?.phase === 'playing') for (const p of arrived) this.resolveShot(p.weapon, p.charge);
    }

    const me = this.me();
    this.overlay.draw(now, {
      bodies: this.bodies,
      showSkeleton: this.showSkeleton,
      weapon: this.gun.def,
      charge: this.gun.chargeLevel(now),
      reload: this.gun.reloadProgress(now),
      projectiles: this.projectiles,
      canShoot: !cond.blocked,
      effects: {
        burn: (me?.burnMs ?? 0) > 0,
        soak: (me?.soakMs ?? 0) > 0,
        shock: (me?.shockMs ?? 0) > 0,
        protect: (me?.protectMs ?? 0) > 0,
      },
    });

    const ammo = this.gun.ammo[this.gun.current];
    const ammoText = this.gun.isReloading(now) ? '재장전 중…' : `${ammo} / ${this.gun.def.magazine}`;
    const ammoEl = $('ammo');
    if (ammoEl.textContent !== ammoText) ammoEl.textContent = ammoText;
    const seen = this.bodies.length ? `사람 ${this.bodies.length}명 인식` : '사람을 비추세요';
    const seenEl = $('seen');
    if (seenEl.textContent !== seen) seenEl.textContent = seen;
  }
}
