/* Centro de Mando — interfaz (JS moderno, sin frameworks ni build) */
'use strict';

const hasWebView = !!(window.chrome && window.chrome.webview);

/* ══════════════════════ Puente con el backend ══════════════════════ */
const api = (() => {
  let seq = 0;
  let post = null;
  const pending = new Map();
  const handlers = new Map();

  function deliver(d) {
    if (!d || typeof d !== 'object') return;
    if (d.id != null && pending.has(d.id)) {
      const p = pending.get(d.id);
      pending.delete(d.id);
      clearTimeout(p.timer);
      if (d.ok) p.resolve(d.result);
      else p.reject(new Error(d.error || 'Error desconocido'));
    } else if (d.event) {
      (handlers.get(d.event) || []).forEach(fn => { try { fn(d.data); } catch (e) { console.error(e); } });
    }
  }

  function attach() {
    if (hasWebView) {
      window.chrome.webview.addEventListener('message', e => deliver(e.data));
      post = m => window.chrome.webview.postMessage(m);
    } else {
      window.MockBridge.connect(deliver);
      post = m => window.MockBridge.post(m);
    }
  }

  function call(cmd, args) {
    return new Promise((resolve, reject) => {
      const id = ++seq;
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(new Error('El programa no ha respondido a tiempo (' + cmd + ')'));
      }, 15000);
      pending.set(id, { resolve, reject, timer });
      try { post({ id, cmd, args: args ?? {} }); }
      catch (e) { clearTimeout(timer); pending.delete(id); reject(e); }
    });
  }

  function on(event, fn) {
    if (!handlers.has(event)) handlers.set(event, []);
    handlers.get(event).push(fn);
  }

  return { attach, call, on };
})();

/* ══════════════════════ Utilidades ══════════════════════ */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const h = html => { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; };
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const isNum = v => typeof v === 'number' && isFinite(v);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const NF = [0, 1, 2].map(d => new Intl.NumberFormat(I18N.locale, { minimumFractionDigits: d, maximumFractionDigits: d, useGrouping: false }));
const fmt = (v, d = 0, unit = '') => isNum(v) ? NF[d].format(v) + unit : '—';
const fmtMm = v => isNum(v) ? new Intl.NumberFormat(I18N.locale, { minimumFractionDigits: 1, maximumFractionDigits: 2 }).format(v) : '—';
const fmtClock = mhz => !isNum(mhz) ? '—' : mhz >= 1000 ? NF[2].format(mhz / 1000) + ' GHz' : NF[0].format(mhz) + ' MHz';
const fmtRate = k => !isNum(k) ? '—' : k >= 1000 ? NF[1].format(k / 1000) + ' Mb/s' : NF[0].format(k) + ' kb/s';
const fmtGb = mb => isNum(mb) ? NF[1].format(mb / 1024) : '—';
const signed = v => !isNum(v) ? '—' : (v > 0 ? '+' : '') + NF[0].format(v);
const setText = (sel, v, root = document) => { const e = typeof sel === 'string' ? $(sel, root) : sel; if (e && e.textContent !== v) e.textContent = v; };
const debounce = (fn, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };

const COL = { accent: '#00d6aa', warn: '#ffb020', hot: '#ff5a4f', blue: '#4da3ff', violet: '#9b7bff', dim: '#5c677e' };
const tempHex = t => !isNum(t) ? COL.dim : t < 60 ? COL.accent : t <= 80 ? COL.warn : COL.hot;
const loadHex = l => !isNum(l) ? COL.dim : l < 75 ? COL.blue : l < 92 ? COL.warn : COL.hot;
const hexA = (hex, a) => { const n = parseInt(hex.slice(1), 16); return `rgba(${n >> 16},${(n >> 8) & 255},${n & 255},${a})`; };
const validHex = s => /^#[0-9a-f]{6}$/i.test(s || '');

const I = {
  panel: '<path d="M4 14a8 8 0 1 1 16 0"/><path d="M12 14l4-5"/><circle cx="12" cy="14" r="1.3"/><path d="M5 19.5h14"/>',
  monitor: '<path d="M2.5 12h4l2.5-7 4.5 14 2.5-7h5.5"/>',
  fan: '<circle cx="12" cy="12" r="1.8"/><path d="M12 10.2C11 7 12.2 3.8 15 3.5c2.6.1 3 3.2.3 5.6"/><path d="M13.8 12c3.2-1 6.4.2 6.7 3 -.1 2.6-3.2 3-5.6.3"/><path d="M12 13.8c1 3.2-.2 6.4-3 6.7-2.6-.1-3-3.2-.3-5.6"/><path d="M10.2 12c-3.2 1-6.4-.2-6.7-3 .1-2.6 3.2-3 5.6-.3"/>',
  rgb: '<path d="M9.5 18h5"/><path d="M10.5 21h3"/><path d="M12 3a6 6 0 0 0-4 10.5c.8.8 1.2 1.6 1.3 2.5h5.4c.1-.9.5-1.7 1.3-2.5A6 6 0 0 0 12 3z"/>',
  keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6 10h.01M9.5 10h.01M13 10h.01M16.5 10h.01M6 14h.01M18 14h.01M9 14h6"/>',
  bolt: '<path d="M13 2.5L4.5 13.5H11L10 21.5l8.5-11H12l1-8z"/>',
  list: '<path d="M8.5 6h11.5M8.5 12h11.5M8.5 18h11.5"/><path d="M4.5 6h.01M4.5 12h.01M4.5 18h.01"/>',
  cpu: '<rect x="6" y="6" width="12" height="12" rx="2"/><rect x="9.5" y="9.5" width="5" height="5" rx="1"/><path d="M9 2.5V6M15 2.5V6M9 18v3.5M15 18v3.5M2.5 9H6M2.5 15H6M18 9h3.5M18 15h3.5"/>',
  gpu: '<rect x="2.5" y="6.5" width="19" height="10" rx="2"/><circle cx="9" cy="11.5" r="2.6"/><circle cx="16" cy="11.5" r="2.6"/><path d="M6 16.5v3M10 16.5v3"/>',
  ram: '<rect x="2.5" y="8" width="19" height="8" rx="1.5"/><path d="M6.5 11v2M10 11v2M14 11v2M17.5 11v2M5 16v2.5M9 16v2.5M15 16v2.5M19 16v2.5"/>',
  disk: '<ellipse cx="12" cy="6.5" rx="8" ry="3"/><path d="M4 6.5v11c0 1.7 3.6 3 8 3s8-1.3 8-3v-11"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
  net: '<path d="M7 20V8"/><path d="M3.5 11.5L7 8l3.5 3.5"/><path d="M17 4v12"/><path d="M13.5 12.5L17 16l3.5-3.5"/>',
  down: '<path d="M12 4v14"/><path d="M6 12.5l6 6 6-6"/>',
  up: '<path d="M12 20V6"/><path d="M6 11.5l6-6 6 6"/>',
  shield: '<path d="M12 3l7.5 3v5.5c0 4.5-3.2 8-7.5 9.5-4.3-1.5-7.5-5-7.5-9.5V6L12 3z"/><path d="M9 12l2.2 2.2L15 10.3"/>',
  pencil: '<path d="M4 20l4.5-1L19 8.5 15.5 5 5 15.5 4 20z"/><path d="M13.5 7l3.5 3.5"/>',
  lock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/>',
  x: '<path d="M6 6l12 12M18 6L6 18"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  warn: '<path d="M12 3.5L2.5 20h19L12 3.5z"/><path d="M12 10v4.5M12 17.2h.01"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5.5M12 7.6h.01"/>',
  thermo: '<path d="M10 14.5V5a2 2 0 0 1 4 0v9.5a4 4 0 1 1-4 0z"/><path d="M12 9v7"/>',
  search: '<circle cx="11" cy="11" r="6.5"/><path d="M16 16l4.5 4.5"/>',
  battery: '<rect x="2.5" y="7.5" width="16.5" height="9" rx="2"/><path d="M21.5 10.5v3"/>',
  power: '<path d="M12 3v8"/><path d="M6.3 6.8a8 8 0 1 0 11.4 0"/>',
  refresh: '<path d="M20 4.5v5h-5"/><path d="M4 19.5v-5h5"/><path d="M19.4 9.5A8 8 0 0 0 5.6 7.5L4 9.5M4.6 14.5a8 8 0 0 0 13.8 2l1.6-2"/>',
  broom: '<path d="M19.5 3.5l-8 8"/><path d="M12.5 9.5l2.5 2.5-6.5 8.5H3.5l1-5.5z"/><path d="M7 13.5l3.5 3.5"/>',
  moon: '<path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z"/>',
  drop: '<path d="M12 3.5c3 4 5.5 6.8 5.5 10a5.5 5.5 0 0 1-11 0c0-3.2 2.5-6 5.5-10z"/>',
  wind: '<path d="M3 9h10a2.5 2.5 0 1 0-2.5-2.5"/><path d="M3 13h15a2.5 2.5 0 1 1-2.5 2.5"/><path d="M3 17h7"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2.5M12 19v2.5M2.5 12H5M19 12h2.5M5.3 5.3l1.8 1.8M16.9 16.9l1.8 1.8M5.3 18.7l1.8-1.8M16.9 7.1l1.8-1.8"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  mouse: '<rect x="6" y="2.5" width="12" height="19" rx="6"/><path d="M12 6.5v4"/>',
  headset: '<path d="M4 14v-2a8 8 0 0 1 16 0v2"/><rect x="3" y="14" width="4.5" height="6.5" rx="1.5"/><rect x="16.5" y="14" width="4.5" height="6.5" rx="1.5"/>',
  gamepad: '<path d="M7 8h10a4.5 4.5 0 0 1 4.4 5.4l-.8 4a2.3 2.3 0 0 1-4 1L15 16.5H9l-1.6 1.9a2.3 2.3 0 0 1-4-1l-.8-4A4.5 4.5 0 0 1 7 8z"/><path d="M8 11v3M6.5 12.5h3M15.5 12h.01M17.5 13.5h.01"/>',
  volume: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11"/>',
  mute: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M16 9.5l5 5M21 9.5l-5 5"/>',
  mic: '<rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21"/>',
  globe: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.6 3.7 5.6 3.7 9s-1.2 6.4-3.7 9c-2.5-2.6-3.7-5.6-3.7-9S9.5 5.6 12 3z"/>',
  chip: '<rect x="4" y="4" width="16" height="16" rx="3"/><path d="M9 9h6v6H9z"/>',
};
const icon = (name, cls = '') => `<svg class="ico ${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${I[name] || ''}</svg>`;

/* ══════════════════════ Componentes ══════════════════════ */
function createGauge(host, { label, unit, max = 100, size = 116 }) {
  const r = 46, c = 2 * Math.PI * r, arc = c * 0.75;
  host.innerHTML = `<div class="gauge" style="--size:${size}px">
    <svg viewBox="0 0 120 120"><circle class="g-bg" cx="60" cy="60" r="${r}" stroke-dasharray="${arc} ${c}" transform="rotate(135 60 60)"/>
    <circle class="g-val" cx="60" cy="60" r="${r}" stroke-dasharray="0 ${c}" transform="rotate(135 60 60)"/></svg>
    <div class="g-center"><div class="g-num"><span class="g-v">—</span><span class="g-u">${unit}</span></div><div class="g-label">${label}</div></div></div>`;
  const val = $('.g-val', host), v = $('.g-v', host);
  return {
    set(value, color) {
      const f = isNum(value) ? clamp(value / max, 0, 1) : 0;
      val.style.strokeDasharray = `${arc * f} ${c}`;
      val.style.stroke = color || COL.accent;
      setText(v, isNum(value) ? String(Math.round(value)) : '—');
    },
  };
}

const HIST = 120;
const hist = Object.create(null);
function push(k, v) { const a = hist[k] || (hist[k] = []); a.push(isNum(v) ? v : null); if (a.length > HIST) a.shift(); }

function drawChart(canvas, series, opts = {}) {
  if (!canvas) return;
  const w = canvas.clientWidth, hgt = canvas.clientHeight;
  if (!w || !hgt) return;
  const dpr = window.devicePixelRatio || 1;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(hgt * dpr)) { canvas.width = Math.round(w * dpr); canvas.height = Math.round(hgt * dpr); }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, hgt);
  const n = HIST, padT = 4, padB = 2, ih = hgt - padT - padB;
  if (opts.grid) {
    ctx.strokeStyle = 'rgba(255,255,255,.05)'; ctx.lineWidth = 1;
    for (let i = 1; i < 4; i++) { const y = Math.round(padT + ih * i / 4) + .5; ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }
  }
  let shared = null;
  if (opts.sharedMax) { shared = 1; for (const s of series) for (const v of (s.data || [])) if (isNum(v) && v > shared) shared = v; shared *= 1.18; }
  for (const s of series) {
    const data = s.data || [];
    if (!data.length) continue;
    const min = s.min ?? 0;
    let max = s.max ?? shared;
    if (max == null) { max = 1; for (const v of data) if (isNum(v) && v > max) max = v; max *= 1.18; }
    const step = w / (n - 1), x0 = w - (data.length - 1) * step;
    const X = i => x0 + i * step, Y = v => padT + ih - clamp((v - min) / (max - min), 0, 1) * ih;
    let seg = [];
    const flush = () => {
      if (seg.length > 1) {
        ctx.beginPath();
        seg.forEach(([x, y], i) => i ? ctx.lineTo(x, y) : ctx.moveTo(x, y));
        ctx.strokeStyle = s.color; ctx.lineWidth = s.width || 1.7; ctx.lineJoin = 'round'; ctx.stroke();
        if (s.fill !== false) {
          ctx.lineTo(seg[seg.length - 1][0], hgt); ctx.lineTo(seg[0][0], hgt); ctx.closePath();
          const gr = ctx.createLinearGradient(0, 0, 0, hgt);
          gr.addColorStop(0, hexA(s.color, .26)); gr.addColorStop(1, hexA(s.color, 0));
          ctx.fillStyle = gr; ctx.fill();
        }
      }
      seg = [];
    };
    data.forEach((v, i) => { if (isNum(v)) seg.push([X(i), Y(v)]); else flush(); });
    flush();
  }
}

function toast(msg, type = 'ok', ms = 3800) {
  const t = h(`<div class="toast ${type}">${icon(type === 'err' ? 'warn' : type === 'info' ? 'info' : 'check')}<span>${esc(msg)}</span></div>`);
  $('#toasts').appendChild(t);
  requestAnimationFrame(() => t.classList.add('in'));
  setTimeout(() => { t.classList.remove('in'); setTimeout(() => t.remove(), 300); }, ms);
}

/** Ejecuta una llamada y avisa con un toast del resultado. Nunca lanza. */
async function attempt(fn, okMsg) {
  try {
    const r = await fn();
    if (okMsg) toast(typeof okMsg === 'function' ? okMsg(r) : okMsg);
    return { ok: true, result: r };
  } catch (e) {
    toast((e && e.message) || String(e), 'err', 6000);
    return { ok: false, error: e };
  }
}

function modal({ title, body = '', okLabel = 'Aceptar', cancelLabel = 'Cancelar', danger = false, check = null }) {
  return new Promise(resolve => {
    const m = h(`<div class="modal-back"><div class="modal" role="dialog" aria-modal="true">
      <h3>${esc(title)}</h3><div class="modal-body">${body}</div>
      ${check ? `<label class="check"><input type="checkbox" class="cb" data-check><span>${esc(check)}</span></label>` : ''}
      <div class="modal-actions"><button class="btn ghost" data-a="no">${esc(cancelLabel)}</button><button class="btn ${danger ? 'danger' : 'primary'}" data-a="yes" ${check ? 'disabled' : ''}>${esc(okLabel)}</button></div></div></div>`);
    const done = v => { m.classList.remove('in'); setTimeout(() => m.remove(), 180); document.removeEventListener('keydown', onKey); resolve(v); };
    const onKey = e => { if (e.key === 'Escape') done(false); };
    m.addEventListener('click', e => {
      if (e.target === m) return done(false);
      const a = e.target.closest('[data-a]');
      if (a && !a.disabled) done(a.dataset.a === 'yes');
    });
    const chk = $('[data-check]', m);
    if (chk) chk.addEventListener('change', () => { $('[data-a=yes]', m).disabled = !chk.checked; });
    document.addEventListener('keydown', onKey);
    $('#modal-root').appendChild(m);
    requestAnimationFrame(() => m.classList.add('in'));
  });
}

function paintRange(input) {
  const min = +input.min, max = +input.max;
  const p = max > min ? ((+input.value - min) / (max - min)) * 100 : 0;
  input.style.setProperty('--p', clamp(p, 0, 100) + '%');
}
document.addEventListener('input', e => { if (e.target.matches && e.target.matches('input[type=range]')) paintRange(e.target); });

/** Fila con deslizador + casilla numérica sincronizados. */
function sliderRow({ title, desc, min, max, step, digits = 0, unit = '', value, warm = false }) {
  const el = h(`<div class="srow"><label><b>${esc(title)}</b>${desc ? esc(desc) : ''}</label>
    <input type="range" class="${warm ? 'warm' : ''}"><div class="val"><input type="number" class="field"> <span class="sub">${esc(unit)}</span></div></div>`);
  const range = $('input[type=range]', el), num = $('input[type=number]', el);
  let cb = null, lim = { min, max, step };
  const apply = () => { for (const i of [range, num]) { i.min = lim.min; i.max = lim.max; i.step = lim.step; } };
  const norm = v => { v = clamp(+v, lim.min, lim.max); const k = Math.round((v - lim.min) / lim.step); return +(lim.min + k * lim.step).toFixed(4); };
  const set = (v, silent) => { v = isNum(+v) ? norm(v) : lim.min; range.value = v; num.value = (+v).toFixed(digits); paintRange(range); if (!silent && cb) cb(v); };
  apply(); set(value ?? min, true);
  range.addEventListener('input', () => { num.value = (+range.value).toFixed(digits); if (cb) cb(+range.value); });
  num.addEventListener('change', () => set(num.value));
  return {
    el, get: () => +range.value, set,
    setLimits(mn, mx, st) { lim = { min: mn, max: mx, step: st ?? lim.step }; apply(); set(range.value, true); },
    onInput(fn) { cb = fn; },
    setDisabled(b) { range.disabled = num.disabled = !!b; el.classList.toggle('off', !!b); },
  };
}

function syncList(host, items, key, create, update, emptyHtml) {
  if (!items || !items.length) {
    if (host._sig !== '∅') { host._sig = '∅'; host._els = []; host.innerHTML = emptyHtml || ''; }
    return;
  }
  const sig = items.map(key).join('|');
  if (host._sig !== sig) {
    host._sig = sig; host.innerHTML = '';
    host._els = items.map(it => { const el = create(it); host.appendChild(el); return el; });
  }
  items.forEach((it, i) => update(host._els[i], it));
}

const needAdminMsg = () => state.init && !state.init.isAdmin ? 'Necesita permisos de administrador' : 'Sin datos';

/* ══════════════════════ Estado global ══════════════════════ */
const state = { init: null, settings: {}, sensors: null, page: null };

const PAGES = [
  { id: 'panel', label: 'Panel', icon: 'panel', title: 'Panel', sub: 'Resumen del equipo en tiempo real' },
  { id: 'monitor', label: 'Monitor', icon: 'monitor', title: 'Monitor', sub: 'Temperaturas, uso y sensores al detalle' },
  { id: 'fans', label: 'Ventiladores', icon: 'fan', title: 'Ventiladores', sub: 'Velocidad y curvas de cada ventilador' },
  { id: 'rgb', label: 'RGB', icon: 'rgb', title: 'Iluminación RGB', sub: 'Tiras y ventiladores conectados a los conectores RGB de la placa base' },
  { id: 'perif', label: 'Periféricos', icon: 'mouse', title: 'Periféricos', sub: 'Teclado, ratón y demás dispositivos conectados' },
  { id: 'oc', label: 'Overclock', icon: 'bolt', title: 'Overclock', sub: 'Tarjeta gráfica, procesador y memoria' },
  { id: 'proc', label: 'Procesos', icon: 'list', title: 'Procesos', sub: 'Cierra lo que sobra para que el PC vaya más fluido' },
];

async function saveSettings(patch) {
  const r = await attempt(() => api.call('settings.set', { patch }));
  if (r.ok && r.result && typeof r.result === 'object') state.settings = r.result;
  else if (r.ok) Object.assign(state.settings, patch);
  return r.ok;
}

/* ══════════════════════ Iconos del tiempo ══════════════════════ */
const WX = (() => {
  const cloud = (x, y, s, fill) => `<path transform="translate(${x} ${y}) scale(${s})" d="M11 27a8.5 8.5 0 0 1-.8-17A11.5 11.5 0 0 1 32 11.6 7.8 7.8 0 0 1 31 27z" fill="${fill}"/>`;
  const sun = (cx, cy, r) => {
    let rays = '';
    for (let i = 0; i < 8; i++) { const a = i * Math.PI / 4; rays += `<line x1="${cx + Math.cos(a) * (r + 3.5)}" y1="${cy + Math.sin(a) * (r + 3.5)}" x2="${cx + Math.cos(a) * (r + 7.5)}" y2="${cy + Math.sin(a) * (r + 7.5)}"/>`; }
    return `<g stroke="#ffc94d" stroke-width="2.6" stroke-linecap="round">${rays}</g><circle cx="${cx}" cy="${cy}" r="${r}" fill="#ffc94d"/>`;
  };
  const moon = (x, y, s) => `<path transform="translate(${x} ${y}) scale(${s})" d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z" fill="#cdd6f4"/>`;
  const drops = (n, col = '#4da3ff') => { let o = ''; const xs = n === 2 ? [19, 29] : [15, 24, 33]; xs.forEach((x, i) => o += `<line x1="${x}" y1="${36 + (i % 2) * 2}" x2="${x - 2.5}" y2="${42 + (i % 2) * 2}" stroke="${col}" stroke-width="2.6" stroke-linecap="round"/>`); return o; };
  const flakes = () => [15, 24, 33].map((x, i) => `<circle cx="${x}" cy="${39 + (i % 2) * 3}" r="2" fill="#dfe9ff"/>`).join('');
  const wrap = inner => `<svg viewBox="0 0 48 48" aria-hidden="true">${inner}</svg>`;
  const icons = {
    'clear-day': () => wrap(sun(24, 24, 9)),
    'clear-night': () => wrap(moon(8, 8, 1.35)),
    'partly-day': () => wrap(sun(17, 16, 7) + cloud(6, 12, 1, '#c9d3e6')),
    'partly-night': () => wrap(moon(4, 2, 1.05) + cloud(6, 12, 1, '#aeb9d0')),
    cloudy: () => wrap(cloud(12, 4, .8, '#7f8ba3') + cloud(4, 12, 1, '#c9d3e6')),
    fog: () => wrap(cloud(5, 3, 1, '#aeb9d0') + '<g stroke="#8d98ad" stroke-width="2.6" stroke-linecap="round"><line x1="10" y1="36" x2="38" y2="36"/><line x1="14" y1="42" x2="34" y2="42"/></g>'),
    drizzle: () => wrap(cloud(5, 4, 1, '#c9d3e6') + drops(2)),
    rain: () => wrap(cloud(5, 4, 1, '#9aa7c0') + drops(3)),
    snow: () => wrap(cloud(5, 4, 1, '#c9d3e6') + flakes()),
    storm: () => wrap(cloud(5, 4, 1, '#7f8ba3') + '<path d="M25 32l-6 8h5l-2 6 7-9h-5l2-5z" fill="#ffc94d"/>'),
  };
  const table = [
    [[0], 'clear', 'Despejado'], [[1], 'clear', 'Poco nuboso'], [[2], 'partly', 'Parcialmente nublado'], [[3], 'cloudy', 'Nublado'],
    [[45, 48], 'fog', 'Niebla'], [[51, 53, 55], 'drizzle', 'Llovizna'], [[56, 57], 'drizzle', 'Llovizna helada'],
    [[61], 'rain', 'Lluvia débil'], [[63], 'rain', 'Lluvia'], [[65], 'rain', 'Lluvia fuerte'], [[66, 67], 'rain', 'Lluvia helada'],
    [[71, 73, 75, 77], 'snow', 'Nieve'], [[80, 81], 'rain', 'Chubascos'], [[82], 'rain', 'Chubascos fuertes'], [[85, 86], 'snow', 'Chubascos de nieve'],
    [[95], 'storm', 'Tormenta'], [[96, 99], 'storm', 'Tormenta con granizo'],
  ];
  function info(code, isDay = true) {
    const row = table.find(r => r[0].includes(code)) || [[], 'cloudy', 'Sin datos'];
    let kind = row[1];
    if (kind === 'clear' || kind === 'partly') kind += isDay ? '-day' : '-night';
    return { svg: icons[kind](), text: row[2] };
  }
  return { info };
})();

