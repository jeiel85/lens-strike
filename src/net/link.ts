// 경기 연결. 방장은 경기 규칙을 직접 돌리고(HostLink), 참가자는 방장에게 붙는다(GuestLink).
// 연습 모드는 상대 없이 HostLink에 훈련 표적을 넣어 같은 규칙으로 돌린다.
// 기기 간 연결은 PeerJS(WebRTC 데이터 채널, 공개 시그널링·TURN 서버)를 쓴다.

import Peer, { type DataConnection, type PeerError } from 'peerjs';
import type { CharacterId } from '../config/weapons';
import { DEFAULT_MATCH, Match, type HitEvent, type MatchSnapshot, type ShotReport } from '../game/rules';
import { parseToHost, type ToGuest, type ToHost } from './protocol';

export interface GameLink {
  readonly myId: string;
  readonly isHost: boolean;
  onSnapshot: (s: MatchSnapshot) => void;
  onHit: (e: HitEvent) => void;
  /** 연결 상태 안내. fatal이면 경기를 이어갈 수 없다. */
  onStatus: (message: string, fatal: boolean) => void;
  shoot(shot: ShotReport): void;
  close(): void;
}

const PEER_PREFIX = 'lens-strike-';
const CODE_CHARS = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
const TICK_MS = 100;
const JOIN_TIMEOUT_MS = 15000;
export const DUMMY_ID = 'dummy';

export function makeRoomCode(rand: () => number = Math.random): string {
  return Array.from({ length: 4 }, () => CODE_CHARS[Math.floor(rand() * CODE_CHARS.length)]).join('');
}

export function normalizeRoomCode(input: string): string {
  return input.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 4);
}

const noop = () => {};

export class HostLink implements GameLink {
  readonly myId = 'host';
  readonly isHost = true;
  onSnapshot: (s: MatchSnapshot) => void = noop;
  onHit: (e: HitEvent) => void = noop;
  onStatus: (message: string, fatal: boolean) => void = noop;

  private timer = 0;
  private last = performance.now();
  private conn: DataConnection | null = null;

  private constructor(
    readonly match: Match,
    private readonly peer: Peer | null,
    readonly roomCode: string | null,
  ) {
    this.timer = window.setInterval(() => this.tick(), TICK_MS);
    peer?.on('connection', (c) => this.accept(c));
    peer?.on('disconnected', () => {
      // 시그널링 서버와만 끊긴 것. 이미 맺은 데이터 채널은 유지되고, 새 입장을 위해 다시 붙는다.
      if (!peer.destroyed) peer.reconnect();
    });
    peer?.on('error', (err) => this.onStatus(peerErrorText(err), false));
  }

  static practice(name: string, character: CharacterId): HostLink {
    const match = new Match({ killTarget: 0, respawnMs: 1500, protectMs: 0 });
    match.addPlayer('host', name, character);
    match.addPlayer(DUMMY_ID, '훈련 표적', 'ranger');
    const link = new HostLink(match, null, null);
    match.start();
    return link;
  }

  /** 방을 연다. 코드가 이미 쓰이고 있으면 새 코드로 몇 번 다시 시도한다. */
  static async open(name: string, character: CharacterId): Promise<HostLink> {
    let lastErr: unknown = null;
    for (let attempt = 0; attempt < 4; attempt++) {
      const code = makeRoomCode();
      try {
        const peer = await openPeer(PEER_PREFIX + code);
        const match = new Match(DEFAULT_MATCH);
        match.addPlayer('host', name, character);
        return new HostLink(match, peer, code);
      } catch (err) {
        lastErr = err;
        if ((err as PeerError<string>)?.type !== 'unavailable-id') break;
      }
    }
    throw new Error(peerErrorText(lastErr));
  }

  get guestConnected(): boolean {
    return this.conn?.open === true && this.match.size === 2;
  }

  start(): void {
    this.match.start();
    this.broadcast();
  }

  shoot(shot: ShotReport): void {
    const target = this.match.opponentOf(this.myId);
    if (!target) return;
    this.resolve(this.myId, target.id, shot);
  }

  close(): void {
    window.clearInterval(this.timer);
    this.conn?.close();
    this.peer?.destroy();
  }

  private resolve(shooterId: string, targetId: string, shot: ShotReport): void {
    const event = this.match.applyShot(shooterId, targetId, shot, performance.now());
    if (event) this.emit(event);
    this.broadcast();
  }

  private tick(): void {
    const now = performance.now();
    const dt = now - this.last;
    this.last = now;
    for (const e of this.match.tick(dt)) this.emit(e);
    this.broadcast();
  }

  private emit(e: HitEvent): void {
    this.onHit(e);
    this.send({ t: 'hit', e });
  }

  private broadcast(): void {
    const s = this.match.snapshot();
    this.onSnapshot(s);
    this.send({ t: 'snap', s });
  }

  private send(msg: ToGuest): void {
    if (this.conn?.open) this.conn.send(msg);
  }

