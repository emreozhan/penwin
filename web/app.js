// @ts-check
/**
 * PenWin iPad istemcisi: Apple Pencil olaylarını WebSocket ile PC'deki penwin.exe'ye iletir.
 * Satır protokolünün tamamı src/Controller.cs başındaki açıklamadadır.
 */

import { applyLang, resolveLang, t } from './i18n.js';

/** @typedef {{ name: string, w: number, h: number, primary: boolean }} MonitorInfo */
/**
 * @typedef {object} Settings
 * @property {'mouse' | 'pen'} mode
 * @property {number} mon        Sunucudaki monitör sırası
 * @property {number} deadzone   Tıklama ölü bölgesi (CSS piksel)
 * @property {number} threshold  Temas eşiği (basınç 0..1)
 * @property {boolean} gestures  İki parmak pan / zoom
 * @property {boolean} trail     iPad'de kalem izi
 * @property {'left' | 'right'} side
 * @property {string} token
 * @property {boolean} mirror        PC ekranı arka planda
 * @property {number} mirrorOpacity  0..1
 * @property {'' | 'tr' | 'en'} lang  '' = iPad diline göre
 */
/**
 * @typedef {object} Stroke
 * @property {number} id
 * @property {boolean} down   Sunucuya 'd' gönderildi mi
 * @property {boolean} free   Ölü bölge aşıldı mı
 * @property {number} sx
 * @property {number} sy
 * @property {number} lx
 * @property {number} ly
 */

const STORAGE_KEY = 'penwin.settings.v1';
const PING_INTERVAL_MS = 2000;
const TRAIL_FADE_MS = 900;
const STATUS_BAND_PX = 44; // durum çubuğu için üstte bırakılan pay
const AREA_MARGIN_PX = 8;
const PEN_GUARD_MS = 400; // kalemden hemen sonra gelen parmak/avuç temaslarını yok say
const GESTURE_LOCK_PX = 14;
const PINCH_STEP = 1.12; // her %12 ölçek değişimi = bir tekerlek çentiği
const SHOT_INTERVAL_MS = 1000; // PC ekranı arka planı: saniyede bir kare
const SHOT_MAX_WIDTH = 1600;

/** @type {Settings} */
const DEFAULTS = {
  mode: 'mouse',
  mon: 0,
  deadzone: 4,
  threshold: 0,
  gestures: true,
  trail: true,
  side: 'left',
  token: '',
  mirror: false,
  mirrorOpacity: 0.35,
  lang: '',
};

/** @returns {Settings} */
function loadSettings() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? { ...DEFAULTS, ...JSON.parse(raw) } : { ...DEFAULTS };
  } catch (error) {
    console.warn('Ayarlar okunamadı, varsayılanlar kullanılıyor', error);
    return { ...DEFAULTS };
  }
}

function saveSettings() {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  } catch (error) {
    console.warn('Ayarlar kaydedilemedi', error);
  }
}

const settings = loadSettings();
// Yer imi / ana ekran kısayolu #k=ANAHTAR taşır; adres çubuğunda bırakıyoruz ki "Ana Ekrana Ekle" de alsın.
const hashToken = new URLSearchParams(location.hash.slice(1)).get('k');
if (hashToken) {
  settings.token = hashToken.trim().toUpperCase();
  saveSettings();
}

// ------------------------------------------------------------------ DOM

/**
 * @template {HTMLElement} T
 * @param {string} selector
 * @returns {T}
 */
function $(selector) {
  const el = document.querySelector(selector);
  if (!el) throw new Error(`Eleman bulunamadı: ${selector}`);
  return /** @type {T} */ (el);
}

const app = $('#app');
const toolbar = $('#toolbar');
const pad = $('#pad');
const areaEl = $('#area');
const shotEl = /** @type {HTMLImageElement} */ ($('#shot'));
const mirrorSwitch = $('#mirrorSwitch');
const canvas = /** @type {HTMLCanvasElement} */ ($('#ink'));
const ctx = /** @type {CanvasRenderingContext2D} */ (canvas.getContext('2d'));
const dot = $('#status .dot');
const statusText = $('#statusText');
const latencyEl = $('#latency');
const modeBadge = $('#modeBadge');
const hoverBadge = $('#hoverBadge');
const notice = $('#notice');
const dialog = /** @type {HTMLDialogElement} */ ($('#settings'));
const form = /** @type {HTMLFormElement} */ (dialog.querySelector('form'));

