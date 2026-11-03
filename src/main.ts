import qrcode from 'qrcode-generator';
import './style.css';
import { CHARACTERS, CHARACTER_IDS, WEAPONS, isCharacterId, type CharacterId } from './config/weapons';
import { unlockAudio } from './fx/audio';
import { Play } from './game/play';
import type { MatchSnapshot } from './game/rules';
import { cleanName } from './net/protocol';
import { GuestLink, HostLink, normalizeRoomCode, type GameLink } from './net/link';
import { CameraError, startCamera, stopCamera } from './vision/camera';
import { PoseDetector } from './vision/detector';

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const NAME_KEY = 'lens-strike:name';
const CHAR_KEY = 'lens-strike:character';

function load(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function save(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // 저장소를 못 쓰면 다음 실행 때 다시 입력한다
  }
}

let character: CharacterId = isCharacterId(load(CHAR_KEY)) ? (load(CHAR_KEY) as CharacterId) : 'ranger';
let play: Play | null = null;
let busy = false;
let wakeLock: { release(): Promise<void> } | null = null;
let toastTimer = 0;

// 사람 인식 엔진은 페이지를 열자마자 받기 시작한다(첫 실행 때 시간이 걸린다)
let detector: PoseDetector | null = null;
let detectorError: Error | null = null;
const detectorReady = PoseDetector.create().then(
  (d) => {
    detector = d;
    if (!busy) setStatus(new URLSearchParams(location.search).has('room') ? '준비 완료 · 참가를 누르면 방에 들어갑니다' : '준비 완료');
    return d;
  },
  (err: unknown) => {
    detectorError = err instanceof Error ? err : new Error(String(err));
    setStatus('사람 인식 엔진을 불러오지 못했습니다. 인터넷 연결을 확인하고 새로고침해 주세요.', true);
    throw detectorError;
  },
);
detectorReady.catch(() => {});

function setStatus(text: string, error = false): void {
  const el = $('setup-status');
  el.textContent = text;
  el.classList.toggle('error', error);
}

function toast(text: string): void {
  const el = $('toast');
  el.textContent = text;
  el.classList.add('show');
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => el.classList.remove('show'), 3000);
}

function show(id: 'screen-setup' | 'screen-lobby' | 'screen-result' | null): void {
  for (const s of ['screen-setup', 'screen-lobby', 'screen-result']) $(s).hidden = s !== id;
}

// ---- 설정 화면 ----

function renderCharacters(): void {
  $('characters').replaceChildren(
    ...CHARACTER_IDS.map((id) => {
      const c = CHARACTERS[id];
      const b = document.createElement('button');
      b.className = `character${id === character ? ' selected' : ''}`;
      b.style.setProperty('--c', c.color);
      const name = document.createElement('strong');
      name.textContent = c.name;
      const weapons = document.createElement('em');
      weapons.textContent = c.weapons.map((w) => WEAPONS[w].name).join(' · ');
      const tag = document.createElement('small');
      tag.textContent = `${c.tagline} (HP ${c.maxHp})`;
      b.append(name, weapons, tag);
      b.addEventListener('click', () => {
        character = id;
        save(CHAR_KEY, id);
        renderCharacters();
      });
      return b;
    }),
  );
}

function playerName(): string {
  const name = cleanName($<HTMLInputElement>('name').value);
  save(NAME_KEY, name);
  return name;
}