/* ══════════════════════ Rueda de color ══════════════════════ */
/** Rueda HSV: el ángulo es el tono, la distancia al centro la saturación (centro = blanco) y el
 *  deslizador de luminosidad oscurece el color. onChange recibe un hex '#rrggbb'. */
function colorWheel(onChange) {
  const el = h(`<div class="cw">
    <div class="cw-wheel"><canvas width="320" height="320"></canvas><i class="cw-dot"></i></div>
    <div class="cw-lum"><span>Más oscuro</span><input type="range" min="4" max="100" step="1" aria-label="Luminosidad"><span>Más claro</span></div>
  </div>`);
  const cv = $('canvas', el), dot = $('.cw-dot', el), lum = $('input', el), wrap = $('.cw-wheel', el);
  let hsv = { h: 0, s: 0, v: 1 };
  const hsv2rgb = (H, S, V) => {
    const f = n => { const k = (n + H / 60) % 6; return V - V * S * Math.max(0, Math.min(k, 4 - k, 1)); };
    return [f(5), f(3), f(1)].map(x => Math.round(x * 255));
  };
  const hex = () => '#' + hsv2rgb(hsv.h, hsv.s, hsv.v).map(x => x.toString(16).padStart(2, '0')).join('');
  // Dibujo de la rueda (una sola vez)
  const ctx = cv.getContext('2d'), N = cv.width, R = N / 2, img = ctx.createImageData(N, N);
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
    const dx = x - R + .5, dy = y - R + .5, d = Math.hypot(dx, dy) / R, i = (y * N + x) * 4;
    if (d > 1) { img.data[i + 3] = 0; continue; }
    const [r, g, b] = hsv2rgb((Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360, d, 1);
    img.data[i] = r; img.data[i + 1] = g; img.data[i + 2] = b;
    img.data[i + 3] = d > .985 ? Math.round((1 - d) / .015 * 255) : 255;   // borde suavizado
  }
  ctx.putImageData(img, 0, 0);
  const paint = () => {
    const a = hsv.h * Math.PI / 180;
    dot.style.left = (50 + Math.cos(a) * hsv.s * 50) + '%';
    dot.style.top = (50 + Math.sin(a) * hsv.s * 50) + '%';
    const full = '#' + hsv2rgb(hsv.h, hsv.s, 1).map(x => x.toString(16).padStart(2, '0')).join('');
    dot.style.background = hex();
    lum.style.background = `linear-gradient(90deg, #000, ${full})`;
    cv.style.filter = `brightness(${.35 + .65 * hsv.v})`;
    lum.value = Math.round(hsv.v * 100);
  };
  const fromPoint = e => {
    const r = wrap.getBoundingClientRect();
    const dx = e.clientX - r.left - r.width / 2, dy = e.clientY - r.top - r.height / 2;
    hsv.h = (Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360;
    hsv.s = Math.min(1, Math.hypot(dx, dy) / (r.width / 2));
    paint(); onChange(hex());
  };
  wrap.addEventListener('pointerdown', e => { wrap.setPointerCapture(e.pointerId); fromPoint(e); });
  wrap.addEventListener('pointermove', e => { if (wrap.hasPointerCapture(e.pointerId)) fromPoint(e); });
  lum.addEventListener('input', () => { hsv.v = +lum.value / 100; paint(); onChange(hex()); });
  return {
    el,
    /** Coloca la rueda en un color dado sin avisar. */
    set(c) {
      if (c && c.toLowerCase() === hex()) return;
      const [r, g, b] = (v => [v >> 16, (v >> 8) & 255, v & 255])(parseInt(String(c).slice(1), 16) || 0).map(x => x / 255), mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
      let H = 0;
      if (d) H = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
      hsv = { h: d ? (H * 60 + 360) % 360 : hsv.h, s: mx ? d / mx : 0, v: Math.max(.04, mx) };
      paint();
    },
    setDisabled(b) { el.classList.toggle('off', !!b); lum.disabled = !!b; },
  };
}

/* ══════════════════════ PANEL ══════════════════════ */
const Countdown = {
  list() {
    const l = Array.isArray(state.settings.countdowns) ? state.settings.countdowns.filter(c => c && c.date) : [];
    return l.length ? l : [{ name: 'GTA VI', date: '2026-11-19T00:00:00' }];
  },
  index() { const n = this.list().length; return clamp(Math.floor(+state.settings.countdownIndex || 0), 0, n - 1); },
  current() { return this.list()[this.index()]; },
  mount(root) {
    this.root = root;
    const ed = $('#cd-editor', root), name = $('#cd-name-in', root), date = $('#cd-date-in', root);
    const p = n => String(n).padStart(2, '0');
    const open = mode => {
      this.mode = mode;
      const c = mode === 'new' ? { name: '', date: '' } : this.current();
      const t = new Date(c.date);
      name.value = c.name || '';
      date.value = isNaN(t) ? '' : `${t.getFullYear()}-${p(t.getMonth() + 1)}-${p(t.getDate())}T${p(t.getHours())}:${p(t.getMinutes())}`;
      $('#cd-del', root).hidden = mode === 'new' || this.list().length < 2;
      ed.hidden = false; name.focus();
    };
    $('#cd-edit', root).addEventListener('click', () => { if (!ed.hidden && this.mode === 'edit') ed.hidden = true; else open('edit'); });
    $('#cd-add', root).addEventListener('click', () => open('new'));
    $('#cd-cancel', root).addEventListener('click', () => { ed.hidden = true; });
    $('#cd-prev', root).addEventListener('click', () => this.go(-1));
    $('#cd-next', root).addEventListener('click', () => this.go(1));
    $('#cd-del', root).addEventListener('click', async () => {
      const l = this.list().slice(); l.splice(this.index(), 1);
      if (await saveSettings({ countdowns: l, countdownIndex: 0 })) { ed.hidden = true; this.update(); toast('Cuenta atrás eliminada'); }
    });
    ed.addEventListener('submit', async e => {
      e.preventDefault();
      const n = name.value.trim();
      if (!n) return toast('Ponle un nombre', 'err');
      if (!date.value || isNaN(new Date(date.value))) return toast('Fecha no válida', 'err');
      const item = { name: n.slice(0, 40), date: date.value.length === 16 ? date.value + ':00' : date.value };
      const l = this.list().slice(); let idx = this.index();
      if (this.mode === 'new') { l.push(item); idx = l.length - 1; } else l[idx] = item;
      if (await saveSettings({ countdowns: l, countdownIndex: idx })) { ed.hidden = true; this.update(); toast(this.mode === 'new' ? 'Cuenta atrás creada' : 'Cuenta atrás guardada'); }
    });
    this.update();
    setInterval(() => this.update(), 250);
  },
  async go(d) {
    const n = this.list().length; if (n < 2) return;
    await saveSettings({ countdownIndex: (this.index() + d + n) % n });
    this.update();
  },
  update() {
    const root = this.root; if (!root) return;
    const c = this.current(), l = this.list(), t = new Date(c.date), diff = t - Date.now();
    setText('#cd-name', c.name || 'Cuenta atrás', root);
    setText('#cd-pos', l.length > 1 ? `${this.index() + 1} de ${l.length}` : '', root);
    $('#cd-prev', root).hidden = $('#cd-next', root).hidden = l.length < 2;
    let txt = isNaN(t) ? '—' : t.toLocaleDateString(I18N.locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
    if (!isNaN(t) && (t.getHours() || t.getMinutes())) txt += ' · ' + t.toLocaleTimeString(I18N.locale, { hour: '2-digit', minute: '2-digit' });
    setText('#cd-date-text', txt, root);
    const out = !(diff > 0);
    $('#cd-count', root).hidden = out; $('#cd-out', root).hidden = !out; $('#cd-barwrap', root).hidden = out;
    if (out) return;
    const s = Math.floor(diff / 1000);
    setText('#cd-d', String(Math.floor(s / 86400)), root);
    setText('#cd-h', String(Math.floor(s % 86400 / 3600)).padStart(2, '0'), root);
    setText('#cd-m', String(Math.floor(s % 3600 / 60)).padStart(2, '0'), root);
    setText('#cd-s', String(s % 60).padStart(2, '0'), root);
    // Barra: último tramo de 30 días antes de la fecha
    const span = 30 * 86400e3;
    $('#cd-bar', root).style.width = clamp((1 - diff / span) * 100, 2, 100) + '%';
  },
};

const Weather = {
  data: null, at: 0, timer: null, picking: false,
  mount(root) {
    this.root = root;
    $('#wx-refresh', root).addEventListener('click', () => this.load(true));
    $('#wx-change', root).addEventListener('click', () => this.picker());
    this.load(false);
    this.timer = setInterval(() => this.load(false), 15 * 60 * 1000);
    window.addEventListener('online', () => this.load(false));
  },
  place() {
    const w = state.settings.weather;
    return w && isNum(w.lat) && isNum(w.lon) ? { name: w.name || 'Mi ubicación', lat: w.lat, lon: w.lon } : null;
  },
  /** Buscador de ubicación (geocodificación gratuita de Open-Meteo: sin cuenta ni claves). */
  picker() {
    this.picking = true;
    const root = this.root, body = $('#wx-body', root), had = this.place();
    $('#wx-refresh', root).hidden = true; $('#wx-change', root).hidden = true;
    setText('#wx-place', 'Elige tu ubicación', root);
    body.innerHTML = `
      <form class="wx-search" id="wx-form">
        <input class="field" id="wx-q" placeholder="Escribe tu ciudad o pueblo" autocomplete="off" maxlength="80">
        <button class="btn sm primary">${icon('search')}Buscar</button>
        ${had ? '<button type="button" class="btn sm ghost" id="wx-cancel">Cancelar</button>' : ''}
      </form>
      <div class="wx-results" id="wx-results"><p class="hint">Busca tu localidad para ver su tiempo. Se guarda solo en este PC.</p></div>`;
    const q = $('#wx-q', body), res = $('#wx-results', body);
    q.focus();
    const done = () => { this.picking = false; $('#wx-refresh', root).hidden = false; $('#wx-change', root).hidden = false; };
    const cancel = $('#wx-cancel', body);
    if (cancel) cancel.addEventListener('click', () => { done(); setText('#wx-place', had.name, root); if (this.data) this.render(false); else this.load(false); });
    $('#wx-form', body).addEventListener('submit', async e => {
      e.preventDefault();
      const text = q.value.trim(); if (text.length < 2) return;
      res.innerHTML = '<div class="skeleton" style="height:60px"></div>';
      try {
        const r = await fetch(`https://geocoding-api.open-meteo.com/v1/search?count=6&language=${I18N.code}&format=json&name=${encodeURIComponent(text)}`, { cache: 'no-store' });
        const j = await r.json();
        const items = (j.results || []).filter(x => isNum(x.latitude) && isNum(x.longitude));
        if (!items.length) { res.innerHTML = '<p class="hint">No se ha encontrado ningún sitio con ese nombre.</p>'; return; }
        res.innerHTML = items.map((x, i) => {
          const where = [x.admin2, x.admin1, x.country].filter(Boolean).filter((v, k, a) => a.indexOf(v) === k && v !== x.name).join(', ');
          return `<button type="button" class="wx-res" data-i="${i}"><b>${esc(x.name)}</b><span>${esc(where)}</span></button>`;
        }).join('');
        res.onclick = async ev => {
          const b = ev.target.closest('[data-i]'); if (!b) return;
          const x = items[+b.dataset.i];
          if (await saveSettings({ weather: { name: x.name, lat: +x.latitude.toFixed(3), lon: +x.longitude.toFixed(3) } })) {
            done(); this.data = null; this.load(false); toast('Ubicación guardada');
          }
        };
      } catch { res.innerHTML = '<p class="hint">Sin conexión: no se ha podido buscar.</p>'; }
    });
  },
  async load(manual) {
    if (this.picking) return;
    const body = $('#wx-body', this.root), p = this.place();
    if (!p) return this.picker();
    setText('#wx-place', p.name, this.root);
    if (!this.data) body.innerHTML = '<div class="skeleton"></div>';
    const url = `https://api.open-meteo.com/v1/forecast?latitude=${p.lat}&longitude=${p.lon}` +
      '&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day' +
      '&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=4';
    const ac = new AbortController();
    const to = setTimeout(() => ac.abort(), 10000);
    try {
      const r = await fetch(url, { signal: ac.signal, cache: 'no-store' });
      if (!r.ok) throw new Error('HTTP ' + r.status);
      const j = await r.json();
      if (!j.current || !j.daily) throw new Error('Respuesta incompleta');
      this.data = j; this.at = Date.now();
      this.render(false);
      if (manual) toast('Clima actualizado');
    } catch (e) {
      if (this.data) this.render(true);
      else {
        body.innerHTML = `<div class="empty" style="padding:14px 8px">${icon('wind')}<h3>Sin conexión</h3><p>No se ha podido consultar el tiempo. Comprueba tu conexión a internet.</p><button class="btn sm" id="wx-retry">Reintentar</button></div>`;
        $('#wx-retry', body).addEventListener('click', () => this.load(true));
      }
      if (manual) toast('No se ha podido actualizar el clima', 'err');
    } finally { clearTimeout(to); }
  },
  render(stale) {
    const j = this.data, c = j.current, d = j.daily, body = $('#wx-body', this.root);
    const now = WX.info(c.weather_code, c.is_day !== 0);
    let days = '';
    for (let i = 1; i < Math.min(4, d.time.length); i++) {
      const w = WX.info(d.weather_code[i], true);
      const name = new Date(d.time[i] + 'T12:00:00').toLocaleDateString(I18N.locale, { weekday: 'short', day: 'numeric' });
      const pp = d.precipitation_probability_max ? d.precipitation_probability_max[i] : null;
      days += `<div class="wx-day" title="${esc(w.text)}">${w.svg}<div><span class="d">${esc(name)}</span><span class="t">${fmt(d.temperature_2m_max[i])}°<i>${fmt(d.temperature_2m_min[i])}°</i></span><span class="p" title="Probabilidad de lluvia">${icon('drop')}${fmt(pp, 0, ' %')}</span></div></div>`;
    }
    body.innerHTML = `
      <div class="wx-now">
        <div class="wx-icon">${now.svg}</div>
        <div class="wx-temp"><b>${fmt(c.temperature_2m)}°</b><span>${esc(now.text)}</span></div>
        <div class="wx-meta">
          <div>${icon('thermo')}Sensación <b>${fmt(c.apparent_temperature)}°</b></div>
          <div>${icon('drop')}Humedad <b>${fmt(c.relative_humidity_2m)} %</b></div>
          <div>${icon('wind')}Viento <b>${fmt(c.wind_speed_10m)} km/h</b></div>
          <div>Hoy <b>${fmt(d.temperature_2m_max[0])}° / ${fmt(d.temperature_2m_min[0])}°</b></div>
        </div>
      </div>
      <div class="wx-days">${days}</div>
      ${stale ? `<div class="wx-stale">Sin conexión · datos de las ${new Date(this.at).toLocaleTimeString(I18N.locale, { hour: '2-digit', minute: '2-digit' })}</div>` : ''}`;
  },
};

const Panel = {
  mount(root) {
    this.root = root;
    root.innerHTML = `
    <div class="grid">
      <div class="card span4">
        <div class="card-head">${icon('cpu')}<div><h3>Procesador</h3><span class="sub" id="d-cpu-name">—</span></div></div>
        <div class="gauges"><div id="g-cpu-temp"></div><div id="g-cpu-load"></div></div>
        <div class="stats"><div><span>Reloj</span><b id="d-cpu-clock">—</b></div><div><span>Potencia</span><b id="d-cpu-power">—</b></div><div><span>Voltaje</span><b id="d-cpu-volt">—</b></div></div>
        <canvas class="spark" id="sp-cpu"></canvas>
      </div>
      <div class="card span4">
        <div class="card-head">${icon('gpu')}<div><h3>Tarjeta gráfica</h3><span class="sub" id="d-gpu-name">—</span></div></div>
        <div class="gauges"><div id="g-gpu-temp"></div><div id="g-gpu-load"></div></div>
        <div class="stats"><div><span>Ventilador</span><b id="d-gpu-fan">—</b></div><div><span>Potencia</span><b id="d-gpu-power">—</b></div><div><span>VRAM</span><b id="d-gpu-vram">—</b></div></div>
        <canvas class="spark" id="sp-gpu"></canvas>
      </div>
      <div class="card span4">
        <div class="card-head">${icon('ram')}<div><h3>Memoria</h3><span class="sub" id="d-ram-name">—</span></div></div>
        <div class="ram-wrap"><div id="g-ram"></div>
          <div class="ram-info"><div>En uso <b id="d-ram-used">—</b></div><div>Libre <b id="d-ram-free">—</b></div><div>Total <b id="d-ram-total">—</b></div></div>
        </div>
        <div class="net-nums" style="margin-top:10px">
          <div><span>${icon('down', 'dn')}Descarga</span><b id="d-net-down">—</b></div>
          <div><span>${icon('up', 'up')}Subida</span><b id="d-net-up">—</b></div>
        </div>
        <canvas class="spark" id="sp-net"></canvas>
      </div>

      <div class="card span7 gta">
        <div class="gta-head">
          <div class="gta-logo"><span id="cd-name">GTA VI</span><small>Cuenta atrás <span id="cd-pos"></span></small></div>
          <div class="gta-date"><span id="cd-date-text"></span>
            <button class="icon-btn" id="cd-prev" title="Anterior" hidden>‹</button><button class="icon-btn" id="cd-next" title="Siguiente" hidden>›</button>
            <button class="icon-btn" id="cd-edit" title="Editar esta cuenta atrás">${icon('pencil')}</button>
            <button class="icon-btn" id="cd-add" title="Nueva cuenta atrás">${icon('plus')}</button></div>
        </div>
        <form class="gta-editor" id="cd-editor" hidden>
          <input class="field" id="cd-name-in" placeholder="Nombre (p. ej. Vacaciones)" maxlength="40">
          <input type="datetime-local" class="field" id="cd-date-in">
          <button class="btn sm primary">Guardar</button>
          <button type="button" class="btn sm danger" id="cd-del" hidden>Eliminar</button>
          <button type="button" class="btn sm ghost" id="cd-cancel">Cancelar</button>
        </form>
        <div class="gta-count" id="cd-count">
          <div class="tbox"><b id="cd-d">0</b><span>días</span></div>
          <div class="tbox"><b id="cd-h">00</b><span>horas</span></div>
          <div class="tbox"><b id="cd-m">00</b><span>minutos</span></div>
          <div class="tbox"><b id="cd-s">00</b><span>segundos</span></div>
        </div>
        <div class="gta-out" id="cd-out" hidden>¡Ya ha llegado!</div>
        <div class="gta-bar" id="cd-barwrap"><i id="cd-bar"></i></div>
      </div>
      <div class="card span5">
        <div class="card-head">${icon('sun')}<div><h3>Clima</h3><span class="sub" id="wx-place">—</span></div>
          <div class="right"><button class="icon-btn" id="wx-change" title="Cambiar ubicación">${icon('pencil')}</button><button class="icon-btn" id="wx-refresh" title="Actualizar">${icon('refresh')}</button></div></div>
        <div id="wx-body"><div class="skeleton"></div></div>
      </div>

      <div class="card span6">
        <div class="card-head">${icon('fan')}<div><h3>Ventiladores</h3></div><div class="right"><button class="btn sm ghost" data-go="fans">Controlar</button></div></div>
        <div class="rows two" id="d-fans"></div>
      </div>
      <div class="card span6">
        <div class="card-head">${icon('disk')}<div><h3>Almacenamiento</h3></div></div>
        <div class="rows" id="d-drives"></div>
      </div>
    </div>`;
    this.g = {
      cpuT: createGauge($('#g-cpu-temp', root), { label: 'Temperatura', unit: '°C', size: 104 }),
      cpuL: createGauge($('#g-cpu-load', root), { label: 'Uso', unit: '%', size: 104 }),
      gpuT: createGauge($('#g-gpu-temp', root), { label: 'Temperatura', unit: '°C', size: 104 }),
      gpuL: createGauge($('#g-gpu-load', root), { label: 'Uso', unit: '%', size: 104 }),
      ram: createGauge($('#g-ram', root), { label: 'En uso', unit: '%', size: 104 }),
    };
    root.addEventListener('click', e => { const g = e.target.closest('[data-go]'); if (g) showPage(g.dataset.go); });
    Countdown.mount(root);
    Weather.mount(root);
  },
  show() {
    const hw = (state.init && state.init.hardware) || {};
    setText('#d-cpu-name', hw.cpu || '—', this.root);
    setText('#d-gpu-name', hw.gpu || '—', this.root);
    setText('#d-ram-name', hw.ramTotalGb ? `${hw.ramTotalGb} GB${hw.ramSpeed ? ' · ' + hw.ramSpeed + ' MHz' : ''}` : '—', this.root);
  },
  tick(s) {
    const root = this.root, c = s.cpu || {}, g = s.gpu || {}, r = s.ram || {}, n = s.net || {};
    this.g.cpuT.set(c.temp, tempHex(c.temp)); this.g.cpuL.set(c.load, loadHex(c.load));
    this.g.gpuT.set(g.temp, tempHex(g.temp)); this.g.gpuL.set(g.load, loadHex(g.load));
    this.g.ram.set(r.load, loadHex(r.load));
    setText('#d-cpu-clock', fmtClock(c.clockMhz), root);
    setText('#d-cpu-power', fmt(c.power, 0, ' W'), root);
    setText('#d-cpu-volt', fmt(c.voltage, 2, ' V'), root);
    setText('#d-gpu-fan', isNum(g.fanRpm) ? fmt(g.fanRpm, 0, ' rpm') : fmt(g.fanPct, 0, ' %'), root);
    setText('#d-gpu-power', fmt(g.power, 0, ' W'), root);
    setText('#d-gpu-vram', isNum(g.vramUsedMb) ? `${fmtGb(g.vramUsedMb)} / ${fmtGb(g.vramTotalMb)} GB` : '—', root);
    setText('#d-ram-used', fmt(r.usedGb, 1, ' GB'), root);
    setText('#d-ram-free', isNum(r.usedGb) && isNum(r.totalGb) ? fmt(r.totalGb - r.usedGb, 1, ' GB') : '—', root);
    setText('#d-ram-total', fmt(r.totalGb, 1, ' GB'), root);
    setText('#d-net-down', fmtRate(n.downKbps), root);
    setText('#d-net-up', fmtRate(n.upKbps), root);
    drawChart($('#sp-cpu', root), [{ data: hist.cpuLoad, color: COL.blue, max: 100 }, { data: hist.cpuTemp, color: COL.accent, max: 100, fill: false }]);
    drawChart($('#sp-gpu', root), [{ data: hist.gpuLoad, color: COL.blue, max: 100 }, { data: hist.gpuTemp, color: COL.accent, max: 100, fill: false }]);
    drawChart($('#sp-net', root), [{ data: hist.down, color: COL.accent }, { data: hist.up, color: COL.blue, fill: false }], { sharedMax: true });

    syncList($('#d-fans', root), s.fans, f => f.id,
      f => h(`<div class="row"><span class="name">${esc(f.name)}</span><span class="v"><span data-v></span><small>rpm</small></span><div class="bar"><i></i></div></div>`),
      (el, f) => {
        setText($('[data-v]', el), fmt(f.rpm));
        const p = isNum(f.pct) ? f.pct : isNum(f.rpm) ? clamp(f.rpm / 22, 0, 100) : 0;
        $('.bar i', el).style.width = p + '%';
      }, `<div class="mini-empty">${needAdminMsg()}</div>`);
    syncList($('#d-drives', root), s.drives, d => d.name,
      d => h(`<div class="row"><span class="name">${esc(d.name)}</span><span class="v"><span data-t></span><small data-u></small></span><div class="bar"><i></i></div></div>`),
      (el, d) => {
        const t = $('[data-t]', el); setText(t, fmt(d.temp, 0, ' °C')); t.style.color = tempHex(d.temp);
        setText($('[data-u]', el), isNum(d.usedPct) ? fmt(d.usedPct, 0, ' % ocupado') : '');
        const b = $('.bar i', el); b.style.width = (isNum(d.usedPct) ? d.usedPct : 0) + '%'; b.style.background = d.usedPct > 90 ? COL.hot : d.usedPct > 75 ? COL.warn : COL.blue;
      }, '<div class="mini-empty">Sin datos de discos</div>');
  },
};

/* ══════════════════════ MONITOR ══════════════════════ */
const Monitor = {
  mount(root) {
    this.root = root;
    root.innerHTML = `
    <div class="grid">
      <div class="card span7">
        <div class="card-head">${icon('cpu')}<div><h3>Procesador</h3><span class="sub" id="m-cpu-name">—</span></div>
          <div class="right legend"><span><i style="background:${COL.blue}"></i>Uso</span><span><i style="background:${COL.accent}"></i>Temperatura</span></div></div>
        <canvas class="chart" id="m-cpu-chart"></canvas>
        <div class="stats four"><div><span>Uso</span><b id="m-cpu-load">—</b></div><div><span>Temperatura</span><b id="m-cpu-temp">—</b></div><div><span>Reloj</span><b id="m-cpu-clock">—</b></div><div><span>Potencia</span><b id="m-cpu-power">—</b></div></div>
        <div class="cores" id="m-cores"></div>
      </div>
      <div class="card span5">
        <div class="card-head">${icon('thermo')}<div><h3>Temperaturas</h3><span class="sub">Todos los sensores detectados</span></div></div>
        <div id="m-temps"></div>
      </div>
      <div class="card span7">
        <div class="card-head">${icon('gpu')}<div><h3>Tarjeta gráfica</h3><span class="sub" id="m-gpu-name">—</span></div>
          <div class="right legend"><span><i style="background:${COL.blue}"></i>Uso</span><span><i style="background:${COL.accent}"></i>Temperatura</span><span><i style="background:${COL.violet}"></i>Consumo</span></div></div>
        <canvas class="chart" id="m-gpu-chart"></canvas>
        <div class="stats four">
          <div><span>Uso</span><b id="m-gpu-load">—</b></div><div><span>Temperatura</span><b id="m-gpu-temp">—</b></div>
          <div><span>Punto caliente</span><b id="m-gpu-hot">—</b></div><div><span>Consumo</span><b id="m-gpu-power">—</b></div>
          <div><span>Ventilador</span><b id="m-gpu-fan">—</b></div><div><span>Reloj núcleo</span><b id="m-gpu-clock">—</b></div>
          <div><span>Reloj memoria</span><b id="m-gpu-mclock">—</b></div><div><span>VRAM</span><b id="m-gpu-vram">—</b></div>
        </div>
        <div class="bar" style="margin-top:10px" title="VRAM en uso"><i id="m-gpu-vrambar" style="background:${COL.violet}"></i></div>
      </div>
      <div class="span5 stack">
        <div class="card">
          <div class="card-head">${icon('ram')}<div><h3>Memoria</h3></div><div class="right"><b id="m-ram-txt">—</b></div></div>
          <canvas class="spark" id="m-ram-chart" style="margin-top:0;height:46px"></canvas>
        </div>
        <div class="card">
          <div class="card-head">${icon('net')}<div><h3>Red</h3></div>
            <div class="right legend"><span><i style="background:${COL.accent}"></i>↓ <b id="m-net-down">—</b></span><span><i style="background:${COL.blue}"></i>↑ <b id="m-net-up">—</b></span></div></div>
          <canvas class="spark" id="m-net-chart" style="margin-top:0;height:46px"></canvas>
        </div>
        <div class="card">
          <div class="card-head">${icon('disk')}<div><h3>Almacenamiento</h3></div></div>
          <div class="rows" id="m-drives"></div>
        </div>
      </div>
    </div>`;
  },
  show() {
    const hw = (state.init && state.init.hardware) || {};
    setText('#m-cpu-name', hw.cpu || '—', this.root); setText('#m-gpu-name', hw.gpu || '—', this.root);
  },
  tick(s) {
    const root = this.root, c = s.cpu || {}, g = s.gpu || {}, r = s.ram || {}, n = s.net || {};
    drawChart($('#m-cpu-chart', root), [{ data: hist.cpuLoad, color: COL.blue, max: 100 }, { data: hist.cpuTemp, color: COL.accent, max: 100, fill: false }], { grid: true });
    drawChart($('#m-gpu-chart', root), [{ data: hist.gpuLoad, color: COL.blue, max: 100 }, { data: hist.gpuPower, color: COL.violet, max: 130, fill: false }, { data: hist.gpuTemp, color: COL.accent, max: 100, fill: false }], { grid: true });
    drawChart($('#m-ram-chart', root), [{ data: hist.ram, color: COL.violet, max: 100 }]);
    drawChart($('#m-net-chart', root), [{ data: hist.down, color: COL.accent }, { data: hist.up, color: COL.blue, fill: false }], { sharedMax: true });
    setText('#m-cpu-load', fmt(c.load, 0, ' %'), root);
    const ct = $('#m-cpu-temp', root); setText(ct, fmt(c.temp, 0, ' °C')); ct.style.color = tempHex(c.temp);
    setText('#m-cpu-clock', fmtClock(c.clockMhz), root); setText('#m-cpu-power', fmt(c.power, 0, ' W'), root);
    setText('#m-gpu-load', fmt(g.load, 0, ' %'), root);
    const gt = $('#m-gpu-temp', root); setText(gt, fmt(g.temp, 0, ' °C')); gt.style.color = tempHex(g.temp);
    const gh = $('#m-gpu-hot', root); setText(gh, fmt(g.hotspot, 0, ' °C')); gh.style.color = tempHex(isNum(g.hotspot) ? g.hotspot - 8 : null);
    setText('#m-gpu-power', fmt(g.power, 0, ' W'), root);
    setText('#m-gpu-fan', isNum(g.fanRpm) ? `${fmt(g.fanRpm)} rpm${isNum(g.fanPct) ? ' · ' + fmt(g.fanPct) + ' %' : ''}` : fmt(g.fanPct, 0, ' %'), root);
    setText('#m-gpu-clock', fmtClock(g.clockMhz), root); setText('#m-gpu-mclock', fmtClock(g.memClockMhz), root);
    setText('#m-gpu-vram', isNum(g.vramUsedMb) ? `${fmtGb(g.vramUsedMb)} / ${fmtGb(g.vramTotalMb)} GB` : '—', root);
    $('#m-gpu-vrambar', root).style.width = (isNum(g.vramUsedMb) && g.vramTotalMb ? clamp(g.vramUsedMb / g.vramTotalMb * 100, 0, 100) : 0) + '%';
    setText('#m-ram-txt', isNum(r.usedGb) ? `${fmt(r.usedGb, 1)} / ${fmt(r.totalGb, 1)} GB · ${fmt(r.load)} %` : '—', root);
    setText('#m-net-down', fmtRate(n.downKbps), root); setText('#m-net-up', fmtRate(n.upKbps), root);

    syncList($('#m-cores', root), c.cores, k => k.name,
      k => h(`<div class="core"><div class="top"><span>${esc(k.name)}</span><b data-l></b></div><div class="bar"><i></i></div><small data-c></small></div>`),
      (el, k) => {
        setText($('[data-l]', el), fmt(k.load, 0, ' %'));
        const b = $('.bar i', el); b.style.width = (isNum(k.load) ? k.load : 0) + '%'; b.style.background = loadHex(k.load);
        setText($('[data-c]', el), isNum(k.clockMhz) ? fmt(k.clockMhz, 0, ' MHz') : '');
      }, '<div class="mini-empty">Sin datos por núcleo</div>');

    this.renderTemps(s.temps || []);

    syncList($('#m-drives', root), s.drives, d => d.name,
      d => h(`<div class="row"><span class="name">${esc(d.name)}</span><span class="v"><span data-t></span><small data-u></small></span><div class="bar"><i></i></div></div>`),
      (el, d) => {
        const t = $('[data-t]', el); setText(t, fmt(d.temp, 0, ' °C')); t.style.color = tempHex(d.temp);
        setText($('[data-u]', el), isNum(d.usedPct) ? fmt(d.usedPct, 0, ' %') : '');
        const b = $('.bar i', el); b.style.width = (isNum(d.usedPct) ? d.usedPct : 0) + '%'; b.style.background = d.usedPct > 90 ? COL.hot : d.usedPct > 75 ? COL.warn : COL.blue;
      }, '<div class="mini-empty">Sin datos de discos</div>');
  },
  renderTemps(temps) {
    const host = $('#m-temps', this.root);
    if (!temps.length) {
      if (host._sig !== '∅') { host._sig = '∅'; host.innerHTML = `<div class="empty" style="padding:26px 10px">${icon('shield')}<h3>${needAdminMsg()}</h3><p>Los sensores de temperatura de la placa y la CPU solo se pueden leer como administrador.</p></div>`; }
      return;
    }
    const sig = temps.map(t => t.id).join('|');
    if (host._sig !== sig) {
      host._sig = sig;
      const groups = new Map();
      for (const t of temps) { const g = t.group || 'Otros'; if (!groups.has(g)) groups.set(g, []); groups.get(g).push(t); }
      host.innerHTML = [...groups].map(([g, list]) => `<div class="tgroup"><h4>${esc(g)}</h4>${list.map(t => `<div class="trow" data-id="${esc(t.id)}"><span title="${esc(t.name)}">${esc(t.name)}</span><div class="bar"><i></i></div><b></b></div>`).join('')}</div>`).join('');
      host._map = new Map($$('.trow', host).map(el => [el.dataset.id, el]));
    }
    for (const t of temps) {
      const el = host._map.get(String(t.id)); if (!el) continue;
      const b = el.lastElementChild; setText(b, fmt(t.value, 0, ' °C')); b.style.color = tempHex(t.value);
      const bar = $('.bar i', el); bar.style.width = (isNum(t.value) ? clamp(t.value, 0, 100) : 0) + '%'; bar.style.background = tempHex(t.value);
    }
  },
};

/* ══════════════════════ VENTILADORES ══════════════════════ */
function curveAt(points, t) {
  if (t <= points[0][0]) return points[0][1];
  for (let i = 1; i < points.length; i++) {
    if (t <= points[i][0]) { const [t0, p0] = points[i - 1], [t1, p1] = points[i]; return p0 + (p1 - p0) * (t - t0) / (t1 - t0); }
  }
  return points[points.length - 1][1];
}

function curveEditor(host, points, { onChange, onCommit }) {
  const W = 360, H = 172, L = 30, R = 12, T = 12, B = 22;
  const tx = t => L + (t - 20) / 80 * (W - L - R), ty = p => T + (100 - p) / 100 * (H - T - B);
  let grid = '';
  for (const t of [20, 40, 60, 80, 100]) grid += `<line class="gl" x1="${tx(t)}" y1="${T}" x2="${tx(t)}" y2="${H - B}"/><text x="${tx(t)}" y="${H - 7}" text-anchor="middle">${t}°</text>`;
  for (const p of [0, 25, 50, 75, 100]) grid += `<line class="gl" x1="${L}" y1="${ty(p)}" x2="${W - R}" y2="${ty(p)}"/><text x="${L - 5}" y="${ty(p) + 3}" text-anchor="end">${p}</text>`;
  host.innerHTML = `<svg class="curve" viewBox="0 0 ${W} ${H}">${grid}<path class="c-area"/><path class="c-line"/><line class="c-now" y1="${T}" y2="${H - B}" visibility="hidden"/><circle class="c-nowdot" r="4" visibility="hidden"/><g class="c-pts"></g></svg>`;
  const svg = host.firstElementChild, line = $('.c-line', svg), area = $('.c-area', svg), ptsG = $('.c-pts', svg), now = $('.c-now', svg), nowDot = $('.c-nowdot', svg);
  const circles = points.map((_, i) => {
    const c = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
    c.setAttribute('class', 'pt'); c.setAttribute('r', '6'); c.dataset.i = i; ptsG.appendChild(c); return c;
  });
  function draw() {
    const d = `M${tx(20)},${ty(points[0][1])} ` + points.map(p => `L${tx(p[0])},${ty(p[1])}`).join(' ') + ` L${tx(100)},${ty(points[points.length - 1][1])}`;
    line.setAttribute('d', d);
    area.setAttribute('d', d + ` L${tx(100)},${ty(0)} L${tx(20)},${ty(0)} Z`);
    points.forEach((p, i) => { circles[i].setAttribute('cx', tx(p[0])); circles[i].setAttribute('cy', ty(p[1])); });
  }
  let drag = -1;
  svg.addEventListener('pointerdown', e => {
    const c = e.target.closest('.pt'); if (!c) return;
    drag = +c.dataset.i; c.classList.add('drag'); svg.setPointerCapture(e.pointerId); e.preventDefault();
  });
  svg.addEventListener('pointermove', e => {
    if (drag < 0) return;
    const r = svg.getBoundingClientRect();
    const x = (e.clientX - r.left) / r.width * W, y = (e.clientY - r.top) / r.height * H;
    const t = 20 + (x - L) / (W - L - R) * 80, p = 100 - (y - T) / (H - T - B) * 100;
    const lo = drag > 0 ? points[drag - 1][0] + 2 : 20, hi = drag < points.length - 1 ? points[drag + 1][0] - 2 : 100;
    // La curva nunca baja al subir la temperatura: cada punto queda entre sus vecinos.
    const pLo = drag > 0 ? points[drag - 1][1] : 0, pHi = drag < points.length - 1 ? points[drag + 1][1] : 100;
    points[drag] = [Math.round(clamp(t, lo, hi)), Math.round(clamp(p, pLo, pHi))];
    draw(); if (onChange) onChange(points);
  });
  const end = e => {
    if (drag < 0) return;
    circles[drag].classList.remove('drag'); drag = -1;
    try { svg.releasePointerCapture(e.pointerId); } catch { /* ya liberado */ }
    if (onCommit) onCommit(points);
  };
  svg.addEventListener('pointerup', end); svg.addEventListener('pointercancel', end);
  draw();
  return {
    setNow(temp) {
      const vis = isNum(temp) ? 'visible' : 'hidden';
      now.setAttribute('visibility', vis); nowDot.setAttribute('visibility', vis);
      if (!isNum(temp)) return;
      const x = tx(clamp(temp, 20, 100));
      now.setAttribute('x1', x); now.setAttribute('x2', x);
      nowDot.setAttribute('cx', x); nowDot.setAttribute('cy', ty(curveAt(points, temp)));
    },
  };
}

const isGpuFan = f => String(f.id).startsWith('/gpu-') || /gpu|gr[aá]fica/i.test(f.name || '');

const Fans = {
  cfg: {}, cards: new Map(), loaded: false, sig: null,
  mount(root) {
    this.root = root;
    root.innerHTML = `
      <div class="page-actions">
        <p class="hint">Elige cómo se controla cada ventilador: en automático lo decide la placa base, en manual fijas tú la velocidad y con una curva se ajusta solo según la temperatura.</p>
        <button class="btn" id="fans-auto">${icon('refresh')}Todo en automático</button>
      </div>
      <div id="fans-empty"></div>
      <div class="grid" id="fans-grid"></div>`;
    $('#fans-auto', root).addEventListener('click', () => this.allAuto());
  },
  async show() {
    const r = await attempt(() => api.call('fans.get'));
    if (r.ok && Array.isArray(r.result)) for (const c of r.result) if (c && c.id != null) this.cfg[c.id] = this.norm(c);
    this.loaded = true; this.sig = null;
    if (state.sensors) this.tick(state.sensors);
  },
  norm(c, f) {
    const temps = (state.sensors && state.sensors.temps) || [];
    const isGpu = f ? isGpuFan(f) : false;
    const src = temps.find(t => t.group === (isGpu ? 'GPU' : 'CPU')) || temps[0];
    const pts = c && c.curve && Array.isArray(c.curve.points) && c.curve.points.length >= 2 ? c.curve.points.map(p => [+p[0], +p[1]]) : [[30, 25], [50, 40], [65, 60], [75, 80], [85, 100]];
    return {
      id: c ? c.id : f.id,
      mode: (c && c.mode) || (f && f.mode) || 'auto',
      pct: c && isNum(c.pct) ? Math.round(c.pct) : f && isNum(f.pct) ? Math.round(f.pct) : 50,
      curve: { sourceId: (c && c.curve && c.curve.sourceId) ?? (src ? src.id : null), points: pts },
    };
  },
  tick(s) {
    if (!this.loaded) return;
    const fans = s.fans || [], grid = $('#fans-grid', this.root), empty = $('#fans-empty', this.root);
    const sig = fans.map(f => f.id + ':' + (f.controllable ? 1 : 0)).join('|');
    if (sig !== this.sig) {
      this.sig = sig; this.cards.clear(); grid.innerHTML = '';
      if (!fans.length) {
        const admin = state.init && state.init.isAdmin;
        empty.innerHTML = `<div class="card"><div class="empty">${icon(admin ? 'fan' : 'shield')}<h3>${admin ? 'No se han detectado ventiladores' : 'Necesita permisos de administrador'}</h3>
          <p>${admin ? 'La placa base no ha devuelto ningún sensor de ventilador. Comprueba que el driver PawnIO está instalado.' : 'Para leer y controlar los ventiladores de la placa base hay que abrir la app como administrador.'}</p>
          ${admin ? '' : `<button class="btn primary" id="fans-admin">${icon('shield')}Abrir como administrador</button>`}</div></div>`;
        const b = $('#fans-admin', empty); if (b) b.addEventListener('click', relaunchAdmin);
      } else {
        empty.innerHTML = state.init && !state.init.isAdmin ? `<div class="note warn" style="margin-bottom:12px">${icon('shield')}<span>Sin permisos de administrador solo aparecen los ventiladores de la tarjeta gráfica. Abre la app como administrador para ver y controlar los de la placa base.</span></div>` : '';
        for (const f of fans) this.build(f, grid, s);
      }
    }
    const temps = s.temps || [];
    for (const f of fans) {
      const card = this.cards.get(f.id); if (!card) continue;
      setText(card.rpm, fmt(f.rpm));
      setText(card.pct, isNum(f.pct) ? fmt(f.pct, 0, ' %') : '');
      card.ico.style.setProperty('--spin', isNum(f.rpm) && f.rpm > 60 ? clamp(900 / f.rpm, .18, 4).toFixed(2) + 's' : '0s');
      if (card.curve) { const cfg = this.cfg[f.id]; const t = temps.find(x => x.id === cfg.curve.sourceId); card.curve.setNow(t ? t.value : null); }
    }
  },
  build(f, grid, s) {
    if (!this.cfg[f.id]) this.cfg[f.id] = this.norm(null, f);
    const cfg = this.cfg[f.id];
    const el = h(`<div class="card span6">
      <div class="fan-top">
        <div class="fan-ico">${icon('fan')}</div>
        <div class="fan-title"><h3>${esc(f.name)}</h3><span class="sub">${f.controllable ? 'Controlable' : 'Solo lectura'}</span></div>
        <div class="fan-read"><b class="rpm">—</b><span>rpm</span><em data-pct></em></div>
      </div>
      ${f.controllable ? `<div class="seg" data-seg><button data-v="auto">Automático</button><button data-v="manual">Manual</button><button data-v="curve">Curva</button></div><div class="fan-body"></div><div class="fan-warn" hidden></div>`
        : `<div class="hint">Este ventilador no admite control por software; solo se muestra su velocidad.</div>`}
    </div>`);
    grid.appendChild(el);
    const card = { el, rpm: $('.rpm', el), pct: $('[data-pct]', el), ico: $('.fan-ico .ico', el), body: $('.fan-body', el), warn: $('.fan-warn', el), curve: null, fan: f };
    this.cards.set(f.id, card);
    if (!f.controllable) return;
    $('[data-seg]', el).addEventListener('click', e => {
      const b = e.target.closest('button'); if (!b || cfg.mode === b.dataset.v) return;
      cfg.mode = b.dataset.v; this.renderBody(f.id);
      this.save(f.id, `${f.name}: ${{ auto: 'modo automático', manual: 'velocidad fija al ' + cfg.pct + ' %', curve: 'curva activada' }[cfg.mode]}`);
    });
    this.renderBody(f.id);
  },
  renderBody(id) {
    const card = this.cards.get(id), cfg = this.cfg[id]; if (!card || !card.body) return;
    $$('[data-seg] button', card.el).forEach(b => b.classList.toggle('active', b.dataset.v === cfg.mode));
    card.curve = null;
    const saveSoon = debounce(() => this.save(id), 220);
    if (cfg.mode === 'auto') {
      card.body.innerHTML = `<div class="auto-txt">${isGpuFan(card.fan) ? 'La propia gráfica regula su ventilador (en reposo se para).' : 'La placa base regula la velocidad con su propio perfil (el de la BIOS).'}</div>`;
    } else if (cfg.mode === 'manual') {
      card.body.innerHTML = `<div class="fan-manual"><input type="range" min="0" max="100" step="1" value="${cfg.pct}"><b>${cfg.pct} %</b></div>`;
      const r = $('input', card.body), b = $('b', card.body); paintRange(r);
      r.addEventListener('input', () => { cfg.pct = +r.value; b.textContent = cfg.pct + ' %'; this.checkWarn(id); saveSoon(); });
    } else {
      const temps = (state.sensors && state.sensors.temps) || [];
      const opts = temps.map(t => `<option value="${esc(t.id)}" ${t.id === cfg.curve.sourceId ? 'selected' : ''}>${esc((t.group ? t.group + ' · ' : '') + t.name)}</option>`).join('');
      card.body.innerHTML = `<div class="fan-src"><span>Según</span><select class="field">${opts || '<option value="">Sin sensores de temperatura</option>'}</select></div><div data-curve></div>`;
      const sel = $('select', card.body);
      if (!cfg.curve.sourceId && temps[0]) cfg.curve.sourceId = temps[0].id;
      sel.addEventListener('change', () => { cfg.curve.sourceId = sel.value; this.save(id); });
      card.curve = curveEditor($('[data-curve]', card.body), cfg.curve.points, { onChange: () => this.checkWarn(id), onCommit: () => this.save(id) });
    }
    this.checkWarn(id);
  },
  checkWarn(id) {
    const card = this.cards.get(id), cfg = this.cfg[id]; if (!card || !card.warn) return;
    const isCpu = /cpu|procesador/i.test(card.fan.name) && !isGpuFan(card.fan);
    const low = cfg.mode === 'manual' ? cfg.pct < 20 : cfg.mode === 'curve' ? Math.min(...cfg.curve.points.map(p => p[1])) < 20 : false;
    card.warn.hidden = !(isCpu && low);
    if (isCpu && low) card.warn.innerHTML = `<div class="note warn">${icon('warn')}<span>Por debajo del 20 % el ventilador de la CPU puede llegar a pararse y el procesador se calentará. Vigila la temperatura.</span></div>`;
  },
  async save(id, okMsg) {
    const cfg = this.cfg[id];
    const args = { id, mode: cfg.mode };
    if (cfg.mode === 'manual') args.pct = cfg.pct;
    if (cfg.mode === 'curve') args.curve = { sourceId: cfg.curve.sourceId, points: cfg.curve.points.map(p => [p[0], p[1]]) };
    await attempt(() => api.call('fans.set', args), okMsg);
  },
  async allAuto() {
    const ids = [...this.cards.values()].filter(c => c.fan.controllable).map(c => c.fan.id);
    if (!ids.length) return toast('No hay ventiladores controlables', 'info');
    let ok = 0;
    for (const id of ids) {
      this.cfg[id].mode = 'auto'; this.renderBody(id);
      try { await api.call('fans.set', { id, mode: 'auto' }); ok++; } catch (e) { toast(e.message, 'err', 6000); }
    }
    if (ok) toast(ok === ids.length ? 'Todos los ventiladores en automático' : `${ok} de ${ids.length} ventiladores en automático`);
  },
};

/* ══════════════════════ RGB (ASUS Aura) ══════════════════════ */
const Rgb = {
  st: { effect: 'static', color: '#00d6aa', speed: 3, brightness: 100 },
  sel: new Set(), devices: [], raf: 0,
  FX: [
    { id: 'static', name: 'Estático', sw: 'var(--c)' },
    { id: 'breath', name: 'Respiración', sw: 'radial-gradient(circle, var(--c), transparent 75%)' },
    { id: 'rainbow', name: 'Arcoíris', sw: 'linear-gradient(90deg,#ff4d4d,#ffd24d,#4dff88,#4dc3ff,#b44dff)' },
    { id: 'cycle', name: 'Ciclo de color', sw: 'conic-gradient(#ff4d4d,#ffd24d,#4dff88,#4dc3ff,#b44dff,#ff4d4d)' },
    { id: 'temp', name: 'Según temperatura', sw: 'linear-gradient(90deg,#00d6aa,#ffb020,#ff5a4f)' },
    { id: 'off', name: 'Apagado', sw: '#1c2434' },
  ],
  SW: ['#ff3b30', '#ff9500', '#ffd60a', '#34c759', '#00d6aa', '#0a84ff', '#bf5af2', '#ffffff'],
  mount(root) {
    this.root = root;
    try { Object.assign(this.st, JSON.parse(localStorage.getItem('cdm.rgb') || '{}')); } catch { /* sin almacenamiento */ }
    if (!validHex(this.st.color)) this.st.color = '#00d6aa';
    root.innerHTML = `
    <div id="rgb-empty"></div>
    <div class="grid" id="rgb-main" hidden>
      <div class="card span5">
        <div class="card-head">${icon('chip')}<div><h3>Dispositivos</h3><span class="sub">Detectados en la placa base (controlador Aura)</span></div>
          <div class="right"><button class="icon-btn" id="rgb-refresh" title="Volver a buscar">${icon('refresh')}</button></div></div>
        <div class="dev-list" id="rgb-devs"></div>
        <hr class="sep">
        <div class="note">${icon('info')}<span>Los colores se envían en directo a la placa y no se guardan en ella: al reiniciar el PC, o si aplicas un efecto desde Armoury Crate, vuelve la iluminación que tuvieras. Si las luces de tus ventiladores no cambian, es que van conectadas al controlador de la caja y no a la placa.</span></div>
        <div class="card-foot"><button class="btn ghost sm" id="rgb-release">Dejar de controlar las luces</button></div>
      </div>
      <div class="card span7">
        <div class="card-head">${icon('rgb')}<div><h3>Efecto</h3><span class="sub" id="rgb-target">—</span></div></div>
        <div class="fx-grid" id="rgb-fx"></div>
        <div class="rgb-preview" id="rgb-preview">${'<i></i>'.repeat(22)}</div>
        <div class="rgb-color" id="rgb-color-row">
          <div id="rgb-wheel"></div>
          <div class="rgb-color-side"><b>Color</b><span class="sub">Gira la rueda para el tono; hacia el centro es más pálido y la barra lo oscurece o aclara.</span>
            <div class="swatches" id="rgb-sw"></div>
            <div class="color-row"><span class="rgb-cur" id="rgb-cur"></span><input class="field hex" id="rgb-hex" maxlength="7" spellcheck="false"></div></div>
        </div>
        <div id="rgb-sliders"></div>
        <div class="card-foot"><span class="hint" id="rgb-hint"></span><button class="btn primary" id="rgb-apply">${icon('check')}Aplicar</button></div>
      </div>
    </div>`;
    const st = this.st;
    $('#rgb-fx', root).innerHTML = this.FX.map(f => `<button class="fx" data-fx="${f.id}"><i style="background:${f.sw}"></i><b>${f.name}</b></button>`).join('');
    $('#rgb-sw', root).innerHTML = this.SW.map(c => `<button class="swatch" data-c="${c}" style="background:${c}" title="${c}"></button>`).join('');
    this.bri = sliderRow({ title: 'Brillo', min: 0, max: 100, step: 5, unit: '%', value: st.brightness });
    this.spd = sliderRow({ title: 'Velocidad', desc: 'De los efectos animados', min: 1, max: 5, step: 1, unit: '', value: st.speed });
    $('#rgb-sliders', root).append(this.bri.el, this.spd.el);
    this.bri.onInput(v => { st.brightness = v; this.sync(); });
    this.spd.onInput(v => { st.speed = v; this.sync(); });
    $('#rgb-fx', root).addEventListener('click', e => { const b = e.target.closest('[data-fx]'); if (b) { st.effect = b.dataset.fx; this.sync(); } });
    $('#rgb-sw', root).addEventListener('click', e => { const b = e.target.closest('[data-c]'); if (b) { st.color = b.dataset.c; this.sync(); } });
    this.wheel = colorWheel(c => { st.color = c; this.sync(); });
    $('#rgb-wheel', root).append(this.wheel.el);
    $('#rgb-hex', root).addEventListener('change', e => { let v = e.target.value.trim(); if (v && v[0] !== '#') v = '#' + v; if (validHex(v)) { st.color = v.toLowerCase(); } this.sync(); });
    $('#rgb-devs', root).addEventListener('click', e => {
      const d = e.target.closest('.dev'); if (!d) return;
      const id = d.dataset.id; this.sel.has(id) ? this.sel.delete(id) : this.sel.add(id); this.renderDevs();
    });
    $('#rgb-refresh', root).addEventListener('click', () => this.load(true));
    $('#rgb-apply', root).addEventListener('click', () => this.apply());
    $('#rgb-release', root).addEventListener('click', () => attempt(() => api.call('aura.release'), 'La app ha dejado de controlar las luces'));
    this.sync();
  },
  show() { this.load(false); this.loop(); },
  hide() { cancelAnimationFrame(this.raf); this.raf = 0; },
  async load(manual) {
    const r = await attempt(() => api.call('aura.status'));
    const empty = $('#rgb-empty', this.root), main = $('#rgb-main', this.root);
    const s = r.ok ? (r.result || {}) : { available: false, error: r.error && r.error.message };
    if (!s.available) {
      main.hidden = true;
      empty.innerHTML = `<div class="card"><div class="empty">${icon('rgb')}<h3>El controlador Aura no está disponible</h3>
        <p>${esc(s.error || 'No se ha podido acceder al controlador de luces (Aura) de la placa base.')}</p>
        <button class="btn" id="rgb-retry">${icon('refresh')}Reintentar</button></div></div>`;
      $('#rgb-retry', empty).addEventListener('click', () => this.load(true));
      return;
    }
    empty.innerHTML = ''; main.hidden = false;
    const first = !this.devices.length;
    this.devices = (s.devices || []).map(d => ({ ...d, id: String(d.id) }));
    if (first) this.sel = new Set(this.devices.map(d => d.id));
    else this.sel = new Set([...this.sel].filter(id => this.devices.some(d => d.id === id)));
    this.renderDevs();
    if (manual) toast(`Dispositivos RGB detectados: ${this.devices.length}`);
  },
  renderDevs() {
    const host = $('#rgb-devs', this.root);
    host.innerHTML = this.devices.length ? this.devices.map(d => `<div class="dev ${this.sel.has(d.id) ? 'on' : ''}" data-id="${esc(d.id)}">
      <input type="checkbox" class="cb" ${this.sel.has(d.id) ? 'checked' : ''} tabindex="-1" style="pointer-events:none">
      <div class="meta"><b>${esc(d.name)}</b><span>${esc(d.type || 'Dispositivo')}${isNum(d.leds) ? ' · ' + d.leds + ' LED' : ''}</span></div></div>`).join('')
      : '<div class="mini-empty">La placa no ha informado de ningún conector con luces.</div>';
    const n = this.sel.size, t = this.devices.length;
    setText('#rgb-target', n === t ? 'Se aplica a todos los dispositivos' : `Se aplica a ${n} de ${t} dispositivos`, this.root);
    $('#rgb-apply', this.root).disabled = n === 0;
  },
  sync() {
    const st = this.st, root = this.root, fx = st.effect;
    $$('.fx', root).forEach(b => b.classList.toggle('active', b.dataset.fx === fx));
    $('#rgb-fx', root).style.setProperty('--c', st.color);
    $$('.swatch', root).forEach(b => b.classList.toggle('active', b.dataset.c === st.color));
    this.wheel.set(st.color); $('#rgb-cur', root).style.background = st.color;
    if (document.activeElement !== $('#rgb-hex', root)) $('#rgb-hex', root).value = st.color.toUpperCase();
    const usesColor = fx === 'static' || fx === 'breath';
    $('#rgb-color-row', root).classList.toggle('off', !usesColor); this.wheel.setDisabled(!usesColor);
    this.bri.setDisabled(fx === 'off');
    this.spd.setDisabled(!(fx === 'breath' || fx === 'rainbow' || fx === 'cycle'));
    setText('#rgb-hint', fx === 'temp' ? 'Verde en frío, ámbar templado y rojo cuando la CPU o la GPU se calientan.' : fx === 'off' ? 'Apaga todas las luces seleccionadas.' : '', root);
    try { localStorage.setItem('cdm.rgb', JSON.stringify(st)); } catch { /* sin almacenamiento */ }
  },
  async apply() {
    const st = this.st, all = this.sel.size === this.devices.length;
    const btn = $('#rgb-apply', this.root); btn.classList.add('busy');
    await attempt(() => api.call('aura.apply', { deviceIds: all ? null : [...this.sel], effect: st.effect, color: st.color, speed: st.speed, brightness: st.brightness }), 'Iluminación aplicada');
    btn.classList.remove('busy');
  },
  loop() {
    cancelAnimationFrame(this.raf);
    const leds = $$('#rgb-preview i', this.root), n = leds.length, st = this.st;
    const rgbOf = hex => { const v = parseInt(hex.slice(1), 16); return [v >> 16, (v >> 8) & 255, v & 255]; };
    const frame = now => {
      if (state.page !== 'rgb') return;
      const t = now / 1000, sp = st.speed, br = st.brightness / 100, [r, g, b] = rgbOf(st.color);
      for (let i = 0; i < n; i++) {
        let col;
        if (st.effect === 'off') col = '#161b26';
        else if (st.effect === 'static') col = `rgb(${r * br},${g * br},${b * br})`;
        else if (st.effect === 'breath') { const k = br * (.12 + .88 * (.5 + .5 * Math.sin(t * sp * 1.1))); col = `rgb(${r * k},${g * k},${b * k})`; }
        else if (st.effect === 'rainbow') col = `hsl(${(i / n * 360 - t * 50 * sp) % 360} 95% ${12 + 43 * br}%)`;
        else if (st.effect === 'cycle') col = `hsl(${(t * 30 * sp) % 360} 95% ${12 + 43 * br}%)`;
        else { const s = state.sensors || {}; const tt = Math.max(isNum(s.cpu && s.cpu.temp) ? s.cpu.temp : 0, isNum(s.gpu && s.gpu.temp) ? s.gpu.temp : 0) || 45; col = `hsl(${clamp(165 - (tt - 40) * 4.1, 0, 165)} 90% ${12 + 40 * br}%)`; }
        leds[i].style.background = col;
        leds[i].style.boxShadow = st.effect === 'off' ? 'none' : `0 0 10px ${col}`;
      }
      this.raf = requestAnimationFrame(frame);
    };
    this.raf = requestAnimationFrame(frame);
  },
};

/* ══════════════════════ PERIFÉRICOS ══════════════════════ */
const Perif = {
  items: [], sel: null, loaded: false, names: {},
  /** El controlador de cada periférico informa del modelo exacto cuando lo identifica. */
  setName(driver, name) {
    if (!name || this.names[driver] === name) return;
    this.names[driver] = name;
    const it = this.items.find(i => i.driver === driver);
    if (it) { const b = $(`.pf-dev[data-k="${it.key}"] .pf-txt b`, this.root); if (b) b.textContent = name; }
  },
  KIND: { keyboard: ['keyboard', 'Teclado'], mouse: ['mouse', 'Ratón'], headset: ['headset', 'Auriculares'], gamepad: ['gamepad', 'Mando'], audio: ['volume', 'Volumen y dispositivos'] },
  mount(root) {
    this.root = root;
    root.innerHTML = `
      <div class="pf-strip" id="pf-strip"><div class="skeleton" style="height:64px"></div></div>
      <div id="pf-kb" hidden></div>
      <div id="pf-mouse" hidden></div>
      <div id="pf-sound" hidden></div>
      <div id="pf-info" hidden></div>`;
    Kb.mount($('#pf-kb', root));
    Mouse.mount($('#pf-mouse', root));
    Sound.mount($('#pf-sound', root));
    $('#pf-strip', root).addEventListener('click', e => {
      if (e.target.closest('#pf-rescan')) return this.scan(true);
      const b = e.target.closest('[data-k]'); if (b) this.select(b.dataset.k);
    });
  },
  /** ¿Está a la vista el panel de este controlador ('kb' o 'mouse')? */
  is(panel) {
    if (state.page !== 'perif') return false;
    const it = this.items.find(i => i.key === this.sel);
    return panel === 'kb' ? (!it || it.driver === 'ajazz') : !!it && it.driver === panel;
  },
  async show() { await this.scan(false); },
  hide() { this.hidePanel(this.current()); },
  hidePanel(it) { if (!it) return; if (it.driver === 'ajazz') Kb.hide(); if (it.driver === 'vxe') Mouse.hide(); if (it.driver === 'audio') Sound.hide(); },
  current() { return this.items.find(i => i.key === this.sel); },
  async scan(manual) {
    const r = await attempt(() => api.call('periph.list'));
    this.items = r.ok && r.result && Array.isArray(r.result.items) ? r.result.items : [];
    // Los auriculares se controlan con el sonido de Windows; si no hay ninguno, una tarjeta general de sonido.
    for (const i of this.items) if (i.kind === 'headset' && !i.driver) i.driver = 'audio';
    if (!this.items.some(i => i.driver === 'audio')) this.items.push({ key: 'audio', kind: 'audio', name: 'Sonido', driver: 'audio', wireless: false });
    this.loaded = true;
    if (!this.items.some(i => i.key === this.sel)) {
      const pick = this.items.find(i => i.driver) || this.items[0];
      this.sel = pick ? pick.key : null;
    }
    this.renderStrip();
    this.select(this.sel, true);
    // Nombre exacto del ratón aunque no esté seleccionado
    if (this.items.some(i => i.driver === 'vxe') && !this.names.vxe && this.sel !== this.items.find(i => i.driver === 'vxe').key)
      api.call('mouse.status').then(st => st && st.model && this.setName('vxe', st.model), () => { });
    if (manual) toast(this.items.length ? `Periféricos detectados: ${this.items.length}` : 'No se ha encontrado ningún periférico', 'info');
  },
  renderStrip() {
    const strip = $('#pf-strip', this.root);
    const cards = this.items.map(i => {
      const [ic, label] = this.KIND[i.kind] || ['chip', 'Dispositivo'];
      const tag = i.driver ? '<span class="pill">Configurable</span>' : '<span class="pill off">Solo detección</span>';
      return `<button class="pf-dev${i.key === this.sel ? ' active' : ''}" data-k="${esc(i.key)}">
        <span class="pf-ico">${icon(ic)}</span>
        <span class="pf-txt"><b>${esc(this.names[i.driver] || i.name)}</b><small>${label}${i.wireless ? ' · inalámbrico' : ''}</small>${tag}</span></button>`;
    }).join('');
    strip.innerHTML = (cards || '<div class="pf-none">No se ha detectado ningún teclado, ratón ni mando USB.</div>') +
      `<button class="icon-btn pf-rescan" id="pf-rescan" title="Volver a buscar periféricos">${icon('refresh')}</button>`;
  },
  select(key, force) {
    const prev = this.current();
    if (!force && key === this.sel) return;
    if (prev && prev.key !== key) this.hidePanel(prev);
    this.sel = key;
    $$('.pf-dev', this.root).forEach(b => b.classList.toggle('active', b.dataset.k === key));
    const it = this.current();
    const kb = !it || it.driver === 'ajazz', mouse = !!it && it.driver === 'vxe', sound = !!it && it.driver === 'audio';
    $('#pf-kb', this.root).hidden = !kb;
    $('#pf-mouse', this.root).hidden = !mouse;
    $('#pf-sound', this.root).hidden = !sound;
    const info = $('#pf-info', this.root);
    info.hidden = kb || mouse || sound;
    if (state.page !== 'perif') return;
    if (kb) Kb.show();
    else if (mouse) Mouse.show(it);
    else if (sound) Sound.show(it);
    else {
      const [ic, label] = this.KIND[it.kind] || ['chip', 'Dispositivo'];
      info.innerHTML = `<div class="card"><div class="empty">${icon(ic)}<h3>${esc(it.name)}</h3>
        <p>${label} conectado (USB ${esc(it.key)}). Centro de Mando lo detecta, pero todavía no sabe configurar este modelo.</p></div></div>`;
    }
  },
};

/** Guías de BIOS adaptadas al procesador y la memoria detectados. */
function ocGuides(cpuName, boardName, speed) {
  const cpu = String(cpuName || ''), board = String(boardName || '');
  const amd = /amd|ryzen/i.test(cpu), intel = /intel|core/i.test(cpu) && !amd;
  const gen = (cpu.match(/Ryzen\s+\d\s+(\d)\d{3}/i) || [])[1];               // Ryzen 5 5500 → 5
  const am5 = amd && gen && +gen >= 7;
  const intelK = intel && /\d{4,5}K/i.test(cpu);
  const vendor = /asus/i.test(board) ? 'asus' : /msi|micro-star/i.test(board) ? 'msi' : /gigabyte|aorus/i.test(board) ? 'gigabyte' : /asrock/i.test(board) ? 'asrock' : '';
  const advKey = { asus: 'F7', msi: 'F7', gigabyte: 'F2', asrock: 'F6' }[vendor];
  const saveKey = 'F10';
  const profile = amd ? (vendor === 'asus' ? 'DOCP / EXPO' : 'EXPO / XMP') : 'XMP';
  const mhz = +speed || 0;

  let cpuTitle, cpuBody;
  if (amd) {
    cpuTitle = 'Cómo hacer overclock de verdad a este Ryzen (BIOS)';
    cpuBody = `
      <p>En los Ryzen modernos el overclock que merece la pena es <b>PBO + Curve Optimizer</b>, y solo se ajusta en la BIOS. Desde Windows no se puede hacer bien sin un driver de kernel peligroso, así que esta app no lo intenta.</p>
      <ol>
        <li>Reinicia a la BIOS${advKey ? ` y pulsa <code>${advKey}</code> para el modo avanzado` : ' y entra en el modo avanzado'}.</li>
        <li>Busca <code>AMD Overclocking → Precision Boost Overdrive</code> (suele estar en <code>Advanced</code> o en <code>OC</code>) y ponlo en <code>Advanced</code>.</li>
        <li><code>PBO Limits</code>: <code>Motherboard</code>.</li>
        <li><code>Curve Optimizer</code>: <code>All Cores</code>, signo <code>Negative</code>, magnitud <b>10–15</b> para empezar.</li>
        <li><code>Max CPU Boost Clock Override</code>: <b>+100</b> o <b>+200 MHz</b> (opcional).</li>
        <li>Guarda con <code>${saveKey}</code>. Si es estable unos días, sube la magnitud hasta 20–25. Si hay cuelgues o reinicios, bájala.</li>
      </ol>
      <p>Resultado típico: algo más de rendimiento y varios grados menos, sin tocar voltajes a mano.</p>`;
  } else if (intel) {
    cpuTitle = 'Cómo sacar más rendimiento a este Intel (BIOS)';
    cpuBody = intelK ? `
      <p>Tu procesador es de la serie <b>K</b>, así que admite subir el multiplicador. Se hace en la BIOS; esta app no toca voltajes desde Windows.</p>
      <ol>
        <li>Reinicia a la BIOS y entra en el modo avanzado${advKey ? ` (<code>${advKey}</code>)` : ''}.</li>
        <li>Quita los límites de potencia si tu refrigeración lo aguanta (<code>PL1/PL2</code> o <code>Long/Short Duration Power Limit</code>).</li>
        <li>Sube el multiplicador de los núcleos P de <b>1 en 1</b> (<code>Performance Core Ratio</code>, «Sync All Cores»).</li>
        <li>Guarda con <code>${saveKey}</code>, prueba estabilidad y temperaturas (que no pasen de ~90 °C en carga).</li>
      </ol>
      <p>Alternativa más segura: un pequeño <b>undervolt</b> (offset negativo de 0,050–0,080 V) para bajar temperaturas y mantener el turbo más tiempo.</p>` : `
      <p>Los Intel sin «K» tienen el multiplicador bloqueado. Lo que sí suele dar resultado es <b>quitar los límites de potencia</b> en la BIOS (<code>PL1/PL2</code>) para que el turbo dure más, si la placa y la refrigeración lo permiten.</p>`;
  } else {
    cpuTitle = 'Overclock del procesador';
    cpuBody = '<p>El overclock del procesador se ajusta en la BIOS de la placa base; esta app no lo hace desde Windows.</p>';
  }

  let ram;
  if (amd && !am5) ram = `
    <p>${mhz ? `Tu memoria trabaja a <b>${mhz} MHz</b>.` : ''} En los Ryzen de AM4, por encima de ~3600–3800 MHz el controlador suele pasar a modo 2:1 y <b>puede rendir peor</b> que a menos frecuencia.</p>
    <p>Activa el perfil <b>${profile}</b> y, si tu kit es más rápido, prueba 3600–3800 MHz con <code style="font:12px Consolas,monospace;color:#bfe9df">FCLK</code> en 1:1 (1800–1900) y compara en tus juegos.</p>`;
  else if (am5) ram = `
    <p>${mhz ? `Tu memoria trabaja a <b>${mhz} MT/s</b>.` : ''} En los Ryzen de AM5 el punto dulce es <b>6000 MT/s</b> con <code style="font:12px Consolas,monospace;color:#bfe9df">UCLK = MCLK</code> (1:1).</p>
    <p>Activa el perfil <b>EXPO</b> de tu kit en la BIOS. Por encima de 6400 MT/s el controlador pasa a 1:2 y rara vez compensa.</p>`;
  else ram = `
    <p>${mhz ? `Tu memoria trabaja a <b>${mhz} MHz</b>.` : ''} Si en la BIOS no está activado el perfil <b>${profile}</b>, la RAM funciona a la velocidad básica y pierdes rendimiento gratis.</p>
    <p>Actívalo en la BIOS; es la forma segura de que trabaje a la velocidad para la que está fabricada.</p>`;
  return { cpuTitle, cpuBody, ram };
}

/* ══════════════════════ RATÓN ══════════════════════ */
const Mouse = {
  status: null, data: null, edit: null, busy: false, timer: 0,
  LOD: [[0.7, '0,7 mm'], [1, '1 mm'], [2, '2 mm']],
  mount(root) {
    this.root = root;
    root.innerHTML = `
    <div class="card kb-status">
      <div class="kb-st-ico">${icon('mouse')}</div>
      <div class="kb-st-main"><h3 id="ms-model">Ratón</h3><span class="sub" id="ms-sub">Buscando…</span></div>
      <div class="kb-st-right"><span class="batt" id="ms-batt" hidden></span><span class="pill off" id="ms-pill">Buscando</span>
        <button class="icon-btn" id="ms-refresh" title="Volver a leer el ratón">${icon('refresh')}</button></div>
    </div>
    <div id="ms-empty"></div>
    <div id="ms-main" hidden>
      <div class="grid">
        <div class="card span7">
          <div class="card-head">${icon('mouse')}<div><h3>Niveles de DPI</h3><span class="sub">El botón de DPI del ratón pasa de uno a otro</span></div>
            <div class="right"><label class="hint" for="ms-count">Niveles</label> <select class="field" id="ms-count">${[1, 2, 3, 4, 5, 6, 7, 8].map(n => `<option value="${n}">${n}</option>`).join('')}</select></div></div>
          <div id="ms-stages" class="ms-stages"></div>
          <p class="hint" id="ms-dpinote" hidden>Este modelo no permite cambiar los DPI desde la app.</p>
          <div class="card-foot ms-foot"><span class="hint" id="ms-hint"></span>
            <button class="btn ghost" id="ms-revert" disabled>Descartar</button><button class="btn primary" id="ms-apply" disabled>${icon('check')}Aplicar</button></div>
          <p class="hint ms-applynote">«Aplicar» guarda en el ratón todos los cambios, también los de Rendimiento.</p>
        </div>
        <div class="card span5">
          <div class="card-head">${icon('bolt')}<div><h3>Rendimiento</h3></div></div>
          <div class="ms-opt"><div><b>Frecuencia de sondeo</b><span>Veces por segundo que el ratón informa al PC</span></div><div class="seg" id="ms-rate"></div></div>
          <div class="ms-opt"><div><b>Distancia de levantamiento</b><span>Altura a la que deja de leer al levantarlo</span></div><div class="seg" id="ms-lod"></div></div>
          <div class="ms-opt"><div><b>Rebote de los botones</b><span>Menos es más rápido; por debajo de 4 ms puede dar dobles clics</span></div><select class="field" id="ms-deb"></select></div>
          <div class="ms-opt"><div><b>Reposo</b><span>Tiempo sin moverlo hasta que se duerme</span></div><select class="field" id="ms-sleep"></select></div>
          <div class="rt-row"><div><b>Motion Sync</b><span>Sincroniza el sensor con el sondeo: movimiento más uniforme</span></div><label class="switch"><input type="checkbox" id="ms-sync"><span></span></label></div>
          <div class="rt-row"><div><b>Corrección de ángulo</b><span>Endereza las líneas horizontales y verticales</span></div><label class="switch"><input type="checkbox" id="ms-snap"><span></span></label></div>
          <div class="rt-row"><div><b>Control de ondulación</b><span>Suaviza el temblor a DPI altos</span></div><label class="switch"><input type="checkbox" id="ms-ripple"><span></span></label></div>
        </div>
      </div>
    </div>`;
    const r = root;
    $('#ms-refresh', r).addEventListener('click', () => this.refresh(true));
    $('#ms-apply', r).addEventListener('click', () => this.apply());
    $('#ms-revert', r).addEventListener('click', () => { this.edit = structuredClone(this.data); this.render(); });
    $('#ms-count', r).addEventListener('change', e => { this.edit.stageCount = +e.target.value; this.edit.activeStage = Math.min(this.edit.activeStage, this.edit.stageCount - 1); this.render(); });
    $('#ms-rate', r).addEventListener('click', e => { const b = e.target.closest('[data-v]'); if (b) { this.edit.pollRate = +b.dataset.v; this.render(); } });
    $('#ms-lod', r).addEventListener('click', e => { const b = e.target.closest('[data-v]'); if (b) { this.edit.lod = +b.dataset.v; this.render(); } });
    $('#ms-deb', r).addEventListener('change', e => { this.edit.debounce = +e.target.value; this.sync(); });
    $('#ms-sleep', r).addEventListener('change', e => { this.edit.sleep = +e.target.value; this.sync(); });
    for (const [id, k] of [['ms-sync', 'motionSync'], ['ms-snap', 'angleSnap'], ['ms-ripple', 'ripple']])
      $('#' + id, r).addEventListener('change', e => { this.edit[k] = e.target.checked; this.sync(); });
    $('#ms-stages', r).addEventListener('click', e => { const b = e.target.closest('[data-act]'); if (b) { this.edit.activeStage = +b.dataset.act; this.render(); } });
  },
  show() {
    this.refresh(false);
    clearInterval(this.timer);
    this.timer = setInterval(() => { if (!document.hidden && Perif.is('vxe')) this.refresh(false, true); }, 15000);
  },
  hide() { clearInterval(this.timer); this.timer = 0; },
  async refresh(manual, quiet) {
    const r = quiet ? await api.call('mouse.status').then(x => ({ ok: true, result: x }), e => ({ ok: false, error: e })) : await attempt(() => api.call('mouse.status'));
    const st = r.ok ? r.result : { found: false, online: false, error: r.error && r.error.message };
    const was = this.status; this.status = st;
    if (st.model) Perif.setName('vxe', st.model);
    this.renderStatus();
    if (st.online && (manual || !this.data || !(was && was.online)) && !this.dirty()) this.read();
    if (manual && st.found && !st.online) toast('El ratón está dormido: muévelo para despertarlo', 'info');
  },
  renderStatus() {
    const st = this.status, root = this.root, pill = $('#ms-pill', root), batt = $('#ms-batt', root);
    const empty = $('#ms-empty', root), main = $('#ms-main', root);
    setText('#ms-model', st.model || 'Ratón', root);
    if (!st.found) {
      setText('#ms-sub', 'No conectado', root); pill.className = 'pill off'; pill.textContent = 'No encontrado'; batt.hidden = true; main.hidden = true;
      empty.innerHTML = `<div class="card"><div class="empty">${icon('mouse')}<h3>No se ha encontrado el ratón</h3><p>${esc(st.error || 'Conecta el ratón por cable o con su receptor 2,4 G.')}</p></div></div>`;
      return;
    }
    setText('#ms-sub', ['Inalámbrico 2,4 G', st.firmware && 'firmware ' + st.firmware].filter(Boolean).join(' · '), root);
    batt.hidden = !isNum(st.battery);
    if (isNum(st.battery)) { batt.innerHTML = `${icon('battery')}${fmt(st.battery)} %${st.charging ? ' · cargando' : ''}`; batt.style.color = st.battery <= 15 ? COL.hot : st.battery <= 30 ? COL.warn : ''; }
    if (st.online) { pill.className = 'pill'; pill.textContent = 'En línea'; }
    else { pill.className = 'pill warn'; pill.textContent = 'Dormido'; }
    if (!st.online && !this.data) {
      main.hidden = true;
      empty.innerHTML = `<div class="card"><div class="empty">${icon('moon')}<h3>El ratón está dormido</h3><p>Muévelo o pulsa un botón y dale a «Reintentar».</p><button class="btn" id="ms-retry">${icon('refresh')}Reintentar</button></div></div>`;
      $('#ms-retry', empty).addEventListener('click', () => this.refresh(true));
    } else if (!st.online) {
      empty.innerHTML = `<div class="note warn" style="margin-bottom:12px">${icon('moon')}<span>El ratón está dormido. Muévelo para despertarlo antes de aplicar cambios.</span></div>`;
    } else if (this.data) { empty.innerHTML = ''; main.hidden = false; }
  },
  async read() {
    if (this.busy) return;
    this.busy = true;
    if (!this.data) $('#ms-empty', this.root).innerHTML = '<div class="card"><div class="skeleton" style="height:220px"></div></div>';
    const r = await attempt(() => api.call('mouse.read'));
    this.busy = false;
    if (!r.ok) { if (!this.data) $('#ms-empty', this.root).innerHTML = ''; return; }
    this.data = r.result; this.edit = structuredClone(r.result);
    $('#ms-empty', this.root).innerHTML = ''; $('#ms-main', this.root).hidden = false;
    this.render();
  },
  render() {
    const d = this.edit, root = this.root; if (!d) return;
    $('#ms-count', root).value = d.stageCount; $('#ms-count', root).disabled = !d.dpiEditable;
    $('#ms-dpinote', root).hidden = d.dpiEditable;
    const host = $('#ms-stages', root);
    host.innerHTML = '';
    this.sl = [];
    for (let i = 0; i < d.stageCount; i++) {
      const s = sliderRow({ title: `Nivel ${i + 1}`, min: d.dpiMin, max: d.dpiMax, step: d.dpiStep, unit: 'DPI', value: d.stages[i] ?? 800 });
      s.el.classList.add('ms-stage'); s.el.classList.toggle('active', i === d.activeStage);
      $('label', s.el).insertAdjacentHTML('afterbegin', `<button class="ms-act" data-act="${i}" title="Usar este nivel">${i === d.activeStage ? 'Activo' : 'Usar'}</button>`);
      s.setDisabled(!d.dpiEditable || d.stages[i] == null);
      s.onInput(v => { d.stages[i] = v; this.sync(); });
      host.append(s.el); this.sl.push(s);
    }
    $('#ms-rate', root).innerHTML = d.pollRates.map(v => `<button data-v="${v}" class="${v === d.pollRate ? 'active' : ''}">${v} Hz</button>`).join('');
    $('#ms-lod', root).innerHTML = this.LOD.map(([v, t]) => `<button data-v="${v}" class="${v === d.lod ? 'active' : ''}">${t}</button>`).join('');
    $('#ms-deb', root).innerHTML = d.debounces.map(v => `<option value="${v}">${v} ms</option>`).join(''); $('#ms-deb', root).value = d.debounce;
    $('#ms-sleep', root).innerHTML = d.sleepTimes.map(v => `<option value="${v}">${v < 60 ? v + ' s' : v / 60 + ' min'}</option>`).join(''); $('#ms-sleep', root).value = d.sleep;
    $('#ms-sync', root).checked = d.motionSync; $('#ms-snap', root).checked = d.angleSnap; $('#ms-ripple', root).checked = d.ripple;
    this.sync();
  },
  changes() {
    const a = this.data, b = this.edit, out = {};
    if (!a || !b) return out;
    for (const k of ['pollRate', 'lod', 'debounce', 'sleep', 'motionSync', 'angleSnap', 'ripple', 'activeStage']) if (a[k] !== b[k]) out[k] = b[k];
    const sa = a.stages.slice(0, a.stageCount), sb = b.stages.slice(0, b.stageCount);
    if (b.dpiEditable && (a.stageCount !== b.stageCount || sa.some((v, i) => v !== sb[i]))) out.stages = sb.map(v => v ?? 800);
    return out;
  },
  dirty() { return Object.keys(this.changes()).length > 0; },
  sync() {
    const n = Object.keys(this.changes()).length, online = this.status && this.status.online;
    $('#ms-apply', this.root).disabled = !n || !online;
    $('#ms-revert', this.root).disabled = !n;
    setText('#ms-hint', !online ? 'Ratón dormido' : n ? 'Cambios sin aplicar' : '', this.root);
  },
  async apply() {
    const ch = this.changes(); if (!Object.keys(ch).length) return;
    if (isNum(ch.debounce) && ch.debounce < 4 && !(await modal({ title: 'Rebote muy bajo', body: '<p>Con menos de 4 ms algunos switches pueden registrar dobles clics. ¿Aplicarlo igualmente?</p>', okLabel: 'Aplicar' }))) return;
    $('#ms-apply', this.root).disabled = true;
    const r = await attempt(() => api.call('mouse.apply', ch), 'Ajustes del ratón guardados');
    if (r.ok) { this.data = structuredClone(this.edit); this.sync(); this.read(); } else this.sync();
  },
};

/* ══════════════════════ SONIDO ══════════════════════ */
const Sound = {
  data: null, timer: 0, dragging: 0, hint: '',
  mount(root) {
    this.root = root;
    root.innerHTML = `
    <div class="grid">
      <div class="card span6">
        <div class="card-head">${icon('volume')}<div><h3>Salida de sonido</h3><span class="sub">Altavoces y auriculares</span></div>
          <div class="right"><button class="icon-btn" id="snd-refresh" title="Actualizar">${icon('refresh')}</button></div></div>
        <div class="snd-list" id="snd-out"><div class="skeleton" style="height:120px"></div></div>
      </div>
      <div class="card span6">
        <div class="card-head">${icon('mic')}<div><h3>Entrada de sonido</h3><span class="sub">Micrófonos</span></div></div>
        <div class="snd-list" id="snd-in"><div class="skeleton" style="height:120px"></div></div>
      </div>
    </div>`;
    $('#snd-refresh', root).addEventListener('click', () => this.load(true));
    for (const id of ['#snd-out', '#snd-in']) {
      const host = $(id, root);
      host.addEventListener('click', e => {
        const use = e.target.closest('[data-use]'); if (use) return this.setDefault(use.dataset.use);
        const mute = e.target.closest('[data-mute]'); if (mute) return this.toggleMute(mute.dataset.mute);
      });
      host.addEventListener('input', e => {
        const r = e.target.closest('input[type=range][data-id]'); if (!r) return;
        paintRange(r);
        const v = +r.value; $(`[data-pct="${CSS.escape(r.dataset.id)}"]`, host).textContent = v + ' %';
        this.dragging = Date.now();
        clearTimeout(r._t); r._t = setTimeout(() => this.setVolume(r.dataset.id, v), 90);
      });
    }
  },
  show(it) {
    this.hint = it && it.kind === 'headset' ? it.name : '';
    this.load(false);
    clearInterval(this.timer);
    this.timer = setInterval(() => { if (!document.hidden && Perif.is('audio') && Date.now() - this.dragging > 2500) this.load(false); }, 4000);
  },
  hide() { clearInterval(this.timer); this.timer = 0; },
  async load(manual) {
    const r = await attempt(() => api.call('audio.list'), manual ? 'Dispositivos de sonido actualizados' : null);
    if (!r.ok) return;
    if (Date.now() - this.dragging < 1500) return;      // no pisar el deslizador mientras se mueve
    this.data = r.result;
    this.render();
  },
  /** Primera palabra significativa del nombre USB de los auriculares, para resaltarlos en la lista. */
  matches(name) {
    if (!this.hint) return false;
    const w = this.hint.split(/\s+/).find(x => x.length >= 3 && !/wireless|headset|usb|gaming/i.test(x));
    return !!w && name.toLowerCase().includes(w.toLowerCase());
  },
  rows(list, kind) {
    if (!list || !list.length) return `<div class="mini-empty">${kind === 'out' ? 'No hay ningún dispositivo de salida activo.' : 'No hay ningún dispositivo de entrada activo.'}</div>`;
    return list.map(d => `
      <div class="snd-row${d.isDefault ? ' def' : ''}">
        <div class="snd-top">
          <div class="snd-name"><b>${esc(d.name)}</b>${this.matches(d.name) ? '<span class="pill">Tus auriculares</span>' : ''}</div>
          ${d.isDefault ? '<span class="pill">Predeterminado</span>' : `<button class="btn sm" data-use="${esc(d.id)}">Usar este</button>`}
        </div>
        <div class="snd-vol">
          <button class="icon-btn${d.muted ? ' on' : ''}" data-mute="${esc(d.id)}" title="${d.muted ? 'Quitar silencio' : 'Silenciar'}">${icon(d.muted ? 'mute' : kind === 'out' ? 'volume' : 'mic')}</button>
          <input type="range" min="0" max="100" step="1" value="${d.volume}" data-id="${esc(d.id)}">
          <span class="snd-pct" data-pct="${esc(d.id)}">${d.volume} %</span>
        </div>
      </div>`).join('');
  },
  render() {
    const d = this.data; if (!d) return;
    $('#snd-out', this.root).innerHTML = this.rows(d.outputs, 'out');
    $('#snd-in', this.root).innerHTML = this.rows(d.inputs, 'in');
    $$('input[type=range]', this.root).forEach(paintRange);
  },
  find(id) { const d = this.data || {}; return [...(d.outputs || []), ...(d.inputs || [])].find(x => x.id === id); },
  async setVolume(id, v) {
    const r = await attempt(() => api.call('audio.setVolume', { id, volume: v }));
    const dev = this.find(id); if (r.ok && dev) dev.volume = v;
  },
  async toggleMute(id) {
    const dev = this.find(id); if (!dev) return;
    const r = await attempt(() => api.call('audio.setVolume', { id, mute: !dev.muted }));
    if (r.ok) { dev.muted = !dev.muted; this.render(); }
  },
  async setDefault(id) {
    const dev = this.find(id);
    const r = await attempt(() => api.call('audio.setDefault', { id }), dev ? `Ahora se usa: ${dev.name}` : 'Dispositivo cambiado');
    if (r.ok) this.load(false);
  },
};

/* ══════════════════════ TECLADO ══════════════════════ */
const Kb = {
  status: null, data: null, sel: new Set(), tab: 'travel', led: null, reading: false, dirty: false, keyEls: new Map(),
  SW: ['#ff3b30', '#ff9500', '#ffd60a', '#34c759', '#00d6aa', '#0a84ff', '#bf5af2', '#ffffff'],
  mount(root) {
    this.root = root;
    root.innerHTML = `
    <div class="card kb-status">
      <div class="kb-st-ico">${icon('keyboard')}</div>
      <div class="kb-st-main"><h3 id="kb-model">Teclado</h3><span class="sub" id="kb-sub">Buscando…</span></div>
      <div class="kb-st-right"><span class="batt" id="kb-batt" hidden></span><span class="pill off" id="kb-pill">Buscando</span>
        <button class="icon-btn" id="kb-refresh" title="Volver a leer el teclado">${icon('refresh')}</button></div>
    </div>
    <div id="kb-empty"></div>
    <div id="kb-main" hidden>
      <div class="tabs" id="kb-tabs"><button data-t="travel" class="active">Recorrido de las teclas</button><button data-t="led">Iluminación</button></div>
      <div id="kb-travel">
        <div class="card">
          <div class="kb-toolbar">
            <div class="grp"><button class="btn sm" data-sel="all">Todas</button><button class="btn sm" data-sel="wasd">WASD</button><button class="btn sm" data-sel="none">Ninguna</button></div>
            <span class="hint" id="kb-selinfo"></span>
            <div class="kb-legend"><span>Sensible</span><i class="grad"></i><span>Profundo</span><i class="rt-dot"></i><span>Rapid Trigger</span></div>
          </div>
          <div class="kb-board-wrap"><div class="kb-board" id="kb-board"><div class="kb-inner" id="kb-inner"></div></div></div>
        </div>
        <div class="grid kb-controls">
          <div class="card span7">
            <div class="card-head">${icon('keyboard')}<div><h3>Ajuste de las teclas seleccionadas</h3><span class="sub" id="kb-mixed"></span></div></div>
            <div id="kb-sliders"></div>
            <div class="kb-presets">
              <button class="btn sm" data-preset="type">Escritura (2,0 mm)</button>
              <button class="btn sm" data-preset="game">Gaming (1,2 mm + RT 0,3)</button>
              <button class="btn sm" data-preset="comp">Competitivo (0,6 mm + RT 0,15)</button>
            </div>
            <div class="card-foot"><span class="hint" id="kb-applyhint"></span><button class="btn primary" id="kb-apply" disabled>Aplicar</button></div>
          </div>
          <div class="card span5">
            <div class="card-head">${icon('down')}<div><h3>Recorrido del switch</h3><span class="sub">De 0 a 3,5 mm</span></div></div>
            <div id="kb-switch"></div>
          </div>
        </div>
      </div>
      <div id="kb-led" hidden>
        <div class="grid">
          <div class="card span7">
            <div class="card-head">${icon('rgb')}<div><h3>Efecto de las teclas</h3></div></div>
            <div class="led-modes" id="kb-ledmodes"></div>
            <div class="led-prev">
              <div class="led-prev-head"><b>Vista previa</b><span id="kb-ledprev-note">Aproximación de cómo se verá en el teclado</span></div>
              <canvas id="kb-ledprev"></canvas>
            </div>
          </div>
          <div class="card span5">
            <div class="card-head">${icon('sun')}<div><h3>Ajustes</h3></div></div>
            <div class="rt-row" id="kb-dazzle-row"><div><b>Multicolor</b><span>Usa todos los colores en vez de uno</span></div><label class="switch"><input type="checkbox" id="kb-dazzle"><span></span></label></div>
            <div class="srow" id="kb-ledcolor-row" style="grid-template-columns:70px minmax(0,1fr) auto"><label><b>Color</b></label><div class="swatches" id="kb-ledsw"></div><input type="color" id="kb-ledcolor"></div>
            <div id="kb-ledsliders"></div>
            <div class="srow" id="kb-ledopt-row" style="grid-template-columns:70px minmax(0,1fr)"><label><b>Dirección</b></label><select class="field" id="kb-ledopt"></select></div>
            <div class="card-foot"><button class="btn primary" id="kb-ledapply">${icon('check')}Aplicar iluminación</button></div>
          </div>
        </div>
      </div>
    </div>`;
    // Deslizadores de recorrido
    this.c = {
      act: sliderRow({ title: 'Punto de actuación', desc: 'Cuánto hay que pulsar', min: .1, max: 3.4, step: .01, digits: 2, unit: 'mm', value: 2 }),
      rtP: sliderRow({ title: 'Sensibilidad al pulsar', desc: 'Rapid Trigger', min: .05, max: 2, step: .01, digits: 2, unit: 'mm', value: .3 }),
      rtR: sliderRow({ title: 'Sensibilidad al soltar', desc: 'Rapid Trigger', min: .05, max: 2, step: .01, digits: 2, unit: 'mm', value: .3 }),
      dz: sliderRow({ title: 'Zona muerta', desc: 'Al principio del recorrido', min: 0, max: 1, step: .01, digits: 2, unit: 'mm', value: .3, warm: true }),
    };
    const rtRow = h(`<div class="rt-row"><div><b>Rapid Trigger</b><span>La tecla se suelta y se vuelve a activar con movimientos mínimos, sin volver al punto fijo</span></div><label class="switch"><input type="checkbox" id="kb-rt"><span></span></label></div>`);
    $('#kb-sliders', root).append(this.c.act.el, rtRow, this.c.rtP.el, this.c.rtR.el, this.c.dz.el);
    this.rt = $('#kb-rt', root);
    const changed = () => { this.dirty = true; this.syncControls(); };
    Object.values(this.c).forEach(c => c.onInput(changed));
    this.rt.addEventListener('change', changed);

    $('#kb-refresh', root).addEventListener('click', () => this.refresh(true));
    $('#kb-tabs', root).addEventListener('click', e => { const b = e.target.closest('[data-t]'); if (b) this.setTab(b.dataset.t); });
    $('.kb-toolbar', root).addEventListener('click', e => { const b = e.target.closest('[data-sel]'); if (b) this.quickSel(b.dataset.sel); });
    $('.kb-presets', root).addEventListener('click', e => { const b = e.target.closest('[data-preset]'); if (b) this.preset(b.dataset.preset); });
    $('#kb-apply', root).addEventListener('click', () => this.apply());
    this.bindBoard();

    // Iluminación
    this.lc = {
      bri: sliderRow({ title: 'Brillo', min: 0, max: 4, step: 1, unit: '/ 4', value: 4 }),
      spd: sliderRow({ title: 'Velocidad', min: 0, max: 4, step: 1, unit: '/ 4', value: 2 }),
    };
    for (const c of Object.values(this.lc)) c.el.style.gridTemplateColumns = '70px minmax(0,1fr) auto';
    $('#kb-ledsliders', root).append(this.lc.bri.el, this.lc.spd.el);
    $('#kb-ledsw', root).innerHTML = this.SW.map(c => `<button class="swatch" data-c="${c}" style="background:${c}"></button>`).join('');
    this.lc.bri.onInput(v => { this.led.brightness = v; });
    this.lc.spd.onInput(v => { this.led.speed = v; });
    $('#kb-ledmodes', root).addEventListener('click', e => { const b = e.target.closest('[data-m]'); if (b) { this.led.mode = +b.dataset.m; this.led.option = 0; this.syncLed(); } });
    $('#kb-ledsw', root).addEventListener('click', e => { const b = e.target.closest('[data-c]'); if (b) { this.led.color = b.dataset.c; this.led.dazzle = false; this.syncLed(); } });
    $('#kb-ledcolor', root).addEventListener('input', e => { this.led.color = e.target.value; this.led.dazzle = false; this.syncLed(); });
    $('#kb-dazzle', root).addEventListener('change', e => { this.led.dazzle = e.target.checked; this.syncLed(); });
    $('#kb-ledopt', root).addEventListener('change', e => { this.led.option = +e.target.value; });
    $('#kb-ledapply', root).addEventListener('click', async () => {
      const r = await attempt(() => api.call('kb.setLed', { ...this.led }), 'Iluminación del teclado aplicada');
      if (r.ok && this.data) this.data.led = { ...this.led };
    });
  },
  show() { this.refresh(false); this.startLedPreview(); },
  hide() { this.stopLedPreview(); },
  async refresh(manual) {
    const r = await attempt(() => api.call('kb.status'));
    if (!r.ok) return this.setStatus({ found: false, online: false, error: r.error && r.error.message }, false);
    this.setStatus(r.result, true);
    if (manual && r.result && r.result.found && !r.result.online) toast('El teclado sigue dormido: pulsa cualquier tecla para despertarlo', 'info');
  },
  setStatus(st, forceRead) {
    const was = this.status;
    // El evento kbStatus puede traer solo { online }: se combina con lo que ya se sabía.
    if (st && st.found === undefined && was) st = { ...was, ...st };
    this.status = st || { found: false, online: false };
    this.renderStatus();
    if (!Perif.is('kb')) return;
    if (this.status.found && this.status.online && (forceRead || !this.data || !(was && was.online))) this.read();
  },
  renderStatus() {
    const st = this.status, root = this.root, pill = $('#kb-pill', root), batt = $('#kb-batt', root);
    const empty = $('#kb-empty', root), main = $('#kb-main', root);
    setText('#kb-model', st.model || (st.found ? 'Teclado AJAZZ' : 'Teclado'), root);
    if (st.found && st.model) Perif.setName('ajazz', st.model);
    if (!st.found) {
      setText('#kb-sub', 'No conectado', root); pill.className = 'pill off'; pill.textContent = 'No encontrado'; batt.hidden = true;
      main.hidden = true;
      empty.innerHTML = `<div class="card"><div class="empty">${icon('keyboard')}<h3>No se ha encontrado el teclado</h3>
        <p>${esc(st.error || 'Conecta el teclado AJAZZ (por cable o con su receptor 2,4 G) y asegúrate de que el driver de AJAZZ sigue instalado: esta app usa su servicio en segundo plano.')}</p>
        <button class="btn" id="kb-retry">${icon('refresh')}Reintentar</button></div></div>`;
      $('#kb-retry', empty).addEventListener('click', () => this.refresh(true));
      return;
    }
    const parts = [st.wireless ? 'Inalámbrico 2,4 G' : 'Por cable'];
    if (st.firmware) parts.push('firmware ' + st.firmware);
    setText('#kb-sub', parts.join(' · '), root);
    batt.hidden = !isNum(st.battery);
    if (isNum(st.battery)) { batt.innerHTML = `${icon('battery')}${fmt(st.battery)} %`; batt.style.color = st.battery <= 15 ? COL.hot : st.battery <= 30 ? COL.warn : ''; }
    if (st.online) { pill.className = 'pill'; pill.textContent = 'En línea'; }
    else { pill.className = 'pill warn'; pill.textContent = 'Dormido'; }
    if (!st.online && !this.data) {
      main.hidden = true;
      empty.innerHTML = `<div class="card"><div class="empty">${icon('moon')}<h3>El teclado está dormido</h3><p>Pulsa cualquier tecla del teclado para despertarlo; se cargará solo.</p>
        <button class="btn" id="kb-retry">${icon('refresh')}Reintentar</button></div></div>`;
      $('#kb-retry', empty).addEventListener('click', () => this.refresh(true));
    } else if (!st.online) {
      empty.innerHTML = `<div class="note warn" style="margin-bottom:12px">${icon('moon')}<span>El teclado está dormido. Pulsa cualquier tecla del teclado para despertarlo; se cargará solo.</span></div>`;
      main.hidden = false;
    } else if (this.data) { empty.innerHTML = ''; main.hidden = false; }
    this.syncControls();
  },
  async read() {
    if (this.reading) return;
    this.reading = true;
    if (!this.data) $('#kb-empty', this.root).innerHTML = '<div class="card"><div class="skeleton" style="height:220px"></div></div>';
    const r = await attempt(() => api.call('kb.read'));
    this.reading = false;
    if (r.ok && r.result && Array.isArray(r.result.keys)) { this.setData(r.result); $('#kb-empty', this.root).innerHTML = ''; $('#kb-main', this.root).hidden = false; this.renderStatus(); }
    else if (!this.data) {
      $('#kb-empty', this.root).innerHTML = `<div class="card"><div class="empty">${icon('warn')}<h3>No se ha podido leer el teclado</h3><p>Puede que se haya dormido. Pulsa cualquier tecla del teclado para despertarlo; se cargará solo.</p><button class="btn" id="kb-retry">${icon('refresh')}Reintentar</button></div></div>`;
      $('#kb-retry', this.root).addEventListener('click', () => this.refresh(true));
    }
  },
  setData(d) {
    const firstTime = !this.data;
    this.data = d;
    const L = d.limits || {};
    this.lim = { actMin: L.actMin ?? .1, actMax: L.actMax ?? 3.4, rtMin: L.rtMin ?? .05, rtMax: L.rtMax ?? 2, dzMin: L.dzMin ?? 0, dzMax: L.dzMax ?? 1, step: L.step || .01 };
    const l = this.lim;
    this.c.act.setLimits(l.actMin, l.actMax, l.step); this.c.rtP.setLimits(l.rtMin, l.rtMax, l.step);
    this.c.rtR.setLimits(l.rtMin, l.rtMax, l.step); this.c.dz.setLimits(l.dzMin, l.dzMax, l.step);
    const valid = new Set(d.keys.map(k => k.i));
    this.sel = new Set([...this.sel].filter(i => valid.has(i)));
    this.renderBoard();
    this.dirty = false;
    this.loadFromSelection();
    if (firstTime || !this.led) { this.led = { mode: 0, speed: 2, brightness: 4, option: 0, dazzle: false, color: '#00d6aa', ...(d.led || {}) }; this.renderLed(); }
  },
  /* —— tablero —— */
  renderBoard() {
    const keys = this.data.keys, inner = $('#kb-inner', this.root);
    const cols = Math.max(...keys.map(k => k.x + k.w)), rows = Math.max(...keys.map(k => k.row)) + 1;
    inner.style.aspectRatio = `${cols} / ${rows}`;
    inner.innerHTML = keys.map(k => `<div class="key" data-i="${k.i}" style="left:${k.x / cols * 100}%;top:${k.row / rows * 100}%;width:${k.w / cols * 100}%;height:${100 / rows}%"><div class="keycap"><span class="lab">${esc(k.label)}</span><span class="act"></span></div></div>`).join('');
    this.keyEls = new Map($$('.key', inner).map(el => [+el.dataset.i, el]));
    this.paintKeys();
  },
  hue(act) {
    const t = clamp((act - this.lim.actMin) / (this.lim.actMax - this.lim.actMin), 0, 1);
    const stops = [[0, 8], [.3, 45], [.58, 165], [1, 208]];
    for (let i = 1; i < stops.length; i++) if (t <= stops[i][0]) { const [t0, h0] = stops[i - 1], [t1, h1] = stops[i]; return h0 + (h1 - h0) * (t - t0) / (t1 - t0); }
    return 208;
  },
  paintKeys(tempSel) {
    const sel = tempSel || this.sel;
    for (const k of this.data.keys) {
      const el = this.keyEls.get(k.i); if (!el) continue;
      const cap = el.firstElementChild, hu = this.hue(k.actuation);
      cap.style.setProperty('--kb', `hsl(${hu} 70% 46%)`); cap.style.setProperty('--kbg', `hsl(${hu} 45% 15%)`); cap.style.setProperty('--kc', `hsl(${hu} 85% 72%)`);
      setText($('.act', cap), fmtMm(k.actuation));
      const dot = $('.rt', cap);
      if (k.rt && !dot) cap.appendChild(h('<i class="rt" title="Rapid Trigger"></i>')); else if (!k.rt && dot) dot.remove();
      el.classList.toggle('sel', sel.has(k.i));
    }
    const n = sel.size;
    setText('#kb-selinfo', n ? `Teclas seleccionadas: ${n}` : 'Haz clic en las teclas o arrastra para seleccionar varias', this.root);
  },
  bindBoard() {
    const board = $('#kb-board', this.root);
    let start = null, downKey = null, dragging = false, marquee = null, base = null;
    board.addEventListener('pointerdown', e => {
      if (e.button !== 0 || !this.data) return;
      start = { x: e.clientX, y: e.clientY }; dragging = false;
      const k = e.target.closest('.key'); downKey = k ? +k.dataset.i : null;
      base = (e.ctrlKey || e.shiftKey) ? new Set(this.sel) : new Set();
      board.setPointerCapture(e.pointerId);
    });
    board.addEventListener('pointermove', e => {
      if (!start) return;
      const dx = e.clientX - start.x, dy = e.clientY - start.y;
      if (!dragging && Math.hypot(dx, dy) < 6) return;
      dragging = true;
      const br = board.getBoundingClientRect();
      if (!marquee) { marquee = h('<div class="kb-marquee"></div>'); board.appendChild(marquee); }
      const x0 = Math.min(start.x, e.clientX), y0 = Math.min(start.y, e.clientY), x1 = Math.max(start.x, e.clientX), y1 = Math.max(start.y, e.clientY);
      Object.assign(marquee.style, { left: x0 - br.left + 'px', top: y0 - br.top + 'px', width: x1 - x0 + 'px', height: y1 - y0 + 'px' });
      const hit = new Set(base);
      for (const [i, el] of this.keyEls) { const r = el.getBoundingClientRect(); if (r.left < x1 && r.right > x0 && r.top < y1 && r.bottom > y0) hit.add(i); }
      this.pendingSel = hit; this.paintKeys(hit);
    });
    const end = e => {
      if (!start) return;
      try { board.releasePointerCapture(e.pointerId); } catch { /* ya liberado */ }
      if (dragging) { this.sel = this.pendingSel || this.sel; }
      else if (downKey != null) { this.sel.has(downKey) ? this.sel.delete(downKey) : this.sel.add(downKey); }
      start = null; dragging = false; this.pendingSel = null;
      if (marquee) { marquee.remove(); marquee = null; }
      this.selectionChanged();
    };
    board.addEventListener('pointerup', end); board.addEventListener('pointercancel', end);
  },
  quickSel(what) {
    if (!this.data) return;
    if (what === 'all') this.sel = new Set(this.data.keys.map(k => k.i));
    else if (what === 'none') this.sel = new Set();
    else this.sel = new Set(this.data.keys.filter(k => /^[wasd]$/i.test(String(k.label).trim())).map(k => k.i));
    this.selectionChanged();
  },
  selectionChanged() { this.dirty = false; this.paintKeys(); this.loadFromSelection(); },
  loadFromSelection() {
    if (!this.data) return;
    const keys = this.data.keys.filter(k => this.sel.has(k.i));
    const k0 = keys[0] || this.data.keys[0];
    if (k0) {
      this.c.act.set(k0.actuation, true); this.rt.checked = !!k0.rt;
      this.c.rtP.set(isNum(k0.rtPress) ? k0.rtPress : .3, true); this.c.rtR.set(isNum(k0.rtRelease) ? k0.rtRelease : .3, true);
      this.c.dz.set(isNum(k0.deadzone) ? k0.deadzone : 0, true);
    }
    const mixed = keys.length > 1 && keys.some(k => k.actuation !== k0.actuation || !!k.rt !== !!k0.rt || k.rtPress !== k0.rtPress || k.rtRelease !== k0.rtRelease || k.deadzone !== k0.deadzone);
    const mx = $('#kb-mixed', this.root);
    mx.textContent = mixed ? 'Las teclas seleccionadas tienen valores distintos; se muestran los de la primera' : keys.length ? '' : 'Selecciona teclas en el dibujo de arriba';
    mx.className = mixed ? 'kb-mixed' : 'sub';
    this.syncControls();
  },
  syncControls() {
    if (!this.c) return;
    const rt = this.rt.checked, n = this.sel.size, online = !!(this.status && this.status.online);
    this.c.rtP.setDisabled(!rt); this.c.rtR.setDisabled(!rt);
    const btn = $('#kb-apply', this.root);
    btn.disabled = !n || !online;
    btn.innerHTML = `${icon('check')}Aplicar (${n})`;
    setText('#kb-applyhint', !online ? 'Teclado dormido' : !n ? 'Selecciona al menos una tecla' : this.dirty ? 'Cambios sin aplicar' : '', this.root);
    this.drawSwitch();
  },
  preset(p) {
    if (!this.data) return;
    if (!this.sel.size) { this.sel = new Set(this.data.keys.map(k => k.i)); this.paintKeys(); }
    const P = { type: { a: 2.0, rt: false, s: .3 }, game: { a: 1.2, rt: true, s: .3 }, comp: { a: .6, rt: true, s: .15 } }[p];
    this.c.act.set(P.a, true); this.rt.checked = P.rt; this.c.rtP.set(P.s, true); this.c.rtR.set(P.s, true);
    this.dirty = true; this.syncControls();
  },
  async apply() {
    const keys = [...this.sel]; if (!keys.length) return;
    const args = { keys, actuation: this.c.act.get(), rt: this.rt.checked, rtPress: this.c.rtP.get(), rtRelease: this.c.rtR.get(), deadzone: this.c.dz.get() };
    const btn = $('#kb-apply', this.root); btn.classList.add('busy');
    const r = await attempt(() => api.call('kb.apply', args), `Teclas cambiadas: ${keys.length}`);
    btn.classList.remove('busy');
    if (r.ok && r.result && Array.isArray(r.result.keys)) this.setData(r.result);
  },
  drawSwitch() {
    const host = $('#kb-switch', this.root); if (!host || !this.c) return;
    const act = this.c.act.get(), rt = this.rt.checked, rp = this.c.rtP.get(), rr = this.c.rtR.get(), dz = this.c.dz.get();
    const MAX = 3.5, PX = 26, Y0 = 44, y = mm => Y0 + mm * PX, d = act * 10;
    let ticks = '';
    for (let i = 0; i <= 7; i++) {
      const m = i / 2, whole = i % 2 === 0;
      ticks += `<line x1="203" y1="${y(m)}" x2="${whole ? 210 : 207}" y2="${y(m)}" stroke="#46536f" stroke-width="1.2"/>`;
      if (whole || i === 7) ticks += `<text x="215" y="${y(m) + 3.5}" fill="#8d98ad" font-size="10">${String(m).replace('.', ',')}</text>`;
    }
    // muelle entre el vástago y el fondo de la carcasa
    const s0 = 114 + d, s1 = 165;
    let spring = `M85 ${s0}`;
    for (let i = 1; i <= 6; i++) spring += ` L${i % 2 ? 95 : 75} ${(s0 + (s1 - s0) * i / 6.5).toFixed(1)}`;
    spring += ` L85 ${s1}`;
    const rtG = rt ? `<g font-size="10" fill="#cfd8ea">
        <path d="M250 ${y(act) + 4} V${Math.min(y(MAX), y(act) + 4 + Math.max(7, rp * PX))}" stroke="${COL.accent}" stroke-width="2.2" stroke-linecap="round"/><text x="257" y="${y(act) + 15}">↓ ${fmtMm(rp)}</text>
        <path d="M250 ${y(act) - 4} V${Math.max(y(0), y(act) - 4 - Math.max(7, rr * PX))}" stroke="${COL.blue}" stroke-width="2.2" stroke-linecap="round"/><text x="257" y="${y(act) - 8}">↑ ${fmtMm(rr)}</text></g>` : '';
    host.innerHTML = `<svg viewBox="0 0 320 200" font-family="Segoe UI, sans-serif">
      <rect x="25" y="176" width="120" height="7" rx="2" fill="#1c2434"/>
      <rect x="75" y="169" width="20" height="7" rx="1.5" fill="#46536f"/>
      <text x="85" y="196" text-anchor="middle" fill="#5c677e" font-size="9.5">sensor Hall</text>
      <rect x="40" y="100" width="90" height="69" rx="7" fill="#141a27" stroke="#2e3950" stroke-width="1.5"/>
      <rect x="68" y="96" width="34" height="9" rx="2.5" fill="#0d1119" stroke="#2e3950" stroke-width="1.5"/>
      <path d="${spring}" fill="none" stroke="#3a4763" stroke-width="1.7" stroke-linejoin="round"/>
      <g style="transform:translateY(${d}px);transition:transform .2s ease">
        <rect x="77" y="60" width="16" height="54" rx="3" fill="#1c2434" stroke="#46536f" stroke-width="1.5"/>
        <rect x="79" y="106" width="12" height="7" rx="2" fill="${COL.accent}"/>
        <path d="M50 60 L57 45 Q59 40 64 40 H106 Q111 40 113 45 L120 60 Z" fill="#232d42" stroke="#57658a" stroke-width="1.5" stroke-linejoin="round"/>
      </g>
      <text x="85" y="22" text-anchor="middle" fill="#8d98ad" font-size="10">así de hundida se activa</text>
      <rect x="188" y="${y(0)}" width="12" height="${MAX * PX}" rx="4" fill="#1e2637"/>
      ${dz > 0 ? `<rect x="188" y="${y(0)}" width="12" height="${Math.min(dz, act) * PX}" rx="3" fill="${hexA(COL.warn, .6)}"/>` : ''}
      <rect x="188" y="${y(act)}" width="12" height="${(MAX - act) * PX}" rx="3" fill="${hexA(COL.accent, .55)}"/>
      ${ticks}
      <line x1="176" y1="${y(act)}" x2="240" y2="${y(act)}" stroke="${COL.accent}" stroke-width="2" stroke-linecap="round"/>
      <circle cx="176" cy="${y(act)}" r="3.5" fill="${COL.accent}"/>
      ${rtG}
      <text x="176" y="22" fill="#e8edf7" font-size="12" font-weight="600">Actuación ${fmtMm(act)} mm</text>
      <text x="176" y="156" fill="#8d98ad" font-size="10"><tspan fill="${COL.accent}">■</tspan> zona activa${rt ? ' · Rapid Trigger' : ''}</text>
      ${dz > 0 ? `<text x="176" y="171" fill="#8d98ad" font-size="10"><tspan fill="${COL.warn}">■</tspan> zona muerta ${fmtMm(dz)} mm</text>` : ''}
    </svg>`;
  },
  /* —— iluminación —— */
  setTab(t) {
    this.tab = t;
    $$('#kb-tabs button', this.root).forEach(b => b.classList.toggle('active', b.dataset.t === t));
    $('#kb-travel', this.root).hidden = t !== 'travel'; $('#kb-led', this.root).hidden = t !== 'led';
    if (t === 'led') this.startLedPreview(); else this.stopLedPreview();
  },
  renderLed() {
    const modes = this.data.ledModes || [];
    $('#kb-ledmodes', this.root).innerHTML = modes.length ? modes.map(m => `<button class="led-mode" data-m="${m.id}">${esc(m.name)}</button>`).join('') : '<div class="mini-empty">El teclado no ha informado de sus efectos.</div>';
    this.syncLed();
    this.startLedPreview();
  },
  /* —— vista previa animada del efecto sobre la distribución real del teclado —— */
  startLedPreview() {
    if (this.prevRaf || this.tab !== 'led' || !this.data || !Perif.is('kb')) return;
    this.prevLast = 0;
    const loop = ts => {
      this.prevRaf = 0;
      if (this.tab !== 'led' || !this.data || !Perif.is('kb')) return;
      const now = ts / 1000;
      if (now - (this.prevDrawn || 0) >= 1 / 32) { this.prevDrawn = now; try { this.drawLedPreview(now); } catch (e) { console.error(e); return; } }
      this.prevRaf = requestAnimationFrame(loop);
    };
    this.prevRaf = requestAnimationFrame(loop);
  },
  stopLedPreview() { if (this.prevRaf) cancelAnimationFrame(this.prevRaf); this.prevRaf = 0; },
  drawLedPreview(now) {
    const cv = $('#kb-ledprev', this.root), keys = (this.data && this.data.keys) || [], led = this.led;
    if (!cv || !keys.length || !led) return;
    const cssW = cv.clientWidth; if (!cssW) return;
    const W = Math.max(...keys.map(k => k.x + k.w)), H = Math.max(...keys.map(k => k.row)) + 1;
    const u = cssW / W, cssH = u * H, dpr = window.devicePixelRatio || 1;
    if (cv.width !== Math.round(cssW * dpr) || cv.height !== Math.round(cssH * dpr)) { cv.width = Math.round(cssW * dpr); cv.height = Math.round(cssH * dpr); cv.style.height = cssH + 'px'; }

    // el tiempo avanza según la velocidad elegida (sin saltos al mover el deslizador)
    const modes = this.data.ledModes || [], m = modes.find(x => x.id === led.mode) || { hasColor: false, hasSpeed: false };
    const dt = this.prevLast ? Math.min(.1, now - this.prevLast) : 0; this.prevLast = now;
    const speed = m.hasSpeed ? [.4, .65, 1, 1.5, 2.2][Math.max(0, Math.min(4, led.speed ?? 2))] : 1;
    const t = this.prevT = (this.prevT || 0) + dt * speed;
    const mode = led.mode, opt = led.option | 0, dazzle = !!led.dazzle, rainbow = dazzle || !m.hasColor;
    const bri = mode === 0 ? 0 : [0, .4, .6, .8, 1][Math.max(0, Math.min(4, led.brightness ?? 4))];
    const note = mode === 0 ? 'Luces apagadas' : bri === 0 ? 'Con el brillo a 0 las teclas quedan apagadas: súbelo para ver el efecto' : 'Aproximación de cómo se verá en el teclado';
    if (note !== this.prevNote) { this.prevNote = note; setText('#kb-ledprev-note', note, this.root); }

    const hex = validHex(led.color) ? led.color : '#00d6aa';
    const base = [parseInt(hex.slice(1, 3), 16), parseInt(hex.slice(3, 5), 16), parseInt(hex.slice(5, 7), 16)];
    const TAU = Math.PI * 2;
    const hash = (a, b = 0) => { const s = Math.sin(a * 127.1 + b * 311.7) * 43758.5453; return s - Math.floor(s); };
    const wave = p => .5 + .5 * Math.sin(TAU * p);
    const near = (d, w) => Math.max(0, 1 - Math.abs(d) / w);
    const hue = h => { h = ((h % 1) + 1) % 1 * 6; const i = Math.floor(h), f = h - i, q = 1 - f; return [[1, f, 0], [q, 1, 0], [0, 1, f], [0, q, 1], [f, 0, 1], [1, 0, q]][i % 6].map(c => c * 255); };
    // orden en serpiente (para «Serpiente»)
    if (this.prevOrderFor !== keys) {
      const ord = new Map(); let n = 0;
      for (let r = 0; r < H; r++) { const row = keys.filter(k => k.row === r).sort((a, b) => a.x - b.x); if (r % 2) row.reverse(); for (const k of row) ord.set(k.i, n++); }
      this.prevOrder = ord; this.prevOrderFor = keys;
    }
    const N = keys.length;
    // devuelve [intensidad 0–1, tono 0–1] de una tecla para el efecto actual
    const fx = (k, cx, cy) => {
      const nx = cx / W, ny = cy / H, dx = cx - W / 2, dy = (cy - H / 2) * 1.6, d = Math.hypot(dx, dy);
      switch (mode) {
        case 0: return [0, 0];
        case 1: return [1, nx];
        case 2: return [.5 - .5 * Math.cos(t * 2.2), nx];
        case 3: return [1, t * .12];
        case 4: { const hz = opt < 2, p = (hz ? nx * 1.2 : ny * .8) + ((opt === 0 || opt === 2) ? -1 : 1) * t * .45; return [dazzle ? 1 : .12 + .88 * wave(p), p]; }
        case 5: { const r = (t * 5) % (W * .8); return [near(d - r, 1.3), d / W - t * .1]; }
        case 6: { const p = t * 2.2 + hash(k.i) * 7, s = Math.floor(p); return [hash(k.i, s) < .14 ? 1 - (p - s) : 0, hash(k.i, s + 9)]; }
        case 7: {
          const idx = this.prevOrder.get(k.i) || 0, span = opt === 1 ? 2 * N - 2 : N;
          let pos = (t * 16) % span, diff;
          if (opt === 1) { if (pos >= N) pos = span - pos; diff = Math.abs(pos - idx); } else diff = ((pos - idx) % N + N) % N;
          return [Math.max(0, 1 - diff / 7), idx / N];
        }
        case 8: case 19: {
          const p = t * 1.6 + hash(k.i) * 5, s = Math.floor(p), f = p - s, hit = hash(k.i, s) < .09;
          if (mode === 19) return [hit ? f * f : 1, nx];
          return [hit ? (opt === 1 ? Math.min(1, f * 2.5) : 1 - f) : 0, hash(k.i, s + 3)];
        }
        case 9: { const p = (t * .55) % 1; return [near(Math.abs(dx) - (1 - p) * W / 2, 1.2), p]; }
        case 10: { const y = H / 2 + (H / 2 - .6) * Math.sin(cx * .75 - t * 3); return [near(cy - y, .95), nx - t * .1]; }
        case 11: { const p = d * .13 + (opt === 1 ? 1 : -1) * t * .35; return [dazzle ? 1 : .15 + .85 * wave(p), p]; }
        case 12: { const p = (t * .5) % 1, x = opt === 1 ? W + 1 - p * (W + 2) : p * (W + 2) - 1; return [near(cx - x, 1.4), p]; }
        case 14: {
          const p = t * 1.3, s = Math.floor(p), f = p - s, row = Math.floor(hash(s, 1) * H), ox = hash(s, 2) * W, reach = f * W, dist = Math.abs(cx - ox);
          return [k.row === row && dist < reach ? Math.max(0, 1 - (reach - dist) / 6) * (1 - f * .4) : 0, hash(s, 3)];
        }
        case 15: { const p = Math.atan2(dy, dx) / TAU + (opt === 1 ? 1 : -1) * t * .3; return [dazzle ? 1 : .12 + .88 * wave(p), p]; }
        case 16: return [.3 + .7 * wave(t * .9 + hash(k.i) * 3), hash(k.i) + t * .08];
        case 17: { const col = Math.floor(cx), p = t * 1.5 + hash(col, 5) * 4, s = Math.floor(p); return [hash(col, s) < .5 ? near(cy - ((p - s) * (H + 1.5) - .75), 1) : 0, hash(col, s + 1)]; }
        case 18: { const head = ((t * 10 + hash(k.row, 7) * 30) % (W + 12)) - 4; return [cx <= head ? Math.max(0, 1 - (head - cx) / 5.5) : 0, hash(k.row, 2) + t * .03]; }
        case 23: { const L = W + 6, pos = (((k.row % 2 ? -1 : 1) * t * 8 + k.row * 3.7) % L + L) % L - 3; return [Math.min(1, near(cx - pos, 2.4) * 2.4), k.row / H + t * .02]; }
        case 24: {
          const s = Math.floor(t), f = t - s, ox = 2 + hash(s, 1) * (W - 4), oy = .5 + hash(s, 2) * (H - 1), dd = Math.hypot(cx - ox, (cy - oy) * 1.6);
          return [near(dd - f * 6.5, 1) * (1 - f * f), hash(s, 3) + dd * .04];
        }
        default: return [1, nx];
      }
    };

    const ctx = cv.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.clearRect(0, 0, cssW, cssH);
    const gap = Math.max(1.5, u * .06), rad = Math.max(2, u * .16), OFF = [24, 30, 44];
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.font = `600 ${Math.max(7, u * .25)}px "Segoe UI", system-ui, sans-serif`;
    for (const k of keys) {
      const cx = k.x + k.w / 2, cy = k.row + .5;
      const [v, h] = fx(k, cx, cy);
      const lit = Math.max(0, Math.min(1, v)) * bri, col = rainbow ? hue(h) : base;
      const rgb = OFF.map((o, i) => Math.round(o + (col[i] - o) * lit));
      const x = k.x * u + gap, y = k.row * u + gap, w = k.w * u - 2 * gap, hh = u - 2 * gap;
      ctx.shadowBlur = lit > .12 ? u * .45 * lit : 0; ctx.shadowColor = `rgba(${col[0] | 0},${col[1] | 0},${col[2] | 0},${(.7 * lit).toFixed(2)})`;
      ctx.fillStyle = `rgb(${rgb[0]},${rgb[1]},${rgb[2]})`;
      ctx.beginPath(); if (ctx.roundRect) ctx.roundRect(x, y, w, hh, rad); else ctx.rect(x, y, w, hh); ctx.fill();
      ctx.shadowBlur = 0;
      const lum = (rgb[0] * 299 + rgb[1] * 587 + rgb[2] * 114) / 1000;
      ctx.fillStyle = lum > 150 ? 'rgba(0,0,0,.6)' : 'rgba(255,255,255,.5)';
      if (k.label && u >= 22) ctx.fillText(k.label.length > 5 && k.w < 2 ? k.label.slice(0, 4) : k.label, x + w / 2, y + hh / 2 + .5);
    }
  },
  syncLed() {
    const root = this.root, led = this.led, modes = this.data.ledModes || [];
    const m = modes.find(x => x.id === led.mode) || { hasColor: false, hasSpeed: false };
    $$('.led-mode', root).forEach(b => b.classList.toggle('active', +b.dataset.m === led.mode));
    const color = validHex(led.color) ? led.color.toLowerCase() : '#00d6aa';
    $('#kb-ledcolor', root).value = color;
    $$('#kb-ledsw .swatch', root).forEach(b => b.classList.toggle('active', !led.dazzle && b.dataset.c === color));
    $('#kb-dazzle', root).checked = !!led.dazzle;
    $('#kb-dazzle-row', root).style.opacity = m.hasColor ? '' : '.45'; $('#kb-dazzle', root).disabled = !m.hasColor;
    $('#kb-ledcolor-row', root).classList.toggle('off', !m.hasColor || !!led.dazzle);
    this.lc.bri.set(led.brightness ?? 4, true); this.lc.spd.set(led.speed ?? 2, true);
    this.lc.bri.setDisabled(led.mode === 0); this.lc.spd.setDisabled(!m.hasSpeed);
    const optRow = $('#kb-ledopt-row', root), opt = $('#kb-ledopt', root);
    optRow.hidden = !(m.options && m.options.length);
    if (m.options && m.options.length) opt.innerHTML = m.options.map(o => `<option value="${o.id}" ${o.id === led.option ? 'selected' : ''}>${esc(o.name)}</option>`).join('');
  },
};

/* ══════════════════════ OVERCLOCK ══════════════════════ */
const Oc = {
  data: null, sl: null,
  mount(root) { this.root = root; root.innerHTML = '<div id="oc-gate"></div><div id="oc-main" hidden></div>'; },
  show() {
    if (state.settings.ocAccepted) { $('#oc-gate', this.root).innerHTML = ''; this.load(); }
    else this.renderGate();
  },
  renderGate() {
    const g = $('#oc-gate', this.root); $('#oc-main', this.root).hidden = true;
    g.innerHTML = `<div class="card gate">
      <h2>${icon('warn')}Antes de tocar nada</h2>
      <ul>
        <li>El overclock hace trabajar al hardware por encima de lo que garantiza el fabricante. Con valores moderados es seguro, pero <b>lo haces bajo tu responsabilidad</b> y puede afectar a la garantía.</li>
        <li>Si te pasas verás cuelgues, pantallazos o artefactos en pantalla. No se rompe nada: baja los valores o pulsa «Restablecer».</li>
        <li>Más rendimiento es más calor y más consumo. Vigila las temperaturas en la pestaña Monitor.</li>
        <li>Los ajustes de la tarjeta gráfica se aplican al instante y <b>se pierden al reiniciar</b>. Esta app no modifica voltajes.</li>
      </ul>
      <label class="check"><input type="checkbox" class="cb" id="oc-ok"><span>Lo he leído y entiendo los riesgos</span></label>
      <button class="btn primary" id="oc-accept" disabled>${icon('bolt')}Continuar</button>
    </div>`;
    $('#oc-ok', g).addEventListener('change', e => { $('#oc-accept', g).disabled = !e.target.checked; });
    $('#oc-accept', g).addEventListener('click', async () => { if (await saveSettings({ ocAccepted: true })) { g.innerHTML = ''; this.load(); } });
  },
  async load() {
    const main = $('#oc-main', this.root);
    if (!this.data) { main.hidden = false; main.innerHTML = '<div class="card"><div class="skeleton" style="height:240px"></div></div>'; }
    const r = await attempt(() => api.call('oc.get'));
    if (!r.ok) { if (!this.data) { main.innerHTML = `<div class="card"><div class="empty">${icon('warn')}<h3>No se han podido leer los ajustes</h3><button class="btn" id="oc-retry">${icon('refresh')}Reintentar</button></div></div>`; $('#oc-retry', main).addEventListener('click', () => this.load()); } return; }
    this.data = r.result || {};
    this.render();
  },
  render() {
    const d = this.data, main = $('#oc-main', this.root), hw = (state.init && state.init.hardware) || {};
    const gpu = d.gpu || { available: false }, plans = (d.power && d.power.plans) || [];
    const mods = hw.ramModules || [], speed = (d.ram && d.ram.speed) || hw.ramSpeed;
    const g = ocGuides((d.cpu && d.cpu.name) || hw.cpu, hw.board, speed);
    main.hidden = false;
    main.innerHTML = `
    <div class="grid">
      <div class="card span12">
        <div class="card-head">${icon('gpu')}<div><h3>Tarjeta gráfica</h3><span class="sub">${esc(gpu.name || hw.gpu || 'GPU')}</span></div>
          <div class="right oc-live"><span class="chip">Núcleo <b id="oc-g-clock">—</b></span><span class="chip">Memoria <b id="oc-g-mclock">—</b></span><span class="chip">${icon('thermo')}<b id="oc-g-temp">—</b></span><span class="chip">${icon('bolt')}<b id="oc-g-power">—</b></span></div></div>
        ${gpu.available ? `<div class="oc-cols">
          <div id="oc-g-sliders"></div>
          <div class="oc-side">
            <div class="oc-presets">
              <button class="btn sm" data-gp="stock">De fábrica <small>0 / 0</small></button>
              <button class="btn sm" data-gp="soft">Suave <small>+80 / +300</small></button>
              <button class="btn sm" data-gp="mid">Medio <small>+130 / +600</small></button>
              <button class="btn sm" data-gp="hard">Fuerte <small>+180 / +1000</small></button>
            </div>
            <div class="note">${icon('info')}<span>Se aplica al instante y se pierde al reiniciar. Si ves artefactos o cuelgues, baja los valores. Sube poco a poco y prueba con un juego entre cambio y cambio. «Fuerte» no es estable en todas las tarjetas: pruébalo solo si «Medio» te va bien.</span></div>
            <div class="card-foot" style="margin-top:auto"><span class="hint" id="oc-g-state"></span><button class="btn ghost" id="oc-g-reset">Restablecer</button><button class="btn primary" id="oc-g-apply">${icon('bolt')}Aplicar</button></div>
          </div>
        </div>` : `<div class="empty" style="padding:26px 10px">${icon('gpu')}<h3>Overclock de GPU no disponible</h3><p>${esc(gpu.error || 'No se ha podido acceder al controlador de NVIDIA.')}</p></div>`}
      </div>

      <div class="card span6">
        <div class="card-head">${icon('cpu')}<div><h3>Procesador</h3><span class="sub">${esc((d.cpu && d.cpu.name) || hw.cpu || 'CPU')}</span></div></div>
        <div class="stats four"><div><span>Reloj</span><b id="oc-c-clock">—</b></div><div><span>Temperatura</span><b id="oc-c-temp">—</b></div><div><span>Voltaje</span><b id="oc-c-volt">—</b></div><div><span>Potencia</span><b id="oc-c-power">—</b></div></div>
        <hr class="sep">
        <div class="card-head" style="margin-bottom:6px"><div><h3 style="font-size:13px">Plan de energía de Windows</h3><span class="sub">«Alto rendimiento» mantiene la CPU más despierta</span></div>
          <div class="right"><button class="btn sm ghost" id="oc-addplan">${icon('plus')}Máximo rendimiento</button></div></div>
        <div class="plans" id="oc-plans">${plans.length ? plans.map(p => `<div class="plan ${p.active ? 'active' : ''}" data-guid="${esc(p.guid)}"><i></i><span>${esc(p.name)}</span></div>`).join('') : '<div class="mini-empty">No se han podido leer los planes de energía.</div>'}</div>
        <details class="guide">
          <summary>${g.cpuTitle}</summary>
          <div class="g-body">${g.cpuBody}
          </div>
        </details>
        <div class="card-foot"><button class="btn" data-bios>${icon('power')}Reiniciar a la BIOS</button></div>
      </div>

      <div class="card span6">
        <div class="card-head">${icon('ram')}<div><h3>Memoria RAM</h3><span class="sub">${mods.length ? mods.length + ' módulo' + (mods.length === 1 ? '' : 's') : ''}${speed ? ' · ' + speed + ' MHz' : ''}</span></div>
          <div class="right">${speed ? `<span class="pill">${speed} MHz</span>` : ''}</div></div>
        <div class="dimms">${mods.length ? mods.map(m => `<div class="dimm">${icon('ram')}<div><b>${esc((m.maker || '') + ' ' + (m.part || ''))}</b><span>${esc(m.slot || '')}${m.sizeGb ? ' · ' + m.sizeGb + ' GB' : ''}</span></div><em>${m.speed ? m.speed + ' MHz' : ''}</em></div>`).join('') : '<div class="mini-empty">Sin información de los módulos.</div>'}</div>
        <div class="ram-free">
          <div class="stats"><div><span>En uso</span><b id="oc-r-used">—</b></div><div><span>Libre</span><b id="oc-r-free">—</b></div><div><span>Ocupación</span><b id="oc-r-load">—</b></div></div>
          <div class="ram-free-row"><span class="hint" id="oc-r-msg">Recorta la memoria que los programas tienen reservada y no están usando.</span><button class="btn primary" id="oc-trim">${icon('ram')}Liberar RAM</button></div>
        </div>
        <div class="prose" style="margin-top:12px">
          <p>La frecuencia y las latencias de la RAM <b>solo se cambian en la BIOS</b>: la placa las «entrena» al arrancar, así que ningún programa de Windows puede modificarlas en caliente.</p>
          ${g.ram}
        </div>
        <div class="card-foot"><button class="btn" data-bios>${icon('power')}Reiniciar a la BIOS</button></div>
      </div>
    </div>`;
    if (gpu.available) this.buildGpu(gpu);
    $('#oc-plans', main).addEventListener('click', async e => {
      const p = e.target.closest('.plan'); if (!p || p.classList.contains('active')) return;
      const r = await attempt(() => api.call('oc.setPlan', { guid: p.dataset.guid }), 'Plan de energía cambiado');
      if (r.ok) this.load();
    });
    $('#oc-addplan', main).addEventListener('click', async () => { const r = await attempt(() => api.call('oc.addUltimatePlan'), 'Plan «Máximo rendimiento» añadido'); if (r.ok) this.load(); });
    $$('[data-bios]', main).forEach(b => b.addEventListener('click', () => this.rebootBios()));
    $('#oc-trim', main).addEventListener('click', async e => {
      const b = e.currentTarget; b.classList.add('busy');
      const r = await attempt(() => api.call('boost.trimRam'), x => x && isNum(x.freedMb) ? `Memoria liberada: ${fmt(Math.max(0, x.freedMb))} MB` : 'Memoria recortada');
      b.classList.remove('busy');
      if (r.ok && r.result && isNum(r.result.freedMb)) setText('#oc-r-msg', `Última limpieza: ${fmt(Math.max(0, r.result.freedMb))} MB liberados. Los programas recuperan lo que necesiten al usarlos.`, main);
    });
    if (state.sensors) this.tick(state.sensors);
  },
  buildGpu(gpu) {
    const main = $('#oc-main', this.root), host = $('#oc-g-sliders', main);
    const rng = (o, capLo, capHi) => { if (!o || !isNum(o.min) || !isNum(o.max) || o.max <= o.min) return null; const cur = isNum(o.cur) ? o.cur : 0; return { min: Math.min(Math.max(o.min, capLo), cur), max: Math.max(Math.min(o.max, capHi), cur), cur }; };
    const R = { core: rng(gpu.coreOffset, -200, 250), mem: rng(gpu.memOffset, -500, 1500), pow: rng(gpu.powerLimit, 0, 1000), tmp: rng(gpu.tempLimit, 0, 200) };
    const mk = (r, o) => { const s = sliderRow({ ...o, min: r ? r.min : 0, max: r ? r.max : 1, value: r ? r.cur : 0 }); if (!r) { s.setDisabled(true); $('label', s.el).insertAdjacentHTML('beforeend', 'No disponible en esta tarjeta'); } host.appendChild(s.el); return s; };
    this.R = R;
    this.sl = {
      core: mk(R.core, { title: 'Reloj del núcleo', desc: 'Desfase en MHz', step: 5, unit: 'MHz' }),
      mem: mk(R.mem, { title: 'Reloj de la memoria', desc: 'Desfase en MHz', step: 10, unit: 'MHz' }),
      pow: mk(R.pow, { title: 'Límite de potencia', desc: 'Porcentaje del máximo', step: 1, unit: '%' }),
      tmp: mk(R.tmp, { title: 'Límite de temperatura', desc: 'La GPU frena al llegar', step: 1, unit: '°C', warm: true }),
    };
    this.applied = { core: R.core ? R.core.cur : 0, mem: R.mem ? R.mem.cur : 0, pow: R.pow ? R.pow.cur : null, tmp: R.tmp ? R.tmp.cur : null };
    const st = () => {
      const a = this.applied, dirty = this.sl.core.get() !== a.core || this.sl.mem.get() !== a.mem || (R.pow && this.sl.pow.get() !== a.pow) || (R.tmp && this.sl.tmp.get() !== a.tmp);
      setText('#oc-g-state', dirty ? 'Cambios sin aplicar' : (a.core || a.mem ? `Activo: ${signed(a.core)} / ${signed(a.mem)} MHz` : 'Valores de fábrica'), main);
    };
    Object.values(this.sl).forEach(s => s.onInput(st)); st();
    $('.oc-presets', main).addEventListener('click', e => {
      const b = e.target.closest('[data-gp]'); if (!b) return;
      const P = { stock: [0, 0], soft: [80, 300], mid: [130, 600], hard: [180, 1000] }[b.dataset.gp];
      if (!P) return;
      if (R.core) this.sl.core.set(P[0], true); if (R.mem) this.sl.mem.set(P[1], true);
      if (b.dataset.gp === 'stock') { if (R.pow && isNum(gpu.powerLimit.def)) this.sl.pow.set(gpu.powerLimit.def, true); if (R.tmp && isNum(gpu.tempLimit.def)) this.sl.tmp.set(gpu.tempLimit.def, true); }
      st();
    });
    $('#oc-g-apply', main).addEventListener('click', async () => {
      const args = { coreOffset: R.core ? this.sl.core.get() : 0, memOffset: R.mem ? this.sl.mem.get() : 0, powerLimit: R.pow ? this.sl.pow.get() : null, tempLimit: R.tmp ? this.sl.tmp.get() : null };
      if (args.coreOffset > 150 || args.memOffset > 800) {
        const ok = await modal({ title: 'Valores altos', body: `Vas a aplicar <b>${signed(args.coreOffset)} MHz</b> al núcleo y <b>${signed(args.memOffset)} MHz</b> a la memoria. Es posible que la imagen se congele o aparezcan artefactos; si pasa, reinicia el PC y todo volverá a los valores de fábrica.`, okLabel: 'Aplicar igualmente', danger: true });
        if (!ok) return;
      }
      const r = await attempt(() => api.call('oc.gpuApply', args), 'Overclock de la GPU aplicado');
      if (r.ok) { this.data.gpu = r.result || this.data.gpu; this.render(); }
    });
    $('#oc-g-reset', main).addEventListener('click', async () => {
      const r = await attempt(() => api.call('oc.gpuReset'), 'GPU restablecida a los valores de fábrica');
      if (r.ok) { this.data.gpu = r.result || this.data.gpu; this.render(); }
    });
  },
  async rebootBios() {
    const ok = await modal({ title: 'Reiniciar a la BIOS', body: 'El PC se <b>reiniciará ahora mismo</b> y entrará directamente en la BIOS (UEFI). Se cerrarán todos los programas abiertos.', okLabel: 'Reiniciar ahora', danger: true, check: 'He guardado mi trabajo y quiero reiniciar' });
    if (ok) attempt(() => api.call('oc.rebootBios'), 'Reiniciando…');
  },
  tick(s) {
    if (!this.data || !state.settings.ocAccepted) return;
    const root = this.root, c = s.cpu || {}, g = s.gpu || {};
    setText('#oc-g-clock', fmtClock(g.clockMhz), root); setText('#oc-g-mclock', fmtClock(g.memClockMhz), root);
    const gt = $('#oc-g-temp', root); if (gt) { setText(gt, fmt(g.temp, 0, ' °C')); gt.style.color = tempHex(g.temp); }
    setText('#oc-g-power', fmt(g.power, 0, ' W'), root);
    setText('#oc-c-clock', fmtClock(c.clockMhz), root);
    const ct = $('#oc-c-temp', root); if (ct) { setText(ct, fmt(c.temp, 0, ' °C')); ct.style.color = tempHex(c.temp); }
    setText('#oc-c-volt', fmt(c.voltage, 2, ' V'), root); setText('#oc-c-power', fmt(c.power, 0, ' W'), root);
    const m = s.ram || {};
    setText('#oc-r-used', fmt(m.usedGb, 1, ' GB'), root);
    setText('#oc-r-free', isNum(m.totalGb) && isNum(m.usedGb) ? fmt(m.totalGb - m.usedGb, 1, ' GB') : '—', root);
    setText('#oc-r-load', fmt(m.load, 0, ' %'), root);
  },
};

/* ══════════════════════ PROCESOS ══════════════════════ */
const Proc = {
  items: [], sel: new Set(), sort: { key: 'memMb', dir: -1 }, q: '', timer: 0, busy: false,
  mount(root) {
    this.root = root;
    root.innerHTML = `
    <div class="proc-top">
      <div class="search">${icon('search')}<input id="proc-q" placeholder="Buscar proceso…" spellcheck="false"></div>
      <span class="hint" id="proc-sum"></span>
      <div class="spacer"></div>
      <button class="btn" id="proc-trim" title="Pide a Windows que recorte la memoria que los programas no están usando">${icon('ram')}Liberar RAM</button>
      <button class="btn" id="proc-clean">${icon('broom')}Limpieza rápida</button>
    </div>
    <div class="card proc-card">
      <div class="table-wrap">
        <table class="table"><thead><tr>
          <th class="c-cb"></th>
          <th class="sortable" data-k="name">Proceso<span class="arr"></span></th>
          <th class="sortable c-num" data-k="count">Instancias<span class="arr"></span></th>
          <th class="sortable c-num" data-k="cpu">CPU<span class="arr"></span></th>
          <th class="sortable c-num" data-k="memMb">Memoria<span class="arr"></span></th>
        </tr></thead><tbody id="proc-body"></tbody></table>
      </div>
      <div class="proc-foot">
        <div class="clean-list" id="proc-cleanlist"></div>
        <button class="btn ghost sm" id="proc-save" disabled>Guardar selección como lista de limpieza</button>
        <button class="btn danger" id="proc-kill" disabled>${icon('x')}Cerrar seleccionados</button>
      </div>
    </div>`;
    $('#proc-q', root).addEventListener('input', e => { this.q = e.target.value.trim().toLowerCase(); this.render(); });
    $('thead', root).addEventListener('click', e => {
      const th = e.target.closest('[data-k]'); if (!th) return;
      const k = th.dataset.k;
      if (this.sort.key === k) this.sort.dir *= -1; else this.sort = { key: k, dir: k === 'name' ? 1 : -1 };
      this.render();
    });
    $('#proc-body', root).addEventListener('click', e => {
      const tr = e.target.closest('tr[data-n]'); if (!tr || tr.dataset.p === '1') return;
      const n = tr.dataset.n; this.sel.has(n) ? this.sel.delete(n) : this.sel.add(n); this.render();
    });
    $('#proc-kill', root).addEventListener('click', () => this.killSelected());
    $('#proc-save', root).addEventListener('click', async () => {
      const list = [...this.sel];
      if (await saveSettings({ cleanList: list })) { toast(`Lista de limpieza guardada (${list.length})`); this.render(); }
    });
    $('#proc-clean', root).addEventListener('click', () => this.quickClean());
    $('#proc-trim', root).addEventListener('click', async e => {
      const b = e.currentTarget; b.classList.add('busy');
      await attempt(() => api.call('boost.trimRam'), r => r && isNum(r.freedMb) ? `Memoria liberada: ${fmt(Math.max(0, r.freedMb))} MB` : 'Memoria recortada');
      b.classList.remove('busy'); this.refresh();
    });
    $('#proc-cleanlist', root).addEventListener('click', async e => {
      const b = e.target.closest('[data-rm]'); if (!b) return;
      const list = (state.settings.cleanList || []).filter(n => n !== b.dataset.rm);
      if (await saveSettings({ cleanList: list })) this.render();
    });
    document.addEventListener('visibilitychange', () => { if (state.page === 'proc' && !document.hidden) this.refresh(); });
  },
  show() { this.refresh(); clearInterval(this.timer); this.timer = setInterval(() => { if (!document.hidden) this.refresh(); }, 3000); },
  hide() { clearInterval(this.timer); this.timer = 0; },
  async refresh() {
    if (this.busy) return; this.busy = true;
    try {
      const r = await api.call('proc.list');
      this.items = (r && r.items) || []; this.err = null;
    } catch (e) { if (!this.err) toast(e.message, 'err', 6000); this.err = e; }
    this.busy = false;
    const names = new Set(this.items.filter(i => !i.protected).map(i => i.name));
    this.sel = new Set([...this.sel].filter(n => names.has(n)));
    this.render();
  },
  render() {
    const root = this.root, { key, dir } = this.sort;
    let list = this.items;
    if (this.q) list = list.filter(i => i.name.toLowerCase().includes(this.q) || (i.desc || '').toLowerCase().includes(this.q));
    list = [...list].sort((a, b) => key === 'name' ? a.name.localeCompare(b.name, 'es') * dir : ((a[key] || 0) - (b[key] || 0)) * dir || a.name.localeCompare(b.name, 'es'));
    const maxMem = Math.max(1, ...this.items.map(i => i.memMb || 0));
    const clean = new Set(state.settings.cleanList || []);
    $('#proc-body', root).innerHTML = list.length ? list.map(i => `<tr data-n="${esc(i.name)}" data-p="${i.protected ? 1 : 0}" class="${this.sel.has(i.name) ? 'sel' : ''}">
      <td class="c-cb">${i.protected ? `<span title="Proceso del sistema: no se puede cerrar">${icon('lock', 'lock')}</span>` : `<input type="checkbox" class="cb" ${this.sel.has(i.name) ? 'checked' : ''} tabindex="-1" style="pointer-events:none">`}</td>
      <td class="c-name"><div><b>${esc(i.name)}</b>${i.desc && i.desc !== i.name ? `<span>${esc(i.desc)}</span>` : ''}${clean.has(i.name) ? '<i class="tag">limpieza</i>' : ''}</div></td>
      <td class="c-num">${i.count > 1 ? i.count : ''}</td>
      <td class="c-num" style="color:${i.cpu >= 20 ? COL.warn : ''}">${fmt(i.cpu, 1, ' %')}</td>
      <td class="c-num"><span class="membar"><i style="width:${clamp((i.memMb || 0) / maxMem * 100, 0, 100)}%"></i></span>${fmt(i.memMb, 0, ' MB')}</td></tr>`).join('')
      : `<tr><td colspan="5"><div class="mini-empty" style="padding:30px 0">${this.err ? 'No se ha podido leer la lista de procesos' : this.q ? 'Ningún proceso coincide con la búsqueda' : 'Cargando…'}</div></td></tr>`;
    $$('th[data-k]', root).forEach(th => { $('.arr', th).textContent = th.dataset.k === key ? (dir > 0 ? '▲' : '▼') : ''; });
    const totalMem = this.items.reduce((a, i) => a + (i.memMb || 0), 0), totalN = this.items.reduce((a, i) => a + (i.count || 1), 0);
    setText('#proc-sum', this.items.length ? `${totalN} procesos · ${fmt(totalMem / 1024, 1)} GB en uso` : '', root);
    const n = this.sel.size, kill = $('#proc-kill', root);
    kill.disabled = !n; kill.innerHTML = `${icon('x')}Cerrar seleccionados${n ? ` (${n})` : ''}`;
    $('#proc-save', root).disabled = !n;
    const openClean = this.items.filter(i => clean.has(i.name) && !i.protected);
    const cb = $('#proc-clean', root); cb.disabled = !openClean.length; cb.innerHTML = `${icon('broom')}Limpieza rápida${openClean.length ? ` (${openClean.length})` : ''}`;
    const cl = [...clean];
    $('#proc-cleanlist', root).innerHTML = cl.length ? 'Lista de limpieza:' + cl.map(nm => `<span class="tagchip">${esc(nm)}<button data-rm="${esc(nm)}" title="Quitar de la lista">${icon('x')}</button></span>`).join('')
      : 'Marca procesos y guárdalos como lista de limpieza para cerrarlos de un clic antes de jugar.';
  },
  async kill(items, title) {
    if (!items.length) return;
    const ok = await modal({ title, body: `Se cerrarán estos programas y perderás lo que no hayas guardado en ellos:<ul>${items.map(i => `<li><b>${esc(i.name)}</b>${i.count > 1 ? ` (${i.count} instancias)` : ''} · ${fmt(i.memMb, 0, ' MB')}</li>`).join('')}</ul>`, okLabel: 'Cerrar procesos', danger: true });
    if (!ok) return;
    const pids = items.flatMap(i => i.pids || []);
    const r = await attempt(() => api.call('proc.kill', { pids }));
    if (r.ok) {
      const res = r.result || {}, failed = res.failed || [];
      if (res.killed) toast(`Procesos cerrados: ${res.killed}`);
      if (failed.length) toast(`Procesos que no se han podido cerrar: ${failed.length}${failed[0].error ? ' · ' + failed[0].error : ''}`, 'err', 6000);
      this.sel.clear();
    }
    setTimeout(() => this.refresh(), 350);
  },
  killSelected() { return this.kill(this.items.filter(i => this.sel.has(i.name) && !i.protected), 'Cerrar procesos seleccionados'); },
  quickClean() {
    const clean = new Set(state.settings.cleanList || []);
    return this.kill(this.items.filter(i => clean.has(i.name) && !i.protected), 'Limpieza rápida');
  },
};

/* ══════════════════════ Armazón ══════════════════════ */
const pages = { panel: Panel, monitor: Monitor, fans: Fans, rgb: Rgb, perif: Perif, oc: Oc, proc: Proc };

function showPage(id) {
  if (!pages[id] || state.page === id) return;
  const prev = pages[state.page];
  if (prev && prev.hide) prev.hide();
  state.page = id;
  $$('.page').forEach(p => p.classList.toggle('active', p.id === 'page-' + id));
  $$('#nav button').forEach(b => b.classList.toggle('active', b.dataset.page === id));
  const meta = PAGES.find(p => p.id === id);
  setText('#page-title', meta.title); setText('#page-sub', meta.sub);
  $('#content').scrollTop = 0;
  const pg = pages[id];
  if (pg.show) pg.show();
  if (state.sensors && pg.tick) requestAnimationFrame(() => { if (state.page === id) pg.tick(state.sensors); });
}

async function relaunchAdmin() {
  const ok = await modal({ title: 'Abrir como administrador', body: 'La app se cerrará y volverá a abrirse con permisos de administrador. Windows te pedirá confirmación.', okLabel: 'Reiniciar la app' });
  if (ok) attempt(() => api.call('app.relaunchAdmin'));
}

function onSensors(s) {
  if (!s || typeof s !== 'object') return;
  state.sensors = s;
  const c = s.cpu || {}, g = s.gpu || {}, r = s.ram || {}, n = s.net || {};
  push('cpuLoad', c.load); push('cpuTemp', c.temp); push('cpuPower', c.power);
  push('gpuLoad', g.load); push('gpuTemp', g.temp); push('gpuPower', g.power);
  push('ram', r.load); push('up', n.upKbps); push('down', n.downKbps);
  const tc = $('#tc-cpu'), tg = $('#tc-gpu');
  if (tc) { setText(tc, fmt(c.temp, 0, ' °C')); tc.style.color = isNum(c.temp) ? tempHex(c.temp) : ''; }
  if (tg) { setText(tg, fmt(g.temp, 0, ' °C')); tg.style.color = isNum(g.temp) ? tempHex(g.temp) : ''; }
  const pg = pages[state.page];
  if (pg && pg.tick) { try { pg.tick(s); } catch (e) { console.error(e); } }
}

function buildShell() {
  $('#nav').innerHTML = PAGES.map(p => `<button data-page="${p.id}">${icon(p.icon)}<span>${p.label}</span></button>`).join('');
  $('#nav').addEventListener('click', e => { const b = e.target.closest('[data-page]'); if (b) showPage(b.dataset.page); });
  // Selector de idioma
  const lp = h(`<label class="lang-pick" title="Idioma">${icon('globe')}<select id="lang-sel">${I18N.langs.map(l => `<option value="${l.code}"${l.code === I18N.code ? ' selected' : ''}>${l.name}</option>`).join('')}</select></label>`);
  $('#nav').after(lp);
  $('#lang-sel', lp).addEventListener('change', async e => { const code = e.target.value; await saveSettings({ lang: code }); I18N.set(code); });
  $('#top-right').innerHTML = `
    <span class="chip" title="Temperatura del procesador">${icon('cpu')}<b id="tc-cpu">—</b></span>
    <span class="chip" title="Temperatura de la tarjeta gráfica">${icon('gpu')}<b id="tc-gpu">—</b></span>
    <span class="chip"><span id="tc-date"></span><b id="tc-time"></b></span>`;
  const clock = () => {
    const d = new Date();
    setText('#tc-date', d.toLocaleDateString(I18N.locale, { weekday: 'short', day: 'numeric', month: 'short' }));
    setText('#tc-time', d.toLocaleTimeString(I18N.locale, { hour: '2-digit', minute: '2-digit' }));
  };
  clock(); setInterval(clock, 5000);
  for (const p of PAGES) pages[p.id].mount($('#page-' + p.id));
  window.addEventListener('resize', debounce(() => { const pg = pages[state.page]; if (pg && pg.tick && state.sensors) pg.tick(state.sensors); }, 120));
}

function applyInit() {
  const init = state.init, hw = init.hardware || {};
  setText('#brand-ver', 'v' + (init.version || '1.0.0'));
  $('#side-foot').innerHTML = [['cpu', hw.cpu], ['gpu', hw.gpu], ['chip', hw.board], ['ram', hw.ramTotalGb ? `${hw.ramTotalGb} GB${hw.ramSpeed ? ' · ' + hw.ramSpeed + ' MHz' : ''}` : null]]
    .filter(x => x[1]).map(([ic, t]) => `<div class="hw">${icon(ic)}<span>${esc(String(t).replace(/^(AMD|NVIDIA GeForce|ASUS)\s+/i, ''))}</span></div>`).join('');
  const banner = $('#admin-banner');
  if (!init.isAdmin) {
    banner.hidden = false; banner.className = 'banner';
    banner.innerHTML = `${icon('shield')}<span>Para ver la temperatura de la CPU y controlar ventiladores hace falta abrir como administrador.</span><button class="btn sm" id="banner-admin">Abrir como administrador</button>`;
    $('#banner-admin').addEventListener('click', relaunchAdmin);
  } else if (init.pawnIo === false) {
    banner.hidden = false; banner.className = 'banner info';
    banner.innerHTML = `${icon('info')}<span>Falta el driver PawnIO: sin él no se pueden leer las temperaturas de la CPU ni mover los ventiladores de la placa.</span>`;
  } else banner.hidden = true;
}

function loadScript(src) {
  return new Promise((res, rej) => { const s = document.createElement('script'); s.src = src; s.onload = res; s.onerror = () => rej(new Error('No se ha podido cargar ' + src)); document.head.appendChild(s); });
}

async function boot() {
  const bootEl = $('#boot');
  try {
    await I18N.init();
    if (!hasWebView) await loadScript('mock.js');
    api.attach();
    api.on('sensors', onSensors);
    api.on('kbStatus', st => Kb.setStatus(st, false));
    state.init = await api.call('app.init');
    if (!state.init || typeof state.init !== 'object') throw new Error('Respuesta de inicio no válida');
    state.settings = state.init.settings || {};
    // Idioma guardado en los ajustes (manda sobre el detectado); la primera vez se guarda el del sistema.
    const lang = state.settings.lang;
    if (lang && lang !== I18N.code && I18N.langs.some(l => l.code === lang)) { I18N.set(lang); return; }
    if (!lang) saveSettings({ lang: I18N.code });
    else I18N.remember(lang);
    buildShell();
    applyInit();
    showPage('panel');
    bootEl.classList.add('done');
    setTimeout(() => bootEl.remove(), 400);
  } catch (e) {
    console.error(e);
    bootEl.innerHTML = `<div class="empty">${icon('warn')}<h3>No se ha podido iniciar</h3><p>${esc(e.message || String(e))}</p><button class="btn" onclick="location.reload()">Reintentar</button></div>`;
  }
}

boot();