// ------------------------------------------------------------------ durum

const state = {
  /** @type {WebSocket | null} */
  ws: null,
  connected: false,
  stopped: false,
  retry: 0,
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  retryTimer: undefined,
  /** @type {MonitorInfo[]} */
  monitors: [],
  penAvailable: false,
  /** Aktif kalem tuşu: L sol, R sağ, M orta (pan), O shift+orta (orbit) */
  button: 'L',
  hoverSeen: false,
  lastPenAt: 0,
  padRect: { left: 0, top: 0, width: 1, height: 1 },
  area: { x: 0, y: 0, w: 1, h: 1 },
  /** @type {Stroke | null} */
  stroke: null,
  /** @type {Set<string>} */
  latched: new Set(),
};

// ------------------------------------------------------------------ bağlantı

function connect() {
  clearTimeout(state.retryTimer);
  state.stopped = false;
  if (!settings.token) {
    setStatus('off', 'status.needKey');
    showNotice('notice.needKey', 'action.openSettings', openSettings);
    return;
  }
  if (state.ws) {
    const old = state.ws;
    state.ws = null;
    old.close();
  }

  const ws = new WebSocket(`ws://${location.host}/ws?k=${encodeURIComponent(settings.token)}`);
  state.ws = ws;
  setStatus('wait', 'status.connecting');

  ws.addEventListener('open', () => {
    state.retry = 0;
    sendConfig();
  });
  ws.addEventListener('message', (event) => {
    try {
      onServerMessage(JSON.parse(String(event.data)));
    } catch (error) {
      console.error('Sunucu mesajı işlenemedi', error);
    }
  });
  ws.addEventListener('close', () => {
    if (state.ws !== ws) return;
    state.ws = null;
    state.connected = false;
    state.stroke = null;
    resetLatches();
    latencyEl.textContent = '';
    setStatus('off', 'status.disconnected');
    if (!state.stopped) scheduleReconnect();
  });
}

function scheduleReconnect() {
  const delay = Math.min(5000, 500 * 2 ** state.retry);
  state.retry += 1;
  state.retryTimer = setTimeout(connect, delay);
}

/** @param {any} msg */
function onServerMessage(msg) {
  switch (msg.t) {
    case 'hello':
      state.connected = true;
      state.penAvailable = Boolean(msg.pen);
      state.monitors = Array.isArray(msg.monitors) ? msg.monitors : [];
      if (settings.mon >= state.monitors.length) settings.mon = 0;
      if (settings.mode === 'pen' && !state.penAvailable) settings.mode = 'mouse';
      setStatus('on', 'status.connected');
      hideNotice();
      updateBadges();
      layout();
      if (settings.mirror) refreshShot();
      break;
    case 'pong':
      latencyEl.textContent = `${Math.round(performance.now() - Number(msg.v))} ms`;
      break;
    case 'error':
      state.stopped = true;
      if (msg.code === 'token') {
        setStatus('off', 'status.badKey');
        showNotice('notice.badKey', 'action.openSettings', openSettings);
      } else if (msg.code === 'replaced') {
        setStatus('off', 'status.replaced');
        showNotice('notice.replaced', 'action.reconnect', connect);
      }
      break;
    default:
      break;
  }
}

/** @param {string} line */
function send(line) {
  const ws = state.ws;
  if (ws && ws.readyState === WebSocket.OPEN) ws.send(line);
}

function sendConfig() {
  send(`cfg ${settings.mode} ${settings.mon}`);
}

setInterval(() => {
  if (state.connected) send(`ping ${performance.now().toFixed(1)}`);
}, PING_INTERVAL_MS);

document.addEventListener('visibilitychange', () => {
  if (document.visibilityState !== 'visible') return;
  if (!state.ws && !state.stopped) connect();
  else if (settings.mirror) refreshShot();
});

// ------------------------------------------------------------------ yerleşim

