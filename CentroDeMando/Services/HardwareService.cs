using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace CentroDeMando.Services;

public sealed record CoreDto(string Name, double? Load, double? ClockMhz);
public sealed record CpuDto(string Name, double? Load, double? Temp, double? Power, double? ClockMhz, double? Voltage, List<CoreDto> Cores);
public sealed record GpuDto(string Name, double? Load, double? Temp, double? Hotspot, double? Power, double? FanRpm, double? FanPct,
    double? ClockMhz, double? MemClockMhz, double? VramUsedMb, double? VramTotalMb);
public sealed record RamDto(double UsedGb, double TotalGb, double Load);
public sealed record DriveDto(string Name, double? Temp, double? UsedPct);
public sealed record NetDto(double UpKbps, double DownKbps);
public sealed record TempDto(string Id, string Name, string Group, double Value);
public sealed record FanDto(string Id, string Name, double? Rpm, double? Pct, bool Controllable, string Mode);
public sealed record Snapshot(CpuDto Cpu, GpuDto Gpu, RamDto Ram, List<DriveDto> Drives, NetDto Net, List<TempDto> Temps, List<FanDto> Fans);

public sealed class FanCurve
{
    public string? SourceId { get; set; }
    public List<double[]> Points { get; set; } = new();
}
public sealed class FanConfig
{
    public string Id { get; set; } = "";
    public string Mode { get; set; } = "auto";     // auto | manual | curve
    public double? Pct { get; set; }
    public FanCurve? Curve { get; set; }
}

/// <summary>Sensores (LibreHardwareMonitor) y control de ventiladores.</summary>
public sealed class HardwareService : IDisposable
{
    readonly Computer _pc;
    readonly Thread _thread;
    readonly object _cfgLock = new();
    readonly Dictionary<string, FanConfig> _fanCfg = new();
    readonly Dictionary<string, float> _applied = new();          // id de control -> último % aplicado por software
    readonly HashSet<string> _activeFans = new();
    readonly Dictionary<string, double> _smooth = new();
    volatile bool _stop;
    volatile Snapshot? _last;
    public string? OpenError { get; private set; }
    public Snapshot? Last => _last;
    public event Action<Snapshot>? Updated;
    public Action<List<FanConfig>>? SaveFans;

    readonly bool _asusBoard;

