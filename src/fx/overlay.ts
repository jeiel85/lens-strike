// 카메라 화면 위에 그리는 것들: 조준선, 총구 화염, 날아가는 마법탄, 명중 표시, 피격 효과.

import { WEAPONS, type WeaponDef, type WeaponId } from '../config/weapons';
import type { BodyShape, Pt } from '../game/hit';

interface Flash {
  weapon: WeaponId;
  at: number;
  charge: number;
}

interface Marker {
  at: number;
  head: boolean;
  kill: boolean;
}

interface Floater {
  at: number;
  text: string;
  color: string;
  x: number;
  y: number;
}

export interface Projectile {
  weapon: WeaponId;
  charge: number;
  start: number;
  arriveAt: number;
}

export interface MyEffects {
  burn: boolean;
  soak: boolean;
  shock: boolean;
  protect: boolean;
}

interface Drop {
  x: number;
  y: number;
  r: number;
}

export class Overlay {
  private ctx: CanvasRenderingContext2D;
  private w = 0;
  private h = 0;
  private flashes: Flash[] = [];
  private markers: Marker[] = [];
  private floaters: Floater[] = [];
  private hurtAt = -1e9;
  private drops: Drop[] = [];

  constructor(private readonly canvas: HTMLCanvasElement) {
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('캔버스를 만들 수 없습니다.');
    this.ctx = ctx;
  }

  get size(): { w: number; h: number } {
    return { w: this.w, h: this.h };
  }

  get aim(): Pt {
    return { x: this.w / 2, y: this.h / 2 };
  }

  get minDim(): number {
    return Math.min(this.w, this.h);
  }

  /** 화면 크기·방향이 바뀌면 다시 맞춘다(가로/세로 회전 포함) */
  fit(): void {
    const rect = this.canvas.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    if (rect.width === this.w && rect.height === this.h && this.canvas.width === Math.round(rect.width * dpr)) return;
    this.w = rect.width;
    this.h = rect.height;
    this.canvas.width = Math.round(rect.width * dpr);
    this.canvas.height = Math.round(rect.height * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  muzzle(weapon: WeaponId, now: number, charge = 1): void {
    this.flashes.push({ weapon, at: now, charge });
  }

  hitMarker(now: number, head: boolean, kill: boolean): void {
    this.markers.push({ at: now, head, kill });
  }

  float(now: number, text: string, color: string, at?: Pt): void {
    const p = at ?? this.aim;
    this.floaters.push({ at: now, text, color, x: p.x + (Math.random() - 0.5) * 40, y: p.y - 30 });
  }

  hurt(now: number): void {
    this.hurtAt = now;
  }

  splash(): void {
    this.drops = Array.from({ length: 18 }, () => ({
      x: Math.random() * this.w,
      y: Math.random() * this.h,
      r: 12 + Math.random() * 38,
    }));
  }

  draw(now: number, o: {
    bodies: BodyShape[];
    showSkeleton: boolean;
    weapon: WeaponDef;
    charge: number;
    reload: number;
    projectiles: Projectile[];
    effects: MyEffects;
    canShoot: boolean;
  }): void {
    const { ctx, w, h } = this;
    ctx.clearRect(0, 0, w, h);
    if (o.showSkeleton) for (const b of o.bodies) this.drawBody(b);
    for (const p of o.projectiles) this.drawProjectile(p, now);
    this.drawFlashes(now);
    this.drawCrosshair(o.weapon, o.charge, o.reload, o.canShoot, o.bodies.length > 0);
    this.drawMarkers(now);
    this.drawFloaters(now);
    this.drawEffects(now, o.effects);
  }

  private drawBody(b: BodyShape): void {
    const { ctx } = this;
    ctx.save();
    ctx.strokeStyle = 'rgba(120, 255, 180, 0.7)';
    ctx.fillStyle = 'rgba(120, 255, 180, 0.12)';
    ctx.lineWidth = 2;
    if (b.torso) {
      ctx.beginPath();
      b.torso.forEach((p, i) => (i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y)));
      ctx.closePath();
      ctx.fill();
      ctx.stroke();
    }
    ctx.lineCap = 'round';
    ctx.lineWidth = b.thickness * 2;
    ctx.strokeStyle = 'rgba(120, 255, 180, 0.18)';
    for (const [a, c] of b.segments) {
      ctx.beginPath();
      ctx.moveTo(a.x, a.y);
      ctx.lineTo(c.x, c.y);
      ctx.stroke();
    }
    if (b.head) {
      ctx.lineWidth = 2;
      ctx.strokeStyle = 'rgba(255, 220, 120, 0.8)';
      ctx.beginPath();
      ctx.arc(b.head.c.x, b.head.c.y, b.head.r, 0, Math.PI * 2);
      ctx.stroke();
    }
    if (Number.isFinite(b.distanceM) && b.head) {
      ctx.fillStyle = 'rgba(255,255,255,0.85)';
      ctx.font = '600 13px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText(`${b.distanceM.toFixed(1)}m`, b.head.c.x, b.head.c.y - b.head.r - 8);
    }
    ctx.restore();
  }

  private drawCrosshair(weapon: WeaponDef, charge: number, reload: number, canShoot: boolean, seesPerson: boolean): void {
    const { ctx } = this;
    const { x, y } = this.aim;
    const r = Math.max(weapon.aimRadius * this.minDim, 6);
    const color = !canShoot ? 'rgba(255,255,255,0.35)' : seesPerson ? weapon.color : 'rgba(255,255,255,0.9)';
    ctx.save();
    ctx.strokeStyle = color;
    ctx.fillStyle = color;
    ctx.lineWidth = 2;
    ctx.shadowColor = 'rgba(0,0,0,0.6)';
    ctx.shadowBlur = 4;

    // 조준 원(판정 반경)
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.stroke();
    // 가운데 점과 십자
    ctx.beginPath();
    ctx.arc(x, y, 2.5, 0, Math.PI * 2);
    ctx.fill();
    const gap = r + 6;
    const len = 14;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      ctx.beginPath();
      ctx.moveTo(x + dx * gap, y + dy * gap);
      ctx.lineTo(x + dx * (gap + len), y + dy * (gap + len));
      ctx.stroke();
    }
    // 충전량
    if (charge > 0) {
      ctx.lineWidth = 5;
      ctx.strokeStyle = charge >= 1 ? '#ffffff' : weapon.color;
      ctx.beginPath();
      ctx.arc(x, y, gap + len + 8, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * charge);
      ctx.stroke();
    }
    // 재장전
    if (reload < 1) {
      ctx.lineWidth = 4;
      ctx.strokeStyle = 'rgba(255,255,255,0.8)';
      ctx.beginPath();
      ctx.arc(x, y, gap + len + 8, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * reload);
      ctx.stroke();
    }
    ctx.restore();
  }