function layout() {
  const rect = pad.getBoundingClientRect();
  state.padRect = { left: rect.left, top: rect.top, width: rect.width, height: rect.height };

  const availW = rect.width - AREA_MARGIN_PX * 2;
  const availH = rect.height - STATUS_BAND_PX - AREA_MARGIN_PX;
  const mon = state.monitors[settings.mon];
  const aspect = mon ? mon.w / mon.h : availW / availH;
  let w = availW;
  let h = w / aspect;
  if (h > availH) {
    h = availH;
    w = h * aspect;
  }
  const x = (rect.width - w) / 2;
  const y = STATUS_BAND_PX + (availH - h) / 2;
  state.area = { x, y, w, h };
  Object.assign(areaEl.style, { left: `${x}px`, top: `${y}px`, width: `${w}px`, height: `${h}px` });

  const dpr = window.devicePixelRatio || 1;
  canvas.width = Math.round(rect.width * dpr);
  canvas.height = Math.round(rect.height * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  requestRender();
}

new ResizeObserver(layout).observe(pad);

// ------------------------------------------------------------------ kalem

/**
 * Olayı aktif alana göre 0..1 koordinatına ve kalem verisine çevirir.
 * @param {PointerEvent} e
 */
function sample(e) {
  const px = e.clientX - state.padRect.left;
  const py = e.clientY - state.padRect.top;
  const { x, y, w, h } = state.area;
  const nx = Math.min(1, Math.max(0, (px - x) / w));
  const ny = Math.min(1, Math.max(0, (py - y) / h));
  const pressure = Number.isFinite(e.pressure) ? e.pressure : 0;
  const [tiltX, tiltY] = tilt(e);
  return { px, py, nx, ny, pressure, tiltX, tiltY };
}

/**
 * Safari tiltX/tiltY vermezse altitude/azimuth açılarından hesaplar.
 * @param {PointerEvent} e
 * @returns {[number, number]}
 */
function tilt(e) {
  if (e.tiltX || e.tiltY) return [e.tiltX, e.tiltY];
  const alt = /** @type {any} */ (e).altitudeAngle;
  const az = /** @type {any} */ (e).azimuthAngle;
  if (typeof alt !== 'number' || typeof az !== 'number' || alt >= Math.PI / 2) return [0, 0];
  const deg = 180 / Math.PI;
  return [
    Math.round(Math.atan(Math.cos(az) / Math.tan(alt)) * deg),
    Math.round(Math.atan(Math.sin(az) / Math.tan(alt)) * deg),
  ];
}

/**
 * @param {string} cmd
 * @param {ReturnType<typeof sample>} s
 */
function line(cmd, s) {
  return `${cmd} ${s.nx.toFixed(5)} ${s.ny.toFixed(5)} ${s.pressure.toFixed(3)} ${s.tiltX} ${s.tiltY}`;
}

/** @param {PointerEvent} e */
function isStylus(e) {
  return e.pointerType === 'pen';
}

/**
 * Fare/trackpad olayları yok sayılır: sayfa PC'nin kendi tarayıcısında açılırsa
 * imleci hareket ettirip kendi kendini besleyen bir döngüye girmesin.
 * @param {PointerEvent} e
 */
function isIgnored(e) {
  return e.pointerType === 'mouse';
}

pad.addEventListener('pointerdown', (e) => {
  if (isIgnored(e) || notice.contains(/** @type {Node} */ (e.target))) return; // bildirim düğmesi kendi olayını alsın
  if (!isStylus(e)) {
    onTouchDown(e);
    return;
  }
  e.preventDefault();
  cancelGesture();
  touches.clear();
  if (state.stroke) return;
  try {
    pad.setPointerCapture(e.pointerId);
  } catch (error) {
    console.warn('İşaretçi yakalanamadı', error);
  }
  state.lastPenAt = performance.now();

  const s = sample(e);
  state.stroke = { id: e.pointerId, down: false, free: settings.deadzone === 0, sx: s.px, sy: s.py, lx: s.px, ly: s.py };
  if (s.pressure >= settings.threshold) {
    beginContact(s);
  } else {
    send(line('h', s));
  }
  setCursor(s.px, s.py, state.stroke.down);
});

pad.addEventListener('pointermove', (e) => {
  if (isIgnored(e)) return;
  if (!isStylus(e)) {
    onTouchMove(e);
    return;
  }
  state.lastPenAt = performance.now();
  const stroke = state.stroke;

  if (!stroke) {
    if (e.buttons === 0) markHover();
    const s = sample(e);
    send(line('h', s));
    setCursor(s.px, s.py, false);
    return;
  }
  if (stroke.id !== e.pointerId) return;

  // Apple Pencil 240 Hz örnekler; ekran karesi başına birleştirilenleri de gönder.
  const events = typeof e.getCoalescedEvents === 'function' ? e.getCoalescedEvents() : [];
  const lines = [];
  for (const ev of events.length ? events : [e]) {
    const s = sample(ev);
    if (!stroke.down) {
      if (s.pressure >= settings.threshold) {
        lines.push(beginContactLine(s));
      } else {
        lines.push(line('h', s));
      }
      continue;
    }
    if (!stroke.free) {
      if (Math.hypot(s.px - stroke.sx, s.py - stroke.sy) < settings.deadzone) continue;
      stroke.free = true;
    }
    lines.push(line('m', s));
    addTrail(s.px, s.py, s.pressure, false);
    stroke.lx = s.px;
    stroke.ly = s.py;
  }
  if (lines.length) send(lines.join('\n'));
  const last = sample(e);
  setCursor(last.px, last.py, stroke.down);
});

/** @param {PointerEvent} e */
function endStroke(e) {
  const stroke = state.stroke;
  if (!stroke || stroke.id !== e.pointerId) return false;
  if (stroke.down) {
    const s = sample(e);
    // Ölü bölge aşılmadıysa tıklama tam başladığı noktada biter.
    const nx = stroke.free ? s.nx : (stroke.sx - state.area.x) / state.area.w;
    const ny = stroke.free ? s.ny : (stroke.sy - state.area.y) / state.area.h;
    send(`u ${clamp01(nx).toFixed(5)} ${clamp01(ny).toFixed(5)}`);
  }
  state.stroke = null;
  state.lastPenAt = performance.now();
  if (state.button === 'R') selectButton('L'); // sağ tık tek seferlik
  return true;
}

pad.addEventListener('pointerup', (e) => {
  if (isIgnored(e)) return;
  if (!isStylus(e)) {
    onTouchUp(e);
    return;
  }
  if (endStroke(e)) setCursor(cursor.x, cursor.y, false);
});

pad.addEventListener('pointercancel', (e) => {
  if (isIgnored(e)) return;
  if (!isStylus(e)) {
    onTouchUp(e);
    return;
  }
  endStroke(e);
});

pad.addEventListener('pointerleave', (e) => {
  if (!isStylus(e) || state.stroke) return;
  send('o');
  hideCursor();
});

/** @param {ReturnType<typeof sample>} s */
function beginContact(s) {
  send(beginContactLine(s));
}

/** @param {ReturnType<typeof sample>} s */
function beginContactLine(s) {
  const stroke = /** @type {Stroke} */ (state.stroke);
  stroke.down = true;
  stroke.sx = s.px;
  stroke.sy = s.py;
  addTrail(s.px, s.py, s.pressure, true);
  return `${line('d', s)} ${state.button}`;
}

function markHover() {
  if (state.hoverSeen) return;
  state.hoverSeen = true;
  updateBadges();
}

/** @param {number} v */
function clamp01(v) {
  return Math.min(1, Math.max(0, v));
}

// ------------------------------------------------------------------ iki parmak jestleri

/** @type {Map<number, { x: number, y: number }>} */
const touches = new Map();
/** @type {null | { kind: 'pending' | 'pan' | 'zoom', cx: number, cy: number, dist: number, steps: number, moved: Set<number> }} */
let gesture = null;

function touchGeometry() {
  const [a, b] = [...touches.values()];
  return { cx: (a.x + b.x) / 2, cy: (a.y + b.y) / 2, dist: Math.hypot(a.x - b.x, a.y - b.y) || 1 };
}

/** @param {PointerEvent} e */
function onTouchDown(e) {
  if (!settings.gestures || !state.connected) return;
  if (state.stroke || performance.now() - state.lastPenAt < PEN_GUARD_MS) return; // avuç reddi
  touches.set(e.pointerId, { x: e.clientX, y: e.clientY });
  if (touches.size === 2) {
    gesture = { kind: 'pending', ...touchGeometry(), steps: 0, moved: new Set() };
  } else if (touches.size > 2) {
    cancelGesture();
  }
}

/** @param {PointerEvent} e */
function onTouchMove(e) {
  const t = touches.get(e.pointerId);
  if (!t) return;
  t.x = e.clientX;
  t.y = e.clientY;
  if (!gesture || touches.size !== 2) return;

  const g = touchGeometry();
  if (gesture.kind === 'pending') {
    // Parmak güncellemeleri tek tek gelir; tek parmağın yarım hareketi pan'ı zoom gibi gösterir.
    // Bu yüzden iki parmak da hareket ettikten sonra karar verilir.
    gesture.moved.add(e.pointerId);
    if (gesture.moved.size < 2) return;
    const spread = Math.abs(g.dist - gesture.dist);
    const travel = Math.hypot(g.cx - gesture.cx, g.cy - gesture.cy);
    if (spread > GESTURE_LOCK_PX && spread > travel) {
      gesture.kind = 'zoom';
    } else if (travel > GESTURE_LOCK_PX) {
      gesture.kind = 'pan';
      send('pan s');
    }
  }
  if (gesture.kind === 'zoom') {
    const steps = Math.trunc(Math.log(g.dist / gesture.dist) / Math.log(PINCH_STEP));
    if (steps !== gesture.steps) {
      send(`wheel ${(steps - gesture.steps) * 120}`);
      gesture.steps = steps;
    }
  } else if (gesture.kind === 'pan') {
    const dx = (g.cx - gesture.cx) / state.area.w;
    const dy = (g.cy - gesture.cy) / state.area.h;
    send(`pan m ${dx.toFixed(5)} ${dy.toFixed(5)}`);
  }
}

/** @param {PointerEvent} e */
function onTouchUp(e) {
  touches.delete(e.pointerId);
  if (touches.size < 2) cancelGesture();
}

function cancelGesture() {
  if (gesture && gesture.kind === 'pan') send('pan e');
  gesture = null;
}

// ------------------------------------------------------------------ çizim geri bildirimi

/** @type {{ x: number, y: number, t: number, w: number, brk: boolean }[]} */
const trail = [];
const cursor = { x: 0, y: 0, visible: false, contact: false };
let renderPending = false;

/**
 * @param {number} x
 * @param {number} y
 * @param {number} pressure
 * @param {boolean} brk Yeni çizginin başlangıcı
 */
function addTrail(x, y, pressure, brk) {
  if (!settings.trail) return;
  trail.push({ x, y, t: performance.now(), w: 1.2 + pressure * 3, brk });
  requestRender();
}

/**
 * @param {number} x
 * @param {number} y
 * @param {boolean} contact
 */
function setCursor(x, y, contact) {
  Object.assign(cursor, { x, y, visible: true, contact });
  requestRender();
}

function hideCursor() {
  cursor.visible = false;
  requestRender();
}

function requestRender() {
  if (renderPending) return;
  renderPending = true;
  requestAnimationFrame(render);
}

/** @param {number} now */
function render(now) {
  renderPending = false;
  const { width, height } = state.padRect;
  ctx.clearRect(0, 0, width, height);

  while (trail.length && now - trail[0].t > TRAIL_FADE_MS) trail.shift();
  ctx.lineCap = 'round';
  ctx.strokeStyle = '#7fdcff';
  for (let i = 1; i < trail.length; i++) {
    const a = trail[i - 1];
    const b = trail[i];
    if (b.brk) continue;
    ctx.globalAlpha = Math.max(0, 1 - (now - b.t) / TRAIL_FADE_MS);
    ctx.lineWidth = b.w;
    ctx.beginPath();
    ctx.moveTo(a.x, a.y);
    ctx.lineTo(b.x, b.y);
    ctx.stroke();
  }
  ctx.globalAlpha = 1;

  if (cursor.visible) {
    const r = cursor.contact ? 5 : 9;
    ctx.strokeStyle = '#ff8a3d';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.arc(cursor.x, cursor.y, r, 0, Math.PI * 2);
    ctx.moveTo(cursor.x - r - 6, cursor.y);
    ctx.lineTo(cursor.x - r + 2, cursor.y);
    ctx.moveTo(cursor.x + r - 2, cursor.y);
    ctx.lineTo(cursor.x + r + 6, cursor.y);
    ctx.stroke();
  }
  if (trail.length) requestRender();
}

// ------------------------------------------------------------------ araç çubuğu

/** @param {string} button */
function selectButton(button) {
  state.button = button;
  for (const el of toolbar.querySelectorAll('[data-btn]')) {
    el.setAttribute('aria-pressed', String(/** @type {HTMLElement} */ (el).dataset.btn === button));
  }
}

function resetLatches() {
  state.latched.clear();
  for (const el of toolbar.querySelectorAll('[data-mod]')) el.setAttribute('aria-pressed', 'false');
}

toolbar.addEventListener('pointerdown', (e) => {
  const btn = /** @type {HTMLElement | null} */ (/** @type {HTMLElement} */ (e.target).closest('button'));
  if (!btn) return;
  e.preventDefault();
  btn.classList.add('pressed');
  const { btn: button, mod, key, wheel } = btn.dataset;

  if (btn === mirrorSwitch) {
    setMirror(!settings.mirror);
  } else if (button) {
    // Aktif pan/orbit'e tekrar dokunmak sol tıka döndürür.
    selectButton(state.button === button && button !== 'L' ? 'L' : button);
  } else if (mod) {
    const on = !state.latched.has(mod);
    if (on) state.latched.add(mod);
    else state.latched.delete(mod);
    btn.setAttribute('aria-pressed', String(on));
    send(`mod ${mod} ${on ? 1 : 0}`);
  } else if (key) {
    send(`key ${key}`);
  } else if (wheel) {
    send(`wheel ${wheel}`);
  }
});

/** @param {PointerEvent} e */
function releaseToolButton(e) {
  const btn = /** @type {HTMLElement} */ (e.target).closest('button');
  if (!btn) return;
  btn.classList.remove('pressed');
  if (e.type === 'pointerup' && btn.id === 'settingsBtn') openSettings();
}
toolbar.addEventListener('pointerup', releaseToolButton);
toolbar.addEventListener('pointercancel', releaseToolButton);
toolbar.addEventListener('pointerout', (e) => {
  const btn = /** @type {HTMLElement} */ (e.target).closest('button');
  if (btn) btn.classList.remove('pressed');
});

// iOS Safari: uzun basma büyüteci, seçim, çift dokunma yakınlaştırması ve sıkıştırma yakınlaştırması kapalı.
for (const type of ['touchstart', 'touchmove', 'touchend']) {
  app.addEventListener(type, (e) => e.preventDefault(), { passive: false });
}
for (const type of ['gesturestart', 'gesturechange', 'dblclick', 'contextmenu']) {
  document.addEventListener(type, (e) => e.preventDefault());
}

// ------------------------------------------------------------------ durum göstergeleri

/** Dil değişince yeniden çizebilmek için son durum ve bildirim anahtarları saklanır. */
const shown = {
  /** @type {'on' | 'off' | 'wait'} */
  statusKind: 'wait',
  statusKey: 'status.connecting',
  /** @type {null | { textKey: string, actionKey: string, action: () => void }} */
  notice: null,
};

/**
 * @param {'on' | 'off' | 'wait'} kind
 * @param {string} key Sözlük anahtarı
 */
function setStatus(kind, key) {
  shown.statusKind = kind;
  shown.statusKey = key;
  dot.dataset.state = kind;
  statusText.textContent = t(key);
}

function updateBadges() {
  modeBadge.textContent = t(settings.mode === 'pen' ? 'mode.pen' : 'mode.mouse');
  modeBadge.classList.toggle('on', settings.mode === 'pen');
  hoverBadge.textContent = state.hoverSeen ? 'Hover ✓' : 'Hover ?';
  hoverBadge.classList.toggle('on', state.hoverSeen);
  hoverBadge.classList.toggle('dim', !state.hoverSeen);
}

/**
 * @param {string} textKey
 * @param {string} actionKey
 * @param {() => void} action
 */
function showNotice(textKey, actionKey, action) {
  shown.notice = { textKey, actionKey, action };
  notice.replaceChildren();
  const p = document.createElement('div');
  p.textContent = t(textKey);
  const button = document.createElement('button');
  button.textContent = t(actionKey);
  button.addEventListener('pointerup', (e) => {
    e.preventDefault();
    action();
  });
  notice.append(p, button);
  notice.hidden = false;
}

function hideNotice() {
  shown.notice = null;
  notice.hidden = true;
}

/** Dili uygular ve JS'in ürettiği metinleri de yeniler. */
function refreshLanguage() {
  applyLang(resolveLang(settings.lang || undefined));
  setStatus(shown.statusKind, shown.statusKey);
  if (shown.notice) showNotice(shown.notice.textKey, shown.notice.actionKey, shown.notice.action);
  updateBadges();
  setMirrorError(mirror.failed);
  if (dialog.open) fillSettings();
}

// ------------------------------------------------------------------ PC ekranı arka planı

const mirror = {
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  timer: undefined,
  busy: false,
  failed: false,
  url: '',
};

/** @param {boolean} on */
function setMirror(on) {
  settings.mirror = on;
  saveSettings();
  mirrorSwitch.setAttribute('aria-checked', String(on));
  areaEl.classList.toggle('mirroring', on);
  if (on) {
    refreshShot();
  } else {
    clearTimeout(mirror.timer);
    shotEl.hidden = true;
    shotEl.removeAttribute('src');
    if (mirror.url) URL.revokeObjectURL(mirror.url);
    mirror.url = '';
    setMirrorError(false);
  }
}

function applyMirrorOpacity() {
  areaEl.style.setProperty('--mirror-opacity', String(settings.mirrorOpacity));
}

/** @param {boolean} failed */
function setMirrorError(failed) {
  mirror.failed = failed;
  mirrorSwitch.dataset.error = String(failed);
  $('#mirrorHint').textContent = t(failed ? 'mirror.failed' : 'mirror.sub');
}

/**
 * Resmi yükler; takılırsa döngü kilitlenmesin diye zaman aşımı vardır.
 * @param {string} url
 * @returns {Promise<void>}
 */
function loadImage(url) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    const timer = setTimeout(() => reject(new Error('Resim yükleme zaman aşımı')), 5000);
    img.onload = () => {
      clearTimeout(timer);
      resolve();
    };
    img.onerror = () => {
      clearTimeout(timer);
      reject(new Error('Resim çözülemedi'));
    };
    img.src = url;
  });
}