function detectInApp(): void {
  const ua = navigator.userAgent;
  const kakao = /KAKAOTALK/i.test(ua);
  if (kakao || /(Instagram|FBAN|FBAV|NAVER\(inapp|Line\/|DaumApps|Snapchat)/i.test(ua)) {
    $('inapp').hidden = false;
    if (kakao) {
      const btn = $('btn-external');
      btn.hidden = false;
      btn.addEventListener('click', () => {
        location.href = `kakaotalk://web/openExternal?url=${encodeURIComponent(location.href)}`;
      });
    }
  }
}

// ---- 게임 시작 공통 ----

async function prepare(): Promise<PoseDetector> {
  unlockAudio();
  // 안드로이드는 전체화면으로(아이폰 Safari는 지원하지 않아 무시된다). 사용자 터치 안에서 요청해야 한다.
  void document.documentElement.requestFullscreen?.({ navigationUI: 'hide' }).catch(() => {});
  if (detectorError) throw detectorError;
  if (!detector) setStatus('사람 인식 엔진을 내려받는 중… (첫 실행 때 시간이 걸려요)');
  const d = await detectorReady;
  setStatus('카메라를 켜는 중…');
  await startCamera($<HTMLVideoElement>('cam'));
  await requestWakeLock();
  return d;
}

async function requestWakeLock(): Promise<void> {
  try {
    const nav = navigator as Navigator & { wakeLock?: { request(type: 'screen'): Promise<{ release(): Promise<void> }> } };
    wakeLock = (await nav.wakeLock?.request('screen')) ?? null;
  } catch {
    wakeLock = null; // 배터리 절약 모드 등에서 거부될 수 있다. 화면이 꺼질 수 있을 뿐 게임은 된다.
  }
}

document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible' && play && !wakeLock) void requestWakeLock();
  if (document.visibilityState === 'hidden') wakeLock = null;
});

async function run(action: () => Promise<GameLink>): Promise<void> {
  if (busy) return;
  busy = true;
  setButtons(false);
  try {
    const d = await prepare();
    setStatus('연결하는 중…');
    const link = await action();
    begin(link, d);
    setStatus(detector ? '준비 완료' : '');
  } catch (err) {
    stopCamera($<HTMLVideoElement>('cam'));
    const msg = err instanceof CameraError || err instanceof Error ? err.message : String(err);
    setStatus(msg, true);
  } finally {
    busy = false;
    setButtons(true);
  }
}

function setButtons(enabled: boolean): void {
  for (const id of ['btn-practice', 'btn-host', 'btn-join']) $<HTMLButtonElement>(id).disabled = !enabled;
}

function begin(link: GameLink, d: PoseDetector): void {
  play = new Play(link, character, $<HTMLVideoElement>('cam'), d);
  play.onPhase = (s) => onPhase(s);
  play.onUpdate = () => {
    if (!$('screen-lobby').hidden) renderLobby();
  };
  link.onStatus = (message, fatal) => {
    if (fatal) {
      leave();
      setStatus(message, true);
    } else {
      toast(message);
    }
  };
  const code = link instanceof HostLink ? link.roomCode : null;
  if (code) setupShare(code);
  $('host-share').hidden = !code;
  // 연습 모드는 바로 시작 상태이고, 대전은 대기실부터. 이후 화면은 방장이 보내는 경기 단계(onPhase)를 따른다.
  if (isPractice(link)) show(null);
  else {
    show('screen-lobby');
    renderLobby();
  }
}

function isPractice(link: GameLink): boolean {
  return link instanceof HostLink && !link.roomCode;
}

function leave(): void {
  play?.dispose();
  play = null;
  stopCamera($<HTMLVideoElement>('cam'));
  void wakeLock?.release().catch(() => {});
  wakeLock = null;
  document.body.classList.remove('is-dead');
  if (document.fullscreenElement) void document.exitFullscreen().catch(() => {});
  show('screen-setup');
}

// ---- 대기실 · 결과 ----

function onPhase(s: MatchSnapshot): void {
  if (!play) return;
  if (s.phase === 'playing') {
    show(null);
    if (!isPractice(play.link)) toast('시작! 상대를 화면 가운데에 두고 터치하세요.');
  } else if (s.phase === 'lobby') {
    show('screen-lobby');
    renderLobby();
  } else {
    renderResult(s);
    show('screen-result');
  }
}

let lobbyKey = '';

function renderLobby(): void {
  if (!play) return;
  const link = play.link;
  const players = play.snapshot?.players ?? [];
  const key = JSON.stringify(players.map((p) => [p.id, p.name, p.character]));
  if (key === lobbyKey && $('lobby-players').childElementCount === players.length) return;
  lobbyKey = key;
  $('lobby-players').replaceChildren(
    ...players.map((p) => {
      const li = document.createElement('li');
      const name = document.createElement('span');
      name.textContent = `${p.name}${p.id === link.myId ? ' (나)' : ''}`;
      const role = document.createElement('small');
      role.textContent = `${CHARACTERS[p.character].name}${p.id === 'host' ? ' · 방장' : ''}`;
      li.append(name, role);
      return li;
    }),
  );
  const full = players.length === 2;
  const start = $<HTMLButtonElement>('btn-start');
  start.hidden = !link.isHost;
  start.disabled = !full;
  $('lobby-title').textContent = link.isHost ? '방을 열었습니다' : '방에 들어왔습니다';
  $('lobby-status').textContent = link.isHost
    ? full
      ? '준비되면 시작을 누르세요.'
      : '상대가 코드나 QR로 들어오기를 기다리는 중…'
    : '방장이 시작하기를 기다리는 중…';
}