  private accept(c: DataConnection): void {
    if (this.conn?.open) {
      c.on('open', () => {
        c.send({ t: 'full' } satisfies ToGuest);
        setTimeout(() => c.close(), 500);
      });
      return;
    }
    this.conn = c;
    c.on('data', (raw) => {
      const msg = parseToHost(raw);
      if (!msg) return;
      this.handle(c, msg);
    });
    c.on('close', () => {
      if (this.conn !== c) return;
      this.conn = null;
      this.match.removePlayer(c.peer);
      if (this.match.phase !== 'lobby') this.match.phase = 'lobby';
      this.onStatus('상대가 나갔습니다. 다른 사람이 들어오기를 기다립니다.', false);
      this.broadcast();
    });
    c.on('error', (err) => console.warn('참가자 연결 오류', err));
  }

  private handle(c: DataConnection, msg: ToHost): void {
    if (msg.t === 'hello') {
      // 경기 중에 다시 입장 메시지를 보내 HP를 되살리는 것을 막는다
      if (this.match.has(c.peer) && this.match.phase !== 'lobby') return;
      if (this.match.has(c.peer)) this.match.removePlayer(c.peer);
      this.match.addPlayer(c.peer, msg.name, msg.character);
      c.send({ t: 'welcome', id: c.peer } satisfies ToGuest);
      this.broadcast();
      return;
    }
    const target = this.match.opponentOf(c.peer);
    if (target) this.resolve(c.peer, target.id, msg.shot);
  }
}

export class GuestLink implements GameLink {
  readonly isHost = false;
  onSnapshot: (s: MatchSnapshot) => void = noop;
  onHit: (e: HitEvent) => void = noop;
  onStatus: (message: string, fatal: boolean) => void = noop;
  private closing = false;

  private constructor(
    private readonly peer: Peer,
    private readonly conn: DataConnection,
    readonly myId: string,
  ) {
    conn.on('data', (raw) => this.handle(raw as ToGuest));
    conn.on('close', () => {
      if (!this.closing) this.onStatus('방장과 연결이 끊겼습니다.', true);
    });
    peer.on('disconnected', () => {
      if (!peer.destroyed) peer.reconnect();
    });
  }

  static async join(code: string, name: string, character: CharacterId): Promise<GuestLink> {
    const peer = await openPeer();
    try {
      const conn = await new Promise<DataConnection>((resolve, reject) => {
        const c = peer.connect(PEER_PREFIX + code, { reliable: true, serialization: 'json' });
        const timer = setTimeout(() => reject(new Error('방에 연결하지 못했습니다. 코드와 인터넷 연결을 확인해 주세요.')), JOIN_TIMEOUT_MS);
        peer.on('error', (err) => {
          clearTimeout(timer);
          reject(new Error(peerErrorText(err)));
        });
        c.on('open', () => {
          clearTimeout(timer);
          c.send({ t: 'hello', name, character } satisfies ToHost);
          resolve(c);
        });
      });
      const welcome = await new Promise<ToGuest>((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('방장이 응답하지 않습니다.')), JOIN_TIMEOUT_MS);
        conn.on('data', (raw) => {
          const m = raw as ToGuest;
          if (m.t === 'welcome' || m.t === 'full') {
            clearTimeout(timer);
            resolve(m);
          }
        });
      });
      if (welcome.t === 'full') throw new Error('이미 두 명이 들어가 있는 방입니다.');
      return new GuestLink(peer, conn, (welcome as { id: string }).id);
    } catch (err) {
      peer.destroy();
      throw err;
    }
  }

  shoot(shot: ShotReport): void {
    if (this.conn.open) this.conn.send({ t: 'shot', shot } satisfies ToHost);
  }

  close(): void {
    this.closing = true;
    this.conn.close();
    this.peer.destroy();
  }

  private handle(m: ToGuest): void {
    if (m.t === 'snap') this.onSnapshot(m.s);
    else if (m.t === 'hit') this.onHit(m.e);
  }
}

function openPeer(id?: string): Promise<Peer> {
  return new Promise((resolve, reject) => {
    const peer = id ? new Peer(id, { debug: 1 }) : new Peer({ debug: 1 });
    const fail = (err: unknown) => {
      peer.destroy();
      reject(err);
    };
    const timer = setTimeout(() => fail(new Error('연결 서버에 접속하지 못했습니다. 인터넷 연결을 확인해 주세요.')), JOIN_TIMEOUT_MS);
    peer.once('open', () => {
      clearTimeout(timer);
      peer.off('error', fail);
      resolve(peer);
    });
    peer.once('error', (err) => {
      clearTimeout(timer);
      fail(err);
    });
  });
}

function peerErrorText(err: unknown): string {
  const type = (err as PeerError<string>)?.type;
  switch (type) {
    case 'peer-unavailable':
      return '방을 찾을 수 없습니다. 코드를 다시 확인해 주세요.';
    case 'unavailable-id':
      return '방 코드를 만들지 못했습니다. 잠시 뒤 다시 시도해 주세요.';
    case 'network':
    case 'server-error':
    case 'socket-error':
    case 'socket-closed':
      return '연결 서버와 통신하지 못했습니다. 인터넷 연결을 확인해 주세요.';
    case 'browser-incompatible':
      return '이 브라우저는 실시간 연결(WebRTC)을 지원하지 않습니다.';
    default:
      return err instanceof Error ? err.message : '연결 중 오류가 났습니다.';
  }
}