/** Seçili monitörün görüntüsünü çeker; bitince bir sonrakini saniyeye tamamlayacak şekilde planlar. */
async function refreshShot() {
  clearTimeout(mirror.timer);
  if (!settings.mirror || mirror.busy || !state.connected || document.visibilityState === 'hidden') return;
  mirror.busy = true;
  const started = performance.now();
  try {
    const width = Math.min(SHOT_MAX_WIDTH, Math.round(state.area.w * (window.devicePixelRatio || 1)));
    const res = await fetch(`/shot.jpg?k=${encodeURIComponent(settings.token)}&w=${width}`, { cache: 'no-store' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const url = URL.createObjectURL(await res.blob());
    try {
      // Önce ayrı bir resimde yükle: eski kare yeni kare hazır olana kadar görünür kalır, titreme olmaz.
      await loadImage(url);
    } catch (error) {
      URL.revokeObjectURL(url);
      throw error;
    }
    if (settings.mirror) {
      shotEl.src = url;
      shotEl.hidden = false;
      if (mirror.url) URL.revokeObjectURL(mirror.url);
      mirror.url = url;
    } else {
      URL.revokeObjectURL(url);
    }
    setMirrorError(false);
  } catch (error) {
    console.warn('PC ekran görüntüsü alınamadı', error);
    setMirrorError(true);
  } finally {
    mirror.busy = false;
  }
  if (settings.mirror && state.connected) {
    mirror.timer = setTimeout(refreshShot, Math.max(0, SHOT_INTERVAL_MS - (performance.now() - started)));
  }
}

// ------------------------------------------------------------------ ayarlar

/** @param {string} name */
function field(name) {
  return /** @type {any} */ (form.elements.namedItem(name));
}

function openSettings() {
  fillSettings();
  if (!dialog.open) dialog.showModal();
}

function fillSettings() {
  field('lang').value = resolveLang(settings.lang || undefined);
  const monSelect = /** @type {HTMLSelectElement} */ (field('mon'));
  monSelect.replaceChildren(
    ...state.monitors.map(
      (m, i) => new Option(t('set.screenItem', { n: i + 1, w: m.w, h: m.h }) + (m.primary ? t('set.primary') : ''), String(i)),
    ),
  );
  if (!state.monitors.length) monSelect.append(new Option(t('set.waiting'), '0'));
  monSelect.value = String(settings.mon);

  for (const radio of form.querySelectorAll('input[name="mode"]')) {
    const input = /** @type {HTMLInputElement} */ (radio);
    input.checked = input.value === settings.mode;
    input.disabled = input.value === 'pen' && state.connected && !state.penAvailable;
  }
  field('deadzone').value = String(settings.deadzone);
  field('mirrorOpacity').value = String(settings.mirrorOpacity);
  field('threshold').value = String(settings.threshold);
  field('gestures').checked = settings.gestures;
  field('trail').checked = settings.trail;
  field('sideRight').checked = settings.side === 'right';
  field('token').value = settings.token;
  updateOutputs();
}

function updateOutputs() {
  field('deadzoneOut').value = `${settings.deadzone} px`;
  field('mirrorOpacityOut').value = new Intl.NumberFormat(document.documentElement.lang, { style: 'percent' }).format(
    settings.mirrorOpacity,
  );
  field('thresholdOut').value = settings.threshold > 0 ? settings.threshold.toFixed(2) : t('set.threshold.off');
}

let tokenChanged = false;

/** @param {Event} e */
function onSettingChange(e) {
  const target = /** @type {HTMLInputElement} */ (e.target);
  switch (target.name) {
    case 'lang':
      if (settings.lang === target.value) return;
      settings.lang = target.value === 'en' ? 'en' : 'tr';
      saveSettings();
      refreshLanguage();
      return;
    case 'mode':
      settings.mode = target.value === 'pen' ? 'pen' : 'mouse';
      sendConfig();
      break;
    case 'mon':
      settings.mon = Number(target.value) || 0;
      sendConfig();
      layout();
      break;
    case 'mirrorOpacity':
      settings.mirrorOpacity = Number(target.value);
      applyMirrorOpacity();
      break;
    case 'deadzone':
      settings.deadzone = Number(target.value);
      break;
    case 'threshold':
      settings.threshold = Number(target.value);
      break;
    case 'gestures':
      settings.gestures = target.checked;
      break;
    case 'trail':
      settings.trail = target.checked;
      break;
    case 'sideRight':
      settings.side = target.checked ? 'right' : 'left';
      app.dataset.side = settings.side;
      break;
    case 'token':
      settings.token = target.value.trim().toUpperCase();
      tokenChanged = true;
      break;
    default:
      return;
  }
  saveSettings();
  updateOutputs();
  updateBadges();
}
// Safari bazı denetimlerde yalnızca 'change' gönderir; ikisi de dinlenir, işlem tekrarına dayanıklı.
form.addEventListener('input', onSettingChange);
form.addEventListener('change', onSettingChange);

dialog.addEventListener('close', () => {
  // Anahtar değiştiyse veya bağlantı yoksa yeniden dene.
  if (tokenChanged || !state.connected || state.stopped) connect();
  tokenChanged = false;
});

$('#reconnectBtn').addEventListener('click', () => {
  dialog.close();
  connect();
});

$('#fullscreenBtn').addEventListener('click', async () => {
  const root = /** @type {any} */ (document.documentElement);
  try {
    if (root.requestFullscreen) await root.requestFullscreen();
    else if (root.webkitRequestFullscreen) root.webkitRequestFullscreen();
  } catch (error) {
    console.warn('Tam ekran açılamadı', error);
  }
});

// ------------------------------------------------------------------ başlat

app.dataset.side = settings.side;
refreshLanguage();
applyMirrorOpacity();
mirrorSwitch.setAttribute('aria-checked', String(settings.mirror));
areaEl.classList.toggle('mirroring', settings.mirror);
updateBadges();
layout();
connect();
