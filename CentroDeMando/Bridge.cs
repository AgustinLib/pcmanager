using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CentroDeMando.Services;

namespace CentroDeMando;

/// <summary>Enrutador de comandos entre la interfaz web y los servicios nativos.</summary>
public sealed class Bridge : IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,
    };

    public const string Version = "1.1.0";

    public readonly SettingsStore Settings = new();
    public readonly HardwareService Hardware;
    public readonly KeyboardService Keyboard = new();
    public readonly MouseService Mouse = new();
    public readonly CentroDeMando.Services.Brands.BrandService Brands = new();
    public readonly AuraService Aura = new();
    public readonly GpuOcService GpuOc = new();
    public readonly ProcessService Processes = new();
    readonly HardwareInfo _hwInfo;
    readonly Dictionary<string, Func<JsonElement, object?>> _cmds;

    /// <summary>Envía un JSON a la interfaz (lo conecta la ventana).</summary>
    public Action<string>? Post;
    public Action? RequestClose;

    public Bridge()
    {
        _hwInfo = SystemInfo.Read();
        Hardware = new HardwareService(Settings.GetFans(), _hwInfo.Board.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase)) { SaveFans = Settings.SetFans };
        Hardware.Updated += s => Emit("sensors", s);
        Keyboard.StatusChanged += s => Emit("kbStatus", s);
        Aura.TempSource = () => (Hardware.Last?.Cpu.Temp, Hardware.Last?.Gpu.Temp);

        _cmds = new()
        {
            ["app.init"] = _ => new
            {
                version = Version,
                isAdmin = SystemInfo.IsAdmin(),
                pawnIo = SystemInfo.PawnIoInstalled(),
                hardware = _hwInfo,
                settings = Settings.Snapshot(),
            },
            ["app.relaunchAdmin"] = _ => { RelaunchAsAdmin(); return true; },
            ["settings.set"] = a => Settings.Patch(a.TryGetProperty("patch", out var p) ? p : a),

            ["fans.get"] = _ => Hardware.GetFanConfig(),
            ["fans.set"] = a => { Hardware.SetFan(a.Deserialize<FanConfig>(Json) ?? throw new ArgumentException("Datos no válidos.")); return true; },

            ["proc.list"] = _ => new { items = Processes.List() },
            ["proc.kill"] = a =>
            {
                var pids = a.GetProperty("pids").EnumerateArray().Select(x => x.GetInt32()).ToList();
                var (killed, failed) = Processes.Kill(pids);
                return new { killed, failed };
            },
            ["boost.trimRam"] = _ => new { freedMb = Processes.TrimRam() },

            ["aura.status"] = _ => Aura.Status(),
            ["aura.apply"] = a => { Aura.Apply(a.Deserialize<AuraRequest>(Json) ?? new AuraRequest()); return true; },
            ["aura.release"] = _ => { Aura.Release(); return true; },

            ["periph.list"] = _ => new { items = PeripheralService.List() },
            ["audio.list"] = _ => AudioService.List(),
            ["audio.setVolume"] = a => AudioService.SetVolume(a.GetProperty("id").GetString() ?? "", Num(a, "volume") is double v ? (int)v : null, a.TryGetProperty("mute", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False ? m.GetBoolean() : null),
            ["audio.setDefault"] = a => AudioService.SetDefault(a.GetProperty("id").GetString() ?? ""),
            ["brand.info"] = a => Brands.Info(a),
            ["brand.apply"] = a => Brands.Apply(a),
            ["mouse.status"] = _ => Mouse.Status(),
            ["mouse.read"] = _ => Mouse.Read(),
            ["mouse.apply"] = a => Mouse.Apply(a),

            ["kb.status"] = _ => Keyboard.Refresh(),
            ["kb.read"] = a => Keyboard.Read(a.ValueKind == JsonValueKind.Object && a.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True),
            ["kb.setLed"] = a => { Keyboard.SetLed(a); return true; },
            ["kb.apply"] = a => Keyboard.Apply(a),

            ["oc.get"] = _ => OcGet(),
            ["oc.gpuApply"] = a => GpuOc.Apply(Num(a, "coreOffset"), Num(a, "memOffset"), Num(a, "powerLimit"), Num(a, "tempLimit")),
            ["oc.gpuReset"] = _ => GpuOc.Reset(),
            ["oc.setPlan"] = a => { PowerService.SetPlan(a.GetProperty("guid").GetString() ?? ""); return true; },
            ["oc.addUltimatePlan"] = _ => { PowerService.AddUltimate(); return true; },
            ["oc.rebootBios"] = _ => { PowerService.RebootToBios(); return true; },
        };
    }

    public void Start()
    {
        Hardware.Start();
        // Lectura previa del teclado en segundo plano: así la página de Periféricos abre al instante.
        Task.Run(async () =>
        {
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(i == 0 ? 4000 : 30000);
                try { Keyboard.Read(); return; } catch { }
            }
        });
        // Diagnóstico: a los pocos segundos de arrancar se guarda una lectura completa junto a los ajustes.
        Task.Run(async () =>
        {
            await Task.Delay(8000);
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CentroDeMando");
                var diag = new { fecha = DateTime.Now, isAdmin = SystemInfo.IsAdmin(), pawnIo = SystemInfo.PawnIoInstalled(), error = Hardware.OpenError, snapshot = Hardware.Last };
                File.WriteAllText(Path.Combine(dir, "diagnostico.json"), JsonSerializer.Serialize(diag, new JsonSerializerOptions(Json) { WriteIndented = true }));
            }
            catch { }
        });
    }

    static double? Num(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    object OcGet() => new
    {
        gpu = GpuOc.Get(),
        power = new { plans = PowerService.Plans() },
        cpu = new { name = _hwInfo.Cpu },
        ram = new { speed = _hwInfo.RamSpeed, modules = _hwInfo.RamModules.Count },
    };

    void RelaunchAsAdmin()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No se pudo localizar el ejecutable.");
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--wait-previous") { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Has cancelado el permiso de administrador.");
        }
        RequestClose?.Invoke();
    }

    public void Emit(string evt, object data)
    {
        try { Post?.Invoke(JsonSerializer.Serialize(new { @event = evt, data }, Json)); } catch { }
    }

    /// <summary>Procesa un mensaje de la interfaz en un hilo de trabajo y responde con el mismo id.</summary>
    public void Handle(string json)
    {
        Task.Run(() =>
        {
            long id = 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
                string cmd = root.GetProperty("cmd").GetString() ?? "";
                var args = root.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Null ? a.Clone() : default;
                if (!_cmds.TryGetValue(cmd, out var fn)) throw new InvalidOperationException("Comando desconocido: " + cmd);
                var result = fn(args);
                Post?.Invoke(JsonSerializer.Serialize(new { id, ok = true, result }, Json));
            }
            catch (Exception ex)
            {
                var e = ex is AggregateException ag && ag.InnerException != null ? ag.InnerException : ex;
                Post?.Invoke(JsonSerializer.Serialize(new { id, ok = false, error = e.Message }, Json));
            }
        });
    }

    /// <summary>Ejecuta un comando de forma directa (modos de diagnóstico).</summary>
    public object? Invoke(string cmd, string argsJson = "{}")
    {
        using var doc = JsonDocument.Parse(argsJson);
        return _cmds[cmd](doc.RootElement.Clone());
    }

    public void Dispose()
    {
        try { Aura.Dispose(); } catch { }
        try { Keyboard.Dispose(); } catch { }
        try { Hardware.Dispose(); } catch { }
    }
}
