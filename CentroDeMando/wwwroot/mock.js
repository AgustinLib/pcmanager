/* Puente simulado para ver la interfaz en un navegador normal (fuera de WebView2).
   Solo lo carga app.js cuando window.chrome.webview no existe.
   Parámetros de prueba en la URL: ?noadmin=1  ?kb=sleep|none  ?aura=off  ?gpuoc=off  ?nopawn=1 */
(function () {
  'use strict';
  if (window.chrome && window.chrome.webview) return;

  const qs = new URLSearchParams(location.search);
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const rnd = (a, b) => a + Math.random() * (b - a);
  const isAdmin = !qs.has('noadmin');
  let deliver = () => { };

  const settings = {
    countdowns: [{ name: 'GTA VI', date: '2026-11-19T00:00:00' }], countdownIndex: 0,
    weather: null,
    cleanList: ['Discord', 'steamwebhelper'],
    ocAccepted: false,
  };

  const hardware = {
    cpu: 'AMD Ryzen 7 5800X', gpu: 'NVIDIA GeForce RTX 3070', board: 'ASUS TUF GAMING B550-PLUS', bios: '2803',
    ramTotalGb: 16, ramSpeed: 4000,
    ramModules: [
      { slot: 'DIMM_A2', maker: 'Corsair', part: 'CMK16GX4M2B3600C18', sizeGb: 8, speed: 3600 },
      { slot: 'DIMM_B2', maker: 'Corsair', part: 'CMK16GX4M2B3600C18', sizeGb: 8, speed: 3600 },
    ],
  };

  /* ───── sensores ───── */
  const sim = { cpuLoad: 16, gpuLoad: 9, ram: 46, up: 180, down: 2400, cores: [12, 20, 8, 30, 10, 14], gpuOc: { core: 0, mem: 0, pow: 100, tmp: 83 } };
  const fanCfg = {
    'fan/cpu': { id: 'fan/cpu', mode: 'curve', pct: 55, curve: { sourceId: 'temp/cpu', points: [[30, 25], [50, 38], [65, 58], [75, 80], [85, 100]] } },
    'fan/cha1': { id: 'fan/cha1', mode: 'auto', pct: 50, curve: { sourceId: 'temp/cpu', points: [[30, 30], [50, 40], [65, 55], [75, 75], [85, 100]] } },
    'fan/cha2': { id: 'fan/cha2', mode: 'manual', pct: 45, curve: { sourceId: 'temp/board', points: [[30, 30], [50, 40], [65, 55], [75, 75], [85, 100]] } },
    '/gpu-nvidia/0/fan/1': { id: '/gpu-nvidia/0/fan/1', mode: 'auto', pct: 40, curve: { sourceId: 'temp/gpu', points: [[35, 0], [50, 30], [65, 50], [75, 70], [85, 100]] } },
  };
  const fanDefs = [
    { id: 'fan/cpu', name: 'Ventilador CPU', max: 1850, src: 'cpu' },
    { id: 'fan/cha1', name: 'Chasis 1 (frontal)', max: 1400, src: 'cpu' },
    { id: 'fan/cha2', name: 'Chasis 2 (trasero)', max: 1400, src: 'cpu' },
    { id: '/gpu-nvidia/0/fan/1', name: 'Ventilador GPU', max: 2600, src: 'gpu' },
  ];
  const curveAt = (pts, t) => { if (t <= pts[0][0]) return pts[0][1]; for (let i = 1; i < pts.length; i++) if (t <= pts[i][0]) { const [a, b] = pts[i - 1], [c, d] = pts[i]; return b + (d - b) * (t - a) / (c - a); } return pts[pts.length - 1][1]; };
  const walk = (v, lo, hi, step) => clamp(v + (Math.random() - .5) * step, lo, hi);
  let tick = 0;

  function sensors() {
    tick++;
    const burst = Math.sin(tick / 23) > .55;
    sim.cpuLoad = walk(sim.cpuLoad + (burst ? 2.2 : -1.6), 4, 82, 9);
    sim.gpuLoad = walk(sim.gpuLoad + (burst ? 4 : -2.5), 2, 97, 8);
    sim.ram = walk(sim.ram, 38, 78, 1.2);
    sim.up = walk(sim.up, 20, 9000, 300); sim.down = walk(sim.down, 50, 68000, burst ? 9000 : 1800);
    sim.cores = sim.cores.map(c => clamp(walk(c, 1, 100, 22) * .5 + sim.cpuLoad * .5, 1, 100));
    const cpuTemp = 37 + sim.cpuLoad * .46 + rnd(-.7, .7);
    const gpuTemp = 36 + sim.gpuLoad * .38 + rnd(-.5, .5) + sim.gpuOc.core / 60;
    const boardTemp = 34 + sim.cpuLoad * .06, vrm = 41 + sim.cpuLoad * .16, nvme = 39 + sim.gpuLoad * .05;
    const admin = isAdmin;
    const temps = [
      { id: 'temp/cpu', name: 'CPU (Tctl/Tdie)', group: 'CPU', value: admin ? cpuTemp : null },
      { id: 'temp/gpu', name: 'Núcleo', group: 'GPU', value: gpuTemp },
      { id: 'temp/gpuhot', name: 'Punto caliente', group: 'GPU', value: gpuTemp + 9.5 },
    ];
    if (admin) temps.push(
      { id: 'temp/board', name: 'Placa base', group: 'Placa', value: boardTemp },
      { id: 'temp/vrm', name: 'VRM', group: 'Placa', value: vrm },
      { id: 'temp/chipset', name: 'Chipset B550', group: 'Placa', value: 46 + rnd(-.3, .3) });
    temps.push({ id: 'temp/nvme', name: 'Samsung 980 1TB', group: 'Disco', value: nvme }, { id: 'temp/ssd', name: 'WD Blue 1TB', group: 'Disco', value: 31 + rnd(-.2, .2) });
    const tOf = id => (temps.find(t => t.id === id) || {}).value ?? 45;
    const fans = [];
    for (const d of fanDefs) {
      if (!admin && d.id !== '/gpu-nvidia/0/fan/1') continue;
      const cfg = fanCfg[d.id];
      let pct = cfg.mode === 'manual' ? cfg.pct : cfg.mode === 'curve' ? curveAt(cfg.curve.points, tOf(cfg.curve.sourceId)) : clamp(22 + ((d.src === 'gpu' ? gpuTemp : cpuTemp) - 35) * 1.5, d.src === 'gpu' ? 0 : 25, 100);
      if (d.id === '/gpu-nvidia/0/fan/1' && cfg.mode === 'auto' && gpuTemp < 46) pct = 0;
      d.cur = d.cur == null ? pct : d.cur + (pct - d.cur) * .35;
      fans.push({ id: d.id, name: d.name, rpm: d.cur < 3 ? 0 : Math.round(d.max * (.12 + .88 * d.cur / 100) + rnd(-12, 12)), pct: Math.round(d.cur), controllable: admin || d.id === '/gpu-nvidia/0/fan/1', mode: cfg.mode });
    }
    const gpuFan = fans.find(f => f.id === '/gpu-nvidia/0/fan/1');
    const clock = 3600 + sim.cpuLoad * 6.5 + rnd(-30, 30);
    return {
      cpu: {
        name: hardware.cpu, load: sim.cpuLoad, temp: admin ? cpuTemp : null, power: admin ? 14 + sim.cpuLoad * .62 : null, clockMhz: clock, voltage: admin ? 1.02 + sim.cpuLoad * .0032 : null,
        cores: sim.cores.map((l, i) => ({ name: 'Núcleo ' + (i + 1), load: l, clockMhz: Math.round(3550 + l * 6.8 + rnd(-25, 25)) })),
      },
      gpu: {
        name: hardware.gpu, load: sim.gpuLoad, temp: gpuTemp, hotspot: gpuTemp + 9.5, power: 9 + sim.gpuLoad * 1.06 * sim.gpuOc.pow / 100,
        fanRpm: gpuFan ? gpuFan.rpm : null, fanPct: gpuFan ? gpuFan.pct : null,
        clockMhz: Math.round((sim.gpuLoad > 25 ? 2460 : 210 + sim.gpuLoad * 20) + (sim.gpuLoad > 25 ? sim.gpuOc.core : 0)),
        memClockMhz: Math.round((sim.gpuLoad > 25 ? 8501 : 405) + (sim.gpuLoad > 25 ? sim.gpuOc.mem : 0)),
        vramUsedMb: 900 + sim.gpuLoad * 52, vramTotalMb: 8188,
      },
      ram: { usedGb: 15.9 * sim.ram / 100, totalGb: 15.9, load: sim.ram },
      drives: [{ name: 'Samsung 980 1TB (C:)', temp: nvme, usedPct: 63 }, { name: 'WD Blue 1TB (D:)', temp: 31, usedPct: 82 }],
      net: { upKbps: sim.up, downKbps: sim.down },
      temps, fans,
    };
  }

  /* ───── teclado ───── */
  const kbRows = [
    [['Esc', 1], ['1', 1], ['2', 1], ['3', 1], ['4', 1], ['5', 1], ['6', 1], ['7', 1], ['8', 1], ['9', 1], ['0', 1], ['-', 1], ['=', 1], ['Retroceso', 2], ['Inicio', 1]],
    [['Tab', 1.5], ['Q', 1], ['W', 1], ['E', 1], ['R', 1], ['T', 1], ['Y', 1], ['U', 1], ['I', 1], ['O', 1], ['P', 1], ['[', 1], [']', 1], ['\\', 1.5], ['Supr', 1]],
    [['Bloq', 1.75], ['A', 1], ['S', 1], ['D', 1], ['F', 1], ['G', 1], ['H', 1], ['J', 1], ['K', 1], ['L', 1], [';', 1], ["'", 1], ['Intro', 2.25], ['RePág', 1]],
    [['Mayús', 2.25], ['Z', 1], ['X', 1], ['C', 1], ['V', 1], ['B', 1], ['N', 1], ['M', 1], [',', 1], ['.', 1], ['/', 1], ['Mayús', 1.75], ['↑', 1], ['AvPág', 1]],
    [['Ctrl', 1.25], ['Win', 1.25], ['Alt', 1.25], ['Espacio', 6.25], ['Alt', 1], ['Fn', 1], ['Ctrl', 1], ['←', 1], ['↓', 1], ['→', 1]],
  ];
  const kbKeys = [];
  kbRows.forEach((row, r) => { let x = 0; row.forEach(([label, w], c) => { kbKeys.push({ i: c * 6 + r + 1, label, row: r, x, w, actuation: 2.0, rt: false, rtPress: .5, rtRelease: .5, deadzone: .3 }); x += w; }); });
  for (const k of kbKeys) if (/^[WASD]$/.test(k.label)) { k.actuation = 1.2; k.rt = true; k.rtPress = .3; k.rtRelease = .3; }
  let kbLed = { mode: 4, speed: 2, brightness: 3, option: 0, dazzle: true, color: '#969696' };
  const dir = [{ id: 0, name: 'Derecha' }, { id: 1, name: 'Izquierda' }];
  const ledModes = [
    { id: 0, name: 'Apagado', hasColor: false, hasSpeed: false },
    { id: 1, name: 'Fijo', hasColor: true, hasSpeed: false },
    { id: 2, name: 'Respiración', hasColor: true, hasSpeed: true },
    { id: 3, name: 'Neón', hasColor: false, hasSpeed: true },
    { id: 4, name: 'Onda', hasColor: true, hasSpeed: true, options: [{ id: 0, name: 'Derecha' }, { id: 1, name: 'Izquierda' }, { id: 2, name: 'Abajo' }, { id: 3, name: 'Arriba' }] },
    { id: 5, name: 'Ondulación', hasColor: true, hasSpeed: true },
    { id: 6, name: 'Gotas de lluvia', hasColor: true, hasSpeed: true },
    { id: 7, name: 'Serpiente', hasColor: true, hasSpeed: true, options: [{ id: 0, name: 'En zigzag' }, { id: 1, name: 'Ida y vuelta' }] },
    { id: 8, name: 'Reactivo', hasColor: true, hasSpeed: true },
    { id: 9, name: 'Convergencia', hasColor: true, hasSpeed: true },
    { id: 10, name: 'Onda sinusoidal', hasColor: true, hasSpeed: true },
    { id: 11, name: 'Caleidoscopio', hasColor: true, hasSpeed: true, options: [{ id: 0, name: 'Hacia fuera' }, { id: 1, name: 'Hacia dentro' }] },
    { id: 12, name: 'Onda lineal', hasColor: true, hasSpeed: true, options: dir },
    { id: 14, name: 'Láser', hasColor: true, hasSpeed: true },
    { id: 15, name: 'Onda circular', hasColor: true, hasSpeed: true, options: [{ id: 0, name: 'Antihorario' }, { id: 1, name: 'Horario' }] },
    { id: 16, name: 'Deslumbrante', hasColor: true, hasSpeed: true },
    { id: 17, name: 'Lluvia', hasColor: true, hasSpeed: true },
    { id: 18, name: 'Meteoro', hasColor: true, hasSpeed: true },
    { id: 19, name: 'Reactivo inverso', hasColor: true, hasSpeed: true },
    { id: 23, name: 'Tren', hasColor: true, hasSpeed: true },
    { id: 24, name: 'Fuegos artificiales', hasColor: true, hasSpeed: true, options: dir },
  ];
  const audioCfg = { outputs: [{ id: 'o1', name: 'Auriculares (USB Wireless Headset)', isDefault: true, volume: 50, muted: false }, { id: 'o2', name: 'Altavoces (Realtek High Definition Audio)', isDefault: false, volume: 80, muted: false }, { id: 'o3', name: 'Monitor (NVIDIA High Definition Audio)', isDefault: false, volume: 100, muted: false }], inputs: [{ id: 'i1', name: 'Micrófono (USB Wireless Headset)', isDefault: true, volume: 90, muted: false }, { id: 'i2', name: 'Micrófono (Realtek High Definition Audio)', isDefault: false, volume: 100, muted: false }] };
  const mouseCfg = { pollRate: 1000, pollRates: [125, 250, 500, 1000], stageCount: 4, activeStage: 1, stages: [400, 800, 1600, 3200, 3200, 3200, 3200, 3200], dpiEditable: true, dpiMin: 50, dpiMax: 10000, dpiStep: 50, lod: 1, debounce: 4, debounces: [0, 1, 2, 4, 8, 15, 20], motionSync: true, sleep: 60, sleepTimes: [60, 180, 300, 600, 1800], angleSnap: false, ripple: false };
  let kbMode = qs.get('kb') || 'ok';
  const kbStatus = () => kbMode === 'none' ? { found: false, online: false, error: null }
    : { found: true, online: kbMode !== 'sleep', model: 'AJAZZ AK680 MAX', wireless: true, battery: 82, firmware: '3.15' };
  const kbRead = () => {
    if (kbMode !== 'ok') throw new Error('El teclado no está en línea (dev is not online)');
    return { led: { ...kbLed }, ledModes, limits: { actMin: .1, actMax: 3.4, rtMin: .05, rtMax: 2.0, dzMin: 0, dzMax: 1.0, step: .01 }, keys: kbKeys.map(k => ({ ...k })) };
  };
  if (kbMode === 'sleep') setTimeout(() => { kbMode = 'ok'; deliver({ event: 'kbStatus', data: { online: true } }); }, 9000);

  /* ───── procesos ───── */
  const procs = [
    ['System', 'NT Kernel & System', 1, .4, 0.2, true], ['Registry', '', 1, 0, 78, true], ['svchost', 'Proceso host para los servicios de Windows', 74, .9, 612, true],
    ['csrss', 'Proceso en tiempo de ejecución cliente-servidor', 2, .1, 9, true], ['dwm', 'Administrador de ventanas del escritorio', 1, 1.8, 164, true],
    ['explorer', 'Explorador de Windows', 1, .6, 212, true], ['MsMpEng', 'Antimalware Service Executable', 1, .3, 286, true], ['lsass', 'Local Security Authority Process', 1, 0, 21, true],
    ['chrome', 'Google Chrome', 23, 4.2, 2140, false], ['Discord', 'Discord', 6, 1.1, 468, false], ['steam', 'Steam', 1, .2, 148, false], ['steamwebhelper', 'Steam Client WebHelper', 9, .7, 612, false],
    ['Spotify', 'Spotify', 5, .4, 331, false], ['Code', 'Visual Studio Code', 12, 1.3, 902, false], ['claude', 'Claude', 8, 2.1, 744, false],
    ['ArmouryCrate.Service', 'Armoury Crate Service', 1, .2, 96, false], ['LightingService', 'ASUS Aura LightingService', 1, .3, 58, false], ['ArmourySocketServer', 'Armoury Socket Server', 1, 0, 34, false],
    ['NVIDIA App', 'NVIDIA App', 4, .1, 208, false], ['nvcontainer', 'NVIDIA Container', 3, .1, 77, false], ['iot_driver_v187', 'AJAZZ Driver (servicio)', 1, 0, 24, false],
    ['EpicGamesLauncher', 'Epic Games Launcher', 1, .5, 286, false], ['OneDrive', 'Microsoft OneDrive', 1, .1, 112, false], ['msedgewebview2', 'Microsoft Edge WebView2', 7, .6, 398, false],
    ['SearchHost', 'Búsqueda de Windows', 1, 0, 141, false], ['Widgets', 'Widgets de Windows', 1, 0, 43, false], ['PhoneExperienceHost', 'Enlace Móvil', 1, 0, 88, false],
    ['RtkAudUService64', 'Realtek HD Audio Universal Service', 1, 0, 11, false], ['CentroDeMando', 'Centro de Mando', 1, .8, 126, true],
  ].map(([name, desc, count, cpu, memMb, prot], i) => ({ name, desc, count, cpu, memMb, protected: prot, pids: Array.from({ length: Math.min(count, 30) }, (_, k) => 1000 + i * 100 + k * 4) }));
  const procList = () => ({ items: procs.map(p => ({ ...p, cpu: Math.max(0, +(p.cpu + rnd(-.4, .6) * (p.cpu > .2 ? 1 : .1)).toFixed(1)), memMb: Math.round(p.memMb * rnd(.97, 1.03)) })) });

  /* ───── overclock ───── */
  const plans = [
    { guid: '381b4222-f694-41f0-9685-ff5bb260df2e', name: 'Equilibrado', active: true },
    { guid: '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c', name: 'Alto rendimiento', active: false },
    { guid: 'a1841308-3541-4fab-bc81-f71556f20b4a', name: 'Economizador', active: false },
  ];
  const ocGpu = () => qs.get('gpuoc') === 'off' ? { available: false, error: 'No se ha encontrado el controlador de NVIDIA (nvapi64.dll).', name: hardware.gpu }
    : { available: true, name: hardware.gpu, coreOffset: { cur: sim.gpuOc.core, min: -1000, max: 1000 }, memOffset: { cur: sim.gpuOc.mem, min: -1000, max: 3000 }, powerLimit: { cur: sim.gpuOc.pow, min: 78, max: 100, def: 100 }, tempLimit: { cur: sim.gpuOc.tmp, min: 65, max: 88, def: 83 } };

  /* ───── comandos ───── */
  const handlers = {
    'app.init': () => ({ version: '1.0.0', isAdmin, pawnIo: !qs.has('nopawn'), hardware, settings: { ...settings } }),
    'app.relaunchAdmin': () => { setTimeout(() => { qs.delete('noadmin'); location.search = qs.toString(); }, 600); return null; },
    'settings.set': a => { Object.assign(settings, (a && a.patch) || {}); return { ...settings }; },
    'fans.get': () => Object.values(fanCfg).map(c => JSON.parse(JSON.stringify(c))),
    'fans.set': a => {
      const c = fanCfg[a.id]; if (!c) throw new Error('Ventilador desconocido: ' + a.id);
      c.mode = a.mode; if (typeof a.pct === 'number') c.pct = a.pct; if (a.curve) c.curve = JSON.parse(JSON.stringify(a.curve)); return null;
    },
    'proc.list': procList,
    'proc.kill': a => {
      const failed = []; let killed = 0;
      for (const pid of (a.pids || [])) {
        const p = procs.find(x => x.pids.includes(pid));
        if (!p) { failed.push({ pid, error: 'El proceso ya no existe' }); continue; }
        if (p.name === 'LightingService') { failed.push({ pid, error: 'Acceso denegado' }); continue; }
        p.pids = p.pids.filter(x => x !== pid); p.count--; killed++;
      }
      for (let i = procs.length - 1; i >= 0; i--) if (procs[i].count <= 0) procs.splice(i, 1);
      return { killed, failed };
    },
    'boost.trimRam': () => { const f = Math.round(rnd(600, 1900)); sim.ram = clamp(sim.ram - f / 160, 30, 90); return { freedMb: f }; },
    'aura.status': () => qs.get('aura') === 'off' ? { available: false, error: 'No se ha encontrado el controlador Aura de la placa (USB 0B05:1939).', devices: [] }
      : { available: true, devices: [{ id: 'rgb12v', name: 'Conector RGB 12 V', type: 'Tira RGB', leds: 2 }, { id: 'argb1', name: 'Conector ARGB 1 (tiras / ventiladores)', type: 'Tira direccionable', leds: 60 }] },
    'aura.apply': () => null,
    'aura.release': () => null,
    'mouse.status': () => ({ found: true, online: true, model: 'VXE Dragonfly R1 SE+', battery: 80, charging: false, firmware: '3.00' }),
    'mouse.read': () => structuredClone(mouseCfg),
    'mouse.apply': a => { Object.assign(mouseCfg, a); if (a.stages) { mouseCfg.stageCount = a.stages.length; a.stages.forEach((v, i) => mouseCfg.stages[i] = v); } return true; },
    'audio.list': () => structuredClone(audioCfg),
    'audio.setVolume': a => { const d = [...audioCfg.outputs, ...audioCfg.inputs].find(x => x.id === a.id); if (a.volume != null) d.volume = a.volume; if (a.mute != null) d.muted = a.mute; return true; },
    'audio.setDefault': a => { for (const l of [audioCfg.outputs, audioCfg.inputs]) if (l.some(x => x.id === a.id)) l.forEach(x => x.isDefault = x.id === a.id); return true; },
    'periph.list': () => ({ items: [
      { key: '3151:5026', kind: 'keyboard', name: '2.4G Wireless Keyboard', vid: 0x3151, pid: 0x5026, driver: 'ajazz', wireless: true },
      { key: '373B:1085', kind: 'mouse', name: 'VXE Mouse 1K Dongle', vid: 0x373B, pid: 0x1085, driver: 'vxe', wireless: true },
      { key: '046D:C53F', kind: 'headset', name: 'USB Wireless Headset', vid: 0x046D, pid: 0xC53F, driver: null, wireless: true },
    ] }),
    'kb.status': kbStatus,
    'kb.read': kbRead,
    'kb.setLed': a => { if (kbMode !== 'ok') throw new Error('El teclado no está en línea'); kbLed = { ...kbLed, ...a }; return null; },
    'kb.apply': a => {
      if (kbMode !== 'ok') throw new Error('El teclado no está en línea');
      for (const k of kbKeys) if ((a.keys || []).includes(k.i)) { k.actuation = a.actuation; k.rt = !!a.rt; k.rtPress = a.rtPress; k.rtRelease = a.rtRelease; k.deadzone = a.deadzone; }
      return kbRead();
    },
    'oc.get': () => ({ gpu: ocGpu(), power: { plans: plans.map(p => ({ ...p })) }, cpu: { name: hardware.cpu }, ram: { speed: 4000, modules: 2 } }),
    'oc.gpuApply': a => { if (!isAdmin) throw new Error('Para aplicar overclock a la GPU hay que abrir la app como administrador.'); sim.gpuOc = { core: a.coreOffset || 0, mem: a.memOffset || 0, pow: a.powerLimit ?? 100, tmp: a.tempLimit ?? 83 }; return ocGpu(); },
    'oc.gpuReset': () => { sim.gpuOc = { core: 0, mem: 0, pow: 100, tmp: 83 }; return ocGpu(); },
    'oc.setPlan': a => { if (!plans.some(p => p.guid === a.guid)) throw new Error('Plan de energía desconocido'); plans.forEach(p => p.active = p.guid === a.guid); return null; },
    'oc.addUltimatePlan': () => { if (!plans.some(p => p.name === 'Máximo rendimiento')) plans.push({ guid: 'e9a42b02-d5df-448d-aa00-03f14749eb61', name: 'Máximo rendimiento', active: false }); return null; },
    'oc.rebootBios': () => null,
  };

  window.MockBridge = {
    connect(fn) {
      deliver = fn;
      setTimeout(() => deliver({ event: 'sensors', data: sensors() }), 60);
      setInterval(() => deliver({ event: 'sensors', data: sensors() }), 1000);
    },
    post(msg) {
      const fn = handlers[msg.cmd];
      setTimeout(() => {
        try {
          if (!fn) throw new Error('Comando desconocido: ' + msg.cmd);
          const r = fn(msg.args || {});
          deliver({ id: msg.id, ok: true, result: r === undefined ? null : r });
        } catch (e) { deliver({ id: msg.id, ok: false, error: e.message }); }
      }, 40 + Math.random() * 90);
    },
  };
})();