function renderResult(s: MatchSnapshot): void {
  if (!play) return;
  const me = s.players.find((p) => p.id === play!.link.myId);
  const opp = s.players.find((p) => p.id !== play!.link.myId);
  const won = s.winnerId === play.link.myId;
  $('result-title').textContent = won ? '승리!' : '패배';
  $('result-title').style.color = won ? 'var(--accent)' : 'var(--danger)';
  $('result-score').textContent = `${me?.name ?? '나'} ${me?.kills ?? 0} : ${opp?.kills ?? 0} ${opp?.name ?? '상대'}`;
  $('btn-rematch').hidden = !play.link.isHost;
  $('result-status').textContent = play.link.isHost ? '' : '방장이 다시 시작하기를 기다리는 중…';
}

function joinUrl(code: string): string {
  return `${location.origin}${location.pathname}?room=${code}`;
}

function setupShare(code: string): void {
  $('lobby-code').textContent = code;
  const url = joinUrl(code);
  const qr = qrcode(0, 'M');
  qr.addData(url);
  qr.make();
  $('qr').innerHTML = qr.createSvgTag({ cellSize: 4, margin: 0, scalable: true });
  $('btn-share').hidden = !navigator.share;
}

// ---- 이벤트 연결 ----

function bind(): void {
  $('btn-practice').addEventListener('click', () => void run(async () => HostLink.practice(playerName(), character)));
  $('btn-host').addEventListener('click', () => void run(() => HostLink.open(playerName(), character)));
  const codeInput = $<HTMLInputElement>('room-code');
  codeInput.addEventListener('input', () => (codeInput.value = normalizeRoomCode(codeInput.value)));
  const join = () => {
    const code = normalizeRoomCode(codeInput.value);
    if (code.length !== 4) {
      setStatus('방 코드 4자리를 입력해 주세요.', true);
      codeInput.focus();
      return;
    }
    void run(() => GuestLink.join(code, playerName(), character));
  };
  $('btn-join').addEventListener('click', join);
  codeInput.addEventListener('keydown', (e) => e.key === 'Enter' && join());

  $('btn-exit').addEventListener('click', leave);
  $('btn-leave').addEventListener('click', leave);
  $('btn-result-leave').addEventListener('click', leave);
  $('btn-start').addEventListener('click', () => {
    if (play?.link instanceof HostLink) play.link.start();
  });
  $('btn-rematch').addEventListener('click', () => {
    if (play?.link instanceof HostLink) play.link.start();
  });
  $('btn-copy').addEventListener('click', async () => {
    const code = $('lobby-code').textContent ?? '';
    try {
      await navigator.clipboard.writeText(joinUrl(code));
      toast('참가 링크를 복사했습니다.');
    } catch {
      toast(`복사하지 못했습니다. 코드 ${code}를 알려 주세요.`);
    }
  });
  $('btn-share').addEventListener('click', () => {
    const code = $('lobby-code').textContent ?? '';
    void navigator.share?.({ title: '렌즈 스트라이크', text: `방 코드 ${code}`, url: joinUrl(code) }).catch(() => {});
  });
}

function init(): void {
  $<HTMLInputElement>('name').value = load(NAME_KEY) ?? '';
  renderCharacters();
  detectInApp();
  bind();
  setStatus('사람 인식 엔진을 준비하는 중…');
  const room = new URLSearchParams(location.search).get('room');
  if (room) {
    $<HTMLInputElement>('room-code').value = normalizeRoomCode(room);
    setStatus('참가 링크로 들어왔습니다. 닉네임과 캐릭터를 고르고 참가를 누르세요.');
  }
  if (!window.isSecureContext) setStatus('HTTPS 주소로 열어야 카메라를 쓸 수 있습니다.', true);
}

init();
