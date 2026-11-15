/* Centro de Mando — idiomas.
   La interfaz está escrita en español. En otro idioma, cada texto que aparece en pantalla (nodos de
   texto y atributos title / placeholder / aria-label) se sustituye por su traducción de lang/<código>.js.
   Las claves pueden llevar {} donde va un valor variable (números, nombres…). */
'use strict';

const LANGS = [
  { code: 'es', name: 'Español', locale: 'es-ES' },
  { code: 'en', name: 'English', locale: 'en-GB' },
  { code: 'fr', name: 'Français', locale: 'fr-FR' },
  { code: 'de', name: 'Deutsch', locale: 'de-DE' },
  { code: 'it', name: 'Italiano', locale: 'it-IT' },
  { code: 'pt', name: 'Português', locale: 'pt-PT' },
  { code: 'ca', name: 'Català', locale: 'ca-ES' },
  { code: 'pl', name: 'Polski', locale: 'pl-PL' },
  { code: 'ru', name: 'Русский', locale: 'ru-RU' },
  { code: 'zh', name: '简体中文', locale: 'zh-CN' },
];

const I18N = (() => {
  const pick = code => LANGS.find(l => l.code === code);
  let saved = null;
  try { saved = localStorage.getItem('cdm.lang'); } catch { /* sin almacenamiento */ }
  const sys = (navigator.language || 'es').slice(0, 2).toLowerCase();
  const cur = pick(saved) || pick(sys) || pick('en');

  const PLURAL_S = new Set(['en', 'fr', 'pt', 'ca']);
  let exact = new Map(), patterns = [], cache = new Map(), observer = null;
  const ATTRS = ['title', 'placeholder', 'aria-label'];
  const orig = new WeakMap();            // elemento → { atributo: texto original }
  const norm = s => s.replace(/\s+/g, ' ').trim();
  const hasLetters = s => /[A-Za-zÀ-ÿ]{2}/.test(s);

  function load(dict) {
    exact = new Map(); patterns = [];
    for (const [k, v] of Object.entries(dict || {})) {
      if (typeof v !== 'string' || !v) continue;
      if (k.includes('{}')) {
        const re = new RegExp('^' + k.split('{}').map(p => p.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('(.*?)') + '$');
        patterns.push({ re, parts: v.split('{}'), n: k.split('{}').length - 1 });
      } else exact.set(k, v);
    }
    exact.set('Centro de Mando', 'Centro de Mando');                    // nombre de la app: no se traduce
    patterns.sort((a, b) => b.re.source.length - a.re.source.length);   // primero los más concretos
    cache = new Map();
  }

  /** Traducción de un texto completo, o null si no hay. */
  function tr(text, depth = 0) {
    const s = norm(text);
    if (!s || !hasLetters(s)) return null;
    if (cache.has(s)) return cache.get(s);
    let out = exact.get(s) ?? null;
    if (out == null) {
      for (const p of patterns) {
        const m = s.match(p.re);
        if (!m) continue;
        const vals = m.slice(1).map(x => {
          // Terminaciones de plural del español pegadas a una palabra («tecla{}» → «teclas»)
          if (/^(s|es)$/.test(x)) return PLURAL_S.has(cur.code) ? x : '';
          if (/^(ha|han)$/.test(x)) return '';
          const t = depth < 2 && tr(x, depth + 1);
          return t ? keepSpace(x, t) : x;
        });
        out = p.parts.reduce((acc, part, i) => acc + part + (i < vals.length ? vals[i] : ''), '');
        break;
      }
    }
    if (out == null && depth < 2) {
      // Textos compuestos: «Inalámbrico 2,4 G · firmware 3.15», «Nivel 1», etiquetas con dos puntos…
      for (const sep of [' · ', ': ', ' — ']) {
        if (!s.includes(sep)) continue;
        const parts = s.split(sep), t = parts.map(x => tr(x, depth + 1));
        if (t.some(Boolean)) { out = parts.map((x, i) => t[i] || x).join(sep); break; }
      }
    }
    if (cache.size > 5000) cache.clear();
    cache.set(s, out);
    return out;
  }

  /** Conserva los espacios de alrededor del nodo original. */
  function keepSpace(raw, t) { const a = raw.match(/^\s*/)[0], b = raw.match(/\s*$/)[0]; return a + t + b; }

  function doText(node) {
    const raw = node.nodeValue;
    if (node.__cdmOut === raw) return;              // ya es nuestra traducción
    const t = tr(raw);
    if (t == null) return;
    const v = keepSpace(raw, t);
    node.__cdmOut = v;
    if (v !== raw) node.nodeValue = v;
  }
  function doAttrs(el) {
    for (const a of ATTRS) {
      if (!el.hasAttribute || !el.hasAttribute(a)) continue;
      const raw = el.getAttribute(a), o = orig.get(el) || {};
      if (o['out:' + a] === raw) continue;
      const t = tr(raw);
      if (t == null) continue;
      o['out:' + a] = t; orig.set(el, o);
      el.setAttribute(a, t);
    }
  }
  function walk(root) {
    if (root.nodeType === 3) return doText(root);
    if (root.nodeType !== 1) return;
    if (root.tagName === 'SCRIPT' || root.tagName === 'STYLE') return;
    doAttrs(root);
    const it = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, {
      acceptNode: n => n.nodeType === 1 && (n.tagName === 'SCRIPT' || n.tagName === 'STYLE') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
    });
    for (let n = it.nextNode(); n; n = it.nextNode()) n.nodeType === 3 ? doText(n) : doAttrs(n);
  }

  function start() {
    document.documentElement.lang = cur.code;
    if (cur.code === 'es') return;
    walk(document.body);
    observer = new MutationObserver(list => {
      for (const m of list) {
        if (m.type === 'characterData') doText(m.target);
        else if (m.type === 'attributes') doAttrs(m.target);
        else m.addedNodes.forEach(walk);
      }
    });
    observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ATTRS });
  }

  return {
    langs: LANGS, code: cur.code, locale: cur.locale,
    /** Carga el diccionario del idioma activo (si no es español) y empieza a traducir. */
    async init() {
      if (cur.code !== 'es') {
        await new Promise(res => {
          window.CDM_LANG = d => load(d);
          const s = document.createElement('script');
          s.src = `lang/${cur.code}.js`; s.onload = res; s.onerror = res;
          document.head.appendChild(s);
        });
      }
      start();
    },
    /** Traduce un texto suelto (para mensajes generados en JS). */
    t(s) { return cur.code === 'es' ? s : (tr(s) || s); },
    /** Cambia de idioma: se guarda y se recarga la interfaz. */
    set(code) {
      if (!pick(code)) return;
      try { localStorage.setItem('cdm.lang', code); } catch { /* sin almacenamiento */ }
      location.reload();
    },
    remember(code) { try { localStorage.setItem('cdm.lang', code); } catch { /* sin almacenamiento */ } },
  };
})();
