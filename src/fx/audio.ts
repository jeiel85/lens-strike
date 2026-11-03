// 효과음을 Web Audio로 합성한다(음원 파일 없음). iOS는 사용자 터치 뒤에만 소리가 나므로 unlock()을 버튼 클릭에서 부른다.

import type { WeaponId } from '../config/weapons';

let ctx: AudioContext | null = null;
let noiseBuf: AudioBuffer | null = null;

export function unlockAudio(): void {
  if (!ctx) {
    const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctor) return;
    ctx = new Ctor();
    noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
    const data = noiseBuf.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
  }
  if (ctx.state === 'suspended') void ctx.resume();
}

function env(gain: GainNode, t: number, peak: number, attack: number, decay: number): void {
  gain.gain.setValueAtTime(0.0001, t);
  gain.gain.exponentialRampToValueAtTime(peak, t + attack);
  gain.gain.exponentialRampToValueAtTime(0.0001, t + attack + decay);
}

function noise(duration: number, filterFreq: number, peak: number, type: BiquadFilterType = 'lowpass', delay = 0): void {
  if (!ctx || !noiseBuf) return;
  const t = ctx.currentTime + delay;
  const src = ctx.createBufferSource();
  src.buffer = noiseBuf;
  const filter = ctx.createBiquadFilter();
  filter.type = type;
  filter.frequency.value = filterFreq;
  const g = ctx.createGain();
  env(g, t, peak, 0.004, duration);
  src.connect(filter).connect(g).connect(ctx.destination);
  src.start(t);
  src.stop(t + duration + 0.05);
}

function tone(type: OscillatorType, from: number, to: number, duration: number, peak: number, delay = 0): void {
  if (!ctx) return;
  const t = ctx.currentTime + delay;
  const osc = ctx.createOscillator();
  osc.type = type;
  osc.frequency.setValueAtTime(from, t);
  osc.frequency.exponentialRampToValueAtTime(Math.max(20, to), t + duration);
  const g = ctx.createGain();
  env(g, t, peak, 0.005, duration);
  osc.connect(g).connect(ctx.destination);
  osc.start(t);
  osc.stop(t + duration + 0.05);
}

export function playFire(weapon: WeaponId, charge = 1): void {
  switch (weapon) {
    case 'm16':
    case 'k2':
      noise(0.09, 2400, 0.5);
      tone('square', 180, 60, 0.06, 0.15);
      break;
    case 'shotgun':
      noise(0.28, 1200, 0.8);
      tone('sine', 120, 40, 0.2, 0.4);
      break;
    case 'plasma':
      tone('sawtooth', 900 + 600 * charge, 200, 0.25, 0.25);
      tone('sine', 1800, 400, 0.18, 0.15);
      break;
    case 'fireball':
      noise(0.45, 600, 0.45);
      tone('triangle', 220, 90, 0.4, 0.2);
      break;
    case 'waterball':
      tone('sine', 300, 900, 0.18, 0.3);
      noise(0.2, 3000, 0.15, 'highpass', 0.05);
      break;
  }
}

export function playCharge(): void {
  tone('sine', 300, 1200, 0.5, 0.08);
}

export function playEmpty(): void {
  tone('square', 900, 700, 0.04, 0.08);
}

export function playReload(): void {
  noise(0.05, 4000, 0.25, 'highpass');
  noise(0.05, 3000, 0.25, 'highpass', 0.18);
}

export function playHitMarker(head: boolean): void {
  tone('square', head ? 1800 : 1200, head ? 1600 : 1000, 0.06, 0.12);
  if (head) tone('sine', 2400, 2400, 0.08, 0.08, 0.05);
}

export function playKill(): void {
  tone('triangle', 600, 600, 0.1, 0.2);
  tone('triangle', 900, 900, 0.18, 0.2, 0.1);
}

export function playHurt(): void {
  tone('sawtooth', 160, 70, 0.18, 0.25);
  noise(0.12, 500, 0.3);
}

export function playDeath(): void {
  tone('sawtooth', 300, 40, 0.8, 0.3);
}

export function playSplash(): void {
  noise(0.3, 2500, 0.3, 'bandpass');
}