    public HardwareService(IEnumerable<FanConfig>? saved, bool asusBoard = false)
    {
        _asusBoard = asusBoard;
        if (saved != null) foreach (var f in saved) if (!string.IsNullOrEmpty(f.Id)) _fanCfg[f.Id] = f;
        _pc = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true,
            IsStorageEnabled = true, IsNetworkEnabled = true, IsMemoryEnabled = false,
            IsControllerEnabled = false, IsBatteryEnabled = false, IsPsuEnabled = false,
        };
        _thread = new Thread(Loop) { IsBackground = true, Name = "sensores" };
    }

    public void Start() => _thread.Start();

    void Loop()
    {
        try { _pc.Open(); }
        catch (Exception ex) { OpenError = ex.Message; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!_stop)
        {
            long t0 = sw.ElapsedMilliseconds;
            try
            {
                var snap = Sample();
                _last = snap;
                Updated?.Invoke(snap);
            }
            catch (Exception ex) { OpenError = ex.Message; }
            int wait = (int)Math.Max(100, 1000 - (sw.ElapsedMilliseconds - t0));
            for (int i = 0; i < wait / 50 && !_stop; i++) Thread.Sleep(50);
        }
        RestoreFans();
        try { _pc.Close(); } catch { }
    }

    // ───────────── muestreo ─────────────

    static IEnumerable<IHardware> AllHardware(IEnumerable<IHardware> roots)
    {
        foreach (var h in roots)
        {
            yield return h;
            foreach (var s in AllHardware(h.SubHardware)) yield return s;
        }
    }

    static double? V(ISensor? s)
    {
        if (s?.Value is not float f || float.IsNaN(f) || float.IsInfinity(f)) return null;
        return f;
    }
    static double? R(double? v, int d = 1) => v.HasValue ? Math.Round(v.Value, d) : null;
    /// <summary>Descarta ceros y negativos (lecturas que LHM devuelve cuando no tiene acceso al sensor).</summary>
    static double? Pos(double? v) => v is > 0 ? v : null;

    static ISensor? Find(IEnumerable<ISensor> list, SensorType type, params string[] names)
    {
        var ofType = list.Where(s => s.SensorType == type).ToList();
        foreach (var n in names)
        {
            var hit = ofType.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                   ?? ofType.FirstOrDefault(s => s.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (hit != null && hit.Value.HasValue) return hit;
        }
        return null;
    }

    Snapshot Sample()
    {
        var hw = AllHardware(_pc.Hardware).ToList();
        foreach (var h in hw) { try { h.Update(); } catch { } }

        // CPU
        var cpuHw = hw.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var cs = cpuHw?.Sensors ?? Array.Empty<ISensor>();
        var cores = new SortedDictionary<int, (List<double> load, double? clock)>();
        foreach (var s in cs)
        {
            var m = Regex.Match(s.Name, @"Core #(\d+)");
            if (!m.Success) continue;
            int n = int.Parse(m.Groups[1].Value);
            if (!cores.TryGetValue(n, out var e)) e = (new List<double>(), null);
            if (s.SensorType == SensorType.Load && V(s) is double l) e.load.Add(l);
            if (s.SensorType == SensorType.Clock && V(s) is double c) e.clock = c;
            cores[n] = e;
        }
        // LHM da la carga por hilo ("CPU Core #1..#12") y el reloj por núcleo físico ("Core #1..#6"):
        // si hay el doble de cargas que de relojes, se agrupan los hilos de dos en dos.
        int nLoad = cores.Count(kv => kv.Value.load.Count > 0), nClock = cores.Count(kv => kv.Value.clock.HasValue);
        var coreList = new List<CoreDto>();
        if (nClock > 0 && nLoad == nClock * 2)
        {
            for (int c = 1; c <= nClock; c++)
            {
                var loads = new List<double>();
                if (cores.TryGetValue(2 * c - 1, out var a)) loads.AddRange(a.load);
                if (cores.TryGetValue(2 * c, out var b)) loads.AddRange(b.load);
                double? clk = cores.TryGetValue(c, out var k) ? k.clock : null;
                coreList.Add(new CoreDto($"Núcleo {c}", loads.Count > 0 ? R(loads.Average()) : null, R(Pos(clk), 0)));
            }
        }
        else
        {
            string label = nClock > 0 && nLoad > nClock ? "Hilo" : "Núcleo";
            coreList = cores.Select(kv => new CoreDto($"{label} {kv.Key}",
                kv.Value.load.Count > 0 ? R(kv.Value.load.Average()) : null, R(Pos(kv.Value.clock), 0))).ToList();
        }
        var clocks = coreList.Where(c => c.ClockMhz.HasValue).Select(c => c.ClockMhz!.Value).ToList();
        double? vcore = V(Find(cs, SensorType.Voltage, "Core (SVI2 TFN)", "Core", "Vcore"));
        double? cpuTemp = Pos(V(Find(cs, SensorType.Temperature, "Core (Tctl/Tdie)", "Tctl", "Package", "Core")));
        var cpu = new CpuDto(cpuHw?.Name ?? "CPU",
            R(V(Find(cs, SensorType.Load, "CPU Total"))),
            R(cpuTemp),
            R(Pos(V(Find(cs, SensorType.Power, "Package", "CPU")))),
            clocks.Count > 0 ? Math.Round(clocks.Max(), 0) : null,
            cpuTemp.HasValue && vcore is > 0.2 and < 1.549 ? R(vcore, 3) : null,   // 1,55 V exactos = telemetría SVI2 sin dato (VID 0)
            coreList);

        // GPU
        var gpuHw = hw.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
                 ?? hw.FirstOrDefault(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel);
        var gs = gpuHw?.Sensors ?? Array.Empty<ISensor>();
        var gpuFan = gs.FirstOrDefault(s => s.SensorType == SensorType.Fan);
        var gpuCtl = gs.FirstOrDefault(s => s.SensorType == SensorType.Control);
        var gpu = new GpuDto(gpuHw?.Name ?? "GPU",
            R(V(Find(gs, SensorType.Load, "GPU Core", "D3D 3D"))),
            R(V(Find(gs, SensorType.Temperature, "GPU Core"))),
            R(V(Find(gs, SensorType.Temperature, "GPU Hot Spot", "Hot Spot"))),
            R(V(Find(gs, SensorType.Power, "GPU Package", "GPU Power"))),
            R(V(gpuFan), 0), R(V(gpuCtl), 0),
            R(V(Find(gs, SensorType.Clock, "GPU Core")), 0),
            R(V(Find(gs, SensorType.Clock, "GPU Memory")), 0),
            R(V(Find(gs, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used")), 0),
            R(V(Find(gs, SensorType.SmallData, "GPU Memory Total")), 0));

        // RAM
        var ram = ReadRam();

        // Discos
        var drives = new List<DriveDto>();
        foreach (var d in hw.Where(h => h.HardwareType == HardwareType.Storage))
            drives.Add(new DriveDto(Clean(d.Name), R(V(d.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value > 0 && !IsThreshold(s.Name))
                    .OrderBy(s => s.Index).FirstOrDefault()), 0),
                R(V(Find(d.Sensors, SensorType.Load, "Used Space")), 0)));

        // Red (la interfaz más activa)
        double up = 0, down = 0;
        foreach (var n in hw.Where(h => h.HardwareType == HardwareType.Network))
        {
            up = Math.Max(up, V(Find(n.Sensors, SensorType.Throughput, "Upload Speed")) ?? 0);
            down = Math.Max(down, V(Find(n.Sensors, SensorType.Throughput, "Download Speed")) ?? 0);
        }
        var net = new NetDto(Math.Round(up * 8 / 1000, 0), Math.Round(down * 8 / 1000, 0));

        // Temperaturas
        var temps = new List<TempDto>();
        foreach (var h in hw)
        {
            string? group = h.HardwareType switch
            {
                HardwareType.Cpu => "CPU",
                HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
                HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => "Placa",
                HardwareType.Storage => "Disco",
                HardwareType.Memory => "RAM",
                _ => null,
            };
            if (group == null) continue;
            foreach (var s in h.Sensors.Where(s => s.SensorType == SensorType.Temperature))
            {
                if (V(s) is not double v || v <= 0 || v >= 125) continue;
                if (s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase) || IsThreshold(s.Name)) continue;
                string name = TranslateTemp(s.Name);
                if (group == "Disco")
                {
                    // Los discos traen varios sensores; el principal lleva solo el nombre del disco.
                    bool main = s.Name.Equals("Temperature", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("Composite", StringComparison.OrdinalIgnoreCase);
                    string extra = main ? "" : " · " + name.Replace("Temperatura ", "sensor ");
                    name = Clean(h.Name) + extra;
                }
                temps.Add(new TempDto(s.Identifier.ToString(), name, group, Math.Round(v, 1)));
            }
        }

        // Ventiladores + control
        var fans = new List<FanDto>();
        var controls = new Dictionary<string, ISensor>();
        foreach (var h in hw.Where(h => h.HardwareType is HardwareType.SuperIO or HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.EmbeddedController or HardwareType.Cooler))
        {
            bool isGpu = h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd;
            var rpm = h.Sensors.Where(s => s.SensorType == SensorType.Fan).OrderBy(s => s.Index).ToList();
            var ctl = h.Sensors.Where(s => s.SensorType == SensorType.Control).OrderBy(s => s.Index).ToList();
            var usedCtl = new HashSet<ISensor>();
            foreach (var f in rpm)
            {
                var c = ctl.FirstOrDefault(x => x.Index == f.Index && x.Control != null);
                string id = (c ?? f).Identifier.ToString();
                double? r = V(f);
                if (r > 0 || isGpu) _activeFans.Add(id);
                if (c != null) { usedCtl.Add(c); controls[id] = c; }
                if (!_activeFans.Contains(id)) continue;
                fans.Add(new FanDto(id, FanName(f.Name, isGpu, rpm.Count, _asusBoard && h.HardwareType == HardwareType.SuperIO), R(r, 0), R(V(c), 0), c != null, ModeOf(id)));
            }
        }

        ApplyFans(controls, temps, cpu.Temp);
        // refleja el modo real tras aplicar
        for (int i = 0; i < fans.Count; i++) fans[i] = fans[i] with { Mode = ModeOf(fans[i].Id) };
        return new Snapshot(cpu, gpu, ram, drives, net, temps, fans);
    }

    static string TranslateTemp(string n) => n
        .Replace("Core (Tctl/Tdie)", "Núcleo (Tctl/Tdie)")
        .Replace("GPU Core", "Núcleo GPU").Replace("GPU Hot Spot", "Punto caliente GPU")
        .Replace("Temperature", "Temperatura").Replace("Motherboard", "Placa base").Replace("System", "Sistema");

    static string Clean(string s) => s.Replace("\0", "").Trim();
    static bool IsThreshold(string n) =>
        n.Contains("Warning", StringComparison.OrdinalIgnoreCase) || n.Contains("Critical", StringComparison.OrdinalIgnoreCase) ||
        n.Contains("Threshold", StringComparison.OrdinalIgnoreCase) || n.Contains("Limit", StringComparison.OrdinalIgnoreCase);

    static string FanName(string n, bool gpu, int count, bool asusSuperIo = false)
    {
        if (gpu) return count > 1 ? n.Replace("GPU Fan", "Ventilador GPU") : "Ventilador GPU";
        if (asusSuperIo)
        {
            // Cableado habitual de los conectores en placas ASUS AM4 con chip Nuvoton NCT679x.
            var m = Regex.Match(n, @"^Fan #(\d+)$");
            if (m.Success)
                return int.Parse(m.Groups[1].Value) switch
                {
                    1 => "Ventilador caja 1 (CHA_FAN1)",
                    2 => "Ventilador CPU (CPU_FAN)",
                    3 => "Ventilador caja 2 (CHA_FAN2)",
                    4 => "Ventilador caja 3 (CHA_FAN3)",
                    7 => "Bomba / ventilador (AIO_PUMP)",
                    var k => $"Ventilador #{k}",
                };
        }
        return n.Replace("Fan Control", "Ventilador").Replace("CPU Fan", "Ventilador CPU").Replace("Chassis Fan", "Ventilador caja")
                .Replace("Pump", "Bomba").Replace("Fan", "Ventilador");
    }

    string ModeOf(string id)
    {
        lock (_cfgLock) return _fanCfg.TryGetValue(id, out var c) ? c.Mode : "auto";
    }

    // ───────────── RAM ─────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX b);

    public static RamDto ReadRam()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double total = m.ullTotalPhys / 1073741824.0, used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        return new RamDto(Math.Round(used, 1), Math.Round(total, 1), m.dwMemoryLoad);
    }
    public static double AvailableMb()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        return m.ullAvailPhys / 1048576.0;
    }

    // ───────────── ventiladores ─────────────

    public List<FanConfig> GetFanConfig()
    {
        lock (_cfgLock) return _fanCfg.Values.Select(Clone).ToList();
    }

    static FanConfig Clone(FanConfig f) => new()
    {
        Id = f.Id, Mode = f.Mode, Pct = f.Pct,
        Curve = f.Curve == null ? null : new FanCurve { SourceId = f.Curve.SourceId, Points = f.Curve.Points.Select(p => p.ToArray()).ToList() },
    };

    public void SetFan(FanConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.Id)) throw new ArgumentException("Falta el identificador del ventilador.");
        if (cfg.Mode is not ("auto" or "manual" or "curve")) throw new ArgumentException("Modo no válido.");
        if (cfg.Mode == "manual" && cfg.Pct is null) throw new ArgumentException("Falta el porcentaje.");
        if (cfg.Mode == "curve")
        {
            if (cfg.Curve == null || cfg.Curve.Points.Count < 2 || cfg.Curve.Points.Any(p => p.Length < 2))
                throw new ArgumentException("La curva necesita al menos dos puntos.");
            cfg.Curve.Points = cfg.Curve.Points.OrderBy(p => p[0]).ToList();
        }
        List<FanConfig> all;
        lock (_cfgLock)
        {
            if (_fanCfg.TryGetValue(cfg.Id, out var old))
            {
                // conserva la curva/porcentaje anteriores para poder volver a ellos
                cfg.Curve ??= old.Curve;
                cfg.Pct ??= old.Pct;
            }
            _fanCfg[cfg.Id] = cfg;
            all = _fanCfg.Values.Select(Clone).ToList();
        }
        SaveFans?.Invoke(all);
    }

    static double EvalCurve(FanCurve c, double t)
    {
        var p = c.Points;
        if (t <= p[0][0]) return p[0][1];
        for (int i = 1; i < p.Count; i++)
        {
            if (t <= p[i][0])
            {
                double span = p[i][0] - p[i - 1][0];
                double k = span <= 0 ? 1 : (t - p[i - 1][0]) / span;
                return p[i - 1][1] + k * (p[i][1] - p[i - 1][1]);
            }
        }
        return p[^1][1];
    }

    void ApplyFans(Dictionary<string, ISensor> controls, List<TempDto> temps, double? cpuTemp)
    {
        List<FanConfig> cfgs;
        lock (_cfgLock) cfgs = _fanCfg.Values.Select(Clone).ToList();
        foreach (var cfg in cfgs)
        {
            if (!controls.TryGetValue(cfg.Id, out var sensor) || sensor.Control is not IControl ctl) continue;
            try
            {
                double? target = null;
                if (cfg.Mode == "manual") target = cfg.Pct;
                else if (cfg.Mode == "curve" && cfg.Curve != null && cfg.Curve.Points.Count >= 2)
                {
                    var src = temps.FirstOrDefault(t => t.Id == cfg.Curve.SourceId);
                    double? t = src?.Value ?? cpuTemp;
                    if (t.HasValue)
                    {
                        // suavizado para que el ventilador no oscile con cada pico
                        double s = _smooth.TryGetValue(cfg.Id, out var prev) ? prev + (t.Value - prev) * 0.35 : t.Value;
                        _smooth[cfg.Id] = s;
                        target = EvalCurve(cfg.Curve, s);
                    }
                }
                // Protección: si la CPU pasa de 90 °C, todo lo que controlemos va al máximo.
                if (target.HasValue && cpuTemp >= 90) target = 100;

                if (target.HasValue)
                {
                    float v = (float)Math.Clamp(target.Value, Math.Max(0, ctl.MinSoftwareValue), Math.Min(100, ctl.MaxSoftwareValue));
                    if (!_applied.TryGetValue(cfg.Id, out var last) || Math.Abs(last - v) >= 1.5f || ctl.ControlMode != ControlMode.Software)
                    {
                        ctl.SetSoftware(v);
                        _applied[cfg.Id] = v;
                    }
                }
                else if (_applied.Remove(cfg.Id) || ctl.ControlMode == ControlMode.Software)
                {
                    ctl.SetDefault();
                }
            }
            catch { }
        }
    }

    void RestoreFans()
    {
        try
        {
            foreach (var h in AllHardware(_pc.Hardware))
                foreach (var s in h.Sensors.Where(s => s.SensorType == SensorType.Control))
                    if (s.Control is IControl c && c.ControlMode == ControlMode.Software) { try { c.SetDefault(); } catch { } }
        }
        catch { }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(3000);
    }
}