  private drawFlashes(now: number): void {
    const { ctx, w, h } = this;
    this.flashes = this.flashes.filter((f) => now - f.at < 160);
    for (const f of this.flashes) {
      const t = (now - f.at) / 160;
      const weapon = WEAPONS[f.weapon];
      ctx.save();
      ctx.globalAlpha = 1 - t;
      if (f.weapon === 'plasma') {
        // 화면 아래에서 조준점까지 뻗는 빔
        ctx.strokeStyle = weapon.color;
        ctx.lineWidth = 4 + 10 * f.charge * (1 - t);
        ctx.shadowColor = weapon.color;
        ctx.shadowBlur = 20;
        ctx.beginPath();
        ctx.moveTo(w * 0.7, h);
        ctx.lineTo(w / 2, h / 2);
        ctx.stroke();
      } else if (f.weapon !== 'fireball' && f.weapon !== 'waterball') {
        // 총구 화염: 화면 아래 오른쪽
        const g = ctx.createRadialGradient(w * 0.72, h, 0, w * 0.72, h, this.minDim * (f.weapon === 'shotgun' ? 0.5 : 0.3));
        g.addColorStop(0, 'rgba(255,240,200,0.95)');
        g.addColorStop(0.4, 'rgba(255,170,60,0.6)');
        g.addColorStop(1, 'rgba(255,120,0,0)');
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, w, h);
      }
      ctx.restore();
    }
  }

  private drawProjectile(p: Projectile, now: number): void {
    const { ctx, w, h } = this;
    const t = Math.min(1, (now - p.start) / (p.arriveAt - p.start));
    const from = { x: w * 0.6, y: h + 20 };
    const to = this.aim;
    // 멀어질수록 작아지게(원근감)
    const x = from.x + (to.x - from.x) * t;
    const y = from.y + (to.y - from.y) * t;
    const r = this.minDim * (0.12 - 0.09 * t);
    const color = WEAPONS[p.weapon].color;
    ctx.save();
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    if (p.weapon === 'fireball') {
      g.addColorStop(0, 'rgba(255,250,210,1)');
      g.addColorStop(0.35, 'rgba(255,170,40,0.95)');
      g.addColorStop(1, 'rgba(255,60,0,0)');
    } else {
      g.addColorStop(0, 'rgba(230,250,255,1)');
      g.addColorStop(0.4, 'rgba(80,170,255,0.9)');
      g.addColorStop(1, 'rgba(30,90,255,0)');
    }
    ctx.fillStyle = g;
    ctx.shadowColor = color;
    ctx.shadowBlur = 25;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  private drawMarkers(now: number): void {
    const { ctx } = this;
    const { x, y } = this.aim;
    this.markers = this.markers.filter((m) => now - m.at < 350);
    for (const m of this.markers) {
      const t = (now - m.at) / 350;
      ctx.save();
      ctx.globalAlpha = 1 - t;
      ctx.strokeStyle = m.kill ? '#ff3b3b' : m.head ? '#ffd166' : '#ffffff';
      ctx.lineWidth = m.kill ? 4 : 3;
      const a = 10 + t * 6;
      const b = 22 + t * 6;
      for (const [dx, dy] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
        ctx.beginPath();
        ctx.moveTo(x + dx * a, y + dy * a);
        ctx.lineTo(x + dx * b, y + dy * b);
        ctx.stroke();
      }
      ctx.restore();
    }
  }

  private drawFloaters(now: number): void {
    const { ctx } = this;
    this.floaters = this.floaters.filter((f) => now - f.at < 900);
    for (const f of this.floaters) {
      const t = (now - f.at) / 900;
      ctx.save();
      ctx.globalAlpha = 1 - t * t;
      ctx.fillStyle = f.color;
      ctx.strokeStyle = 'rgba(0,0,0,0.7)';
      ctx.lineWidth = 4;
      ctx.font = '800 22px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.strokeText(f.text, f.x, f.y - t * 50);
      ctx.fillText(f.text, f.x, f.y - t * 50);
      ctx.restore();
    }
  }

  private drawEffects(now: number, e: MyEffects): void {
    const { ctx, w, h } = this;
    const hurtT = (now - this.hurtAt) / 400;
    if (hurtT < 1) vignette(ctx, w, h, `rgba(255,0,0,${0.55 * (1 - hurtT)})`);
    if (e.burn) {
      const flicker = 0.35 + 0.15 * Math.sin(now / 60) + 0.1 * Math.sin(now / 23);
      vignette(ctx, w, h, `rgba(255,110,0,${flicker})`);
    }
    if (e.soak) {
      ctx.save();
      for (const d of this.drops) {
        const g = ctx.createRadialGradient(d.x - d.r * 0.3, d.y - d.r * 0.3, 0, d.x, d.y, d.r);
        g.addColorStop(0, 'rgba(255,255,255,0.55)');
        g.addColorStop(0.6, 'rgba(120,180,255,0.3)');
        g.addColorStop(1, 'rgba(60,120,255,0.1)');
        ctx.fillStyle = g;
        ctx.beginPath();
        ctx.arc(d.x, d.y, d.r, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
      vignette(ctx, w, h, 'rgba(40,110,255,0.4)');
    }
    if (e.shock) {
      ctx.save();
      ctx.strokeStyle = 'rgba(160,230,255,0.8)';
      ctx.lineWidth = 2;
      for (let i = 0; i < 6; i++) {
        let x = Math.random() * w;
        let y = 0;
        ctx.beginPath();
        ctx.moveTo(x, y);
        while (y < h) {
          x += (Math.random() - 0.5) * 60;
          y += 20 + Math.random() * 40;
          ctx.lineTo(x, y);
        }
        ctx.stroke();
      }
      ctx.fillStyle = 'rgba(180,230,255,0.12)';
      ctx.fillRect(0, 0, w, h);
      ctx.restore();
    }
    if (e.protect) {
      ctx.save();
      ctx.strokeStyle = `rgba(255,255,255,${0.35 + 0.25 * Math.sin(now / 90)})`;
      ctx.lineWidth = 8;
      ctx.strokeRect(4, 4, w - 8, h - 8);
      ctx.restore();
    }
  }
}

function vignette(ctx: CanvasRenderingContext2D, w: number, h: number, color: string): void {
  const r = Math.hypot(w, h) / 2;
  const g = ctx.createRadialGradient(w / 2, h / 2, r * 0.45, w / 2, h / 2, r);
  g.addColorStop(0, 'rgba(0,0,0,0)');
  g.addColorStop(1, color);
  ctx.save();
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);
  ctx.restore();
}
