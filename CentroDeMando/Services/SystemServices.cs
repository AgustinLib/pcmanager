using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace CentroDeMando.Services;

public sealed record RamModule(string Slot, string Maker, string Part, double SizeGb, int Speed);
public sealed record HardwareInfo(string Cpu, string Gpu, string Board, string Bios, double RamTotalGb, int RamSpeed, List<RamModule> RamModules);

public static class SystemInfo
{
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool PawnIoInstalled()
    {
        try
        {
            return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll"));
        }
        catch { return false; }
    }

    static IEnumerable<ManagementBaseObject> Query(string q)
    {
        using var s = new ManagementObjectSearcher(q);
        foreach (var o in s.Get()) yield return o;
    }
    static string S(ManagementBaseObject o, string p) => (o[p]?.ToString() ?? "").Trim();

    public static HardwareInfo Read()
    {
        string cpu = "", gpu = "", board = "", bios = "";
        var mods = new List<RamModule>();
        try { cpu = Query("SELECT Name FROM Win32_Processor").Select(o => S(o, "Name")).FirstOrDefault() ?? ""; } catch { }
        try
        {
            var gpus = Query("SELECT Name FROM Win32_VideoController").Select(o => S(o, "Name")).ToList();
            gpu = gpus.FirstOrDefault(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) ?? gpus.FirstOrDefault() ?? "";
        }
        catch { }
        try
        {
            var b = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
            if (b != null)
            {
                string m = S(b, "Manufacturer");
                if (m.StartsWith("ASUSTeK", StringComparison.OrdinalIgnoreCase)) m = "ASUS";
                board = (m + " " + S(b, "Product")).Trim();
            }
        }
        catch { }
        try { bios = Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS").Select(o => S(o, "SMBIOSBIOSVersion")).FirstOrDefault() ?? ""; } catch { }
        try
        {
            foreach (var m in Query("SELECT DeviceLocator, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
            {
                double gb = 0; int speed = 0;
                try { gb = Convert.ToUInt64(m["Capacity"]) / 1073741824.0; } catch { }
                try { speed = Convert.ToInt32(m["ConfiguredClockSpeed"] ?? m["Speed"]); } catch { }
                mods.Add(new RamModule(S(m, "DeviceLocator"), S(m, "Manufacturer"), S(m, "PartNumber"), Math.Round(gb, 0), speed));
            }
        }
        catch { }
        return new HardwareInfo(cpu, gpu, board, bios, mods.Sum(m => m.SizeGb), mods.Count > 0 ? mods.Max(m => m.Speed) : 0, mods);
    }
}

public sealed record ProcGroup(string Name, string Desc, int Count, double Cpu, double MemMb, List<int> Pids, bool Protected);
public sealed record KillFail(int Pid, string Error);

public sealed class ProcessService
{
    static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "taskhostw", "ctfmon", "RuntimeBroker", "SearchHost", "SearchIndexer",
        "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "conhost", "audiodg", "spoolsv", "MsMpEng", "NisSrv",
        "SecurityHealthService", "SecurityHealthSystray", "WmiPrvSE", "dllhost", "TextInputHost", "ApplicationFrameHost",
        "SystemSettings", "UserOOBEBroker", "WUDFHost", "dasHost", "LsaIso", "MpDefenderCoreService", "smartscreen",
        "CentroDeMando", "claude", "msedgewebview2", "nvcontainer", "NVDisplay.Container", "LightingService", "iot_driver_v187",
    };

    readonly Dictionary<int, (TimeSpan cpu, DateTime at)> _prev = new();
    readonly Dictionary<string, string> _desc = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();

    public static bool IsProtected(Process p)
    {
        try
        {
            if (p.Id <= 4 || p.Id == Environment.ProcessId) return true;
            if (Critical.Contains(p.ProcessName) || p.ProcessName.StartsWith("iot_driver", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.SessionId == 0) return true;       // servicios del sistema
        }
        catch { return true; }
        return false;
    }

    public List<ProcGroup> List()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            int cpus = Environment.ProcessorCount;
            var groups = new Dictionary<string, (string desc, int count, double cpu, double mem, List<int> pids, bool prot)>(StringComparer.OrdinalIgnoreCase);
            var alive = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    alive.Add(p.Id);
                    double mem = 0, cpu = 0;
                    try { mem = p.WorkingSet64 / 1048576.0; } catch { }
                    try
                    {
                        var t = p.TotalProcessorTime;
                        if (_prev.TryGetValue(p.Id, out var pv))
                        {
                            double dt = (now - pv.at).TotalSeconds;
                            if (dt > 0.2) cpu = Math.Clamp((t - pv.cpu).TotalSeconds / dt / cpus * 100, 0, 100);
                        }
                        _prev[p.Id] = (t, now);
                    }
                    catch { }
                    string name = p.ProcessName;
                    bool prot = IsProtected(p);
                    if (!groups.TryGetValue(name, out var g))
                    {
                        if (!_desc.TryGetValue(name, out var d))
                        {
                            d = "";
                            if (!prot) { try { d = p.MainModule?.FileVersionInfo.FileDescription ?? ""; } catch { } }
                            _desc[name] = d;
                        }
                        g = (d, 0, 0, 0, new List<int>(), prot);
                    }
                    g.count++; g.cpu += cpu; g.mem += mem; g.pids.Add(p.Id); g.prot |= prot;
                    groups[name] = g;
                }
                catch { }
                finally { p.Dispose(); }
            }
            foreach (var dead in _prev.Keys.Where(k => !alive.Contains(k)).ToList()) _prev.Remove(dead);
            return groups.Select(kv => new ProcGroup(kv.Key, kv.Value.desc, kv.Value.count, Math.Round(kv.Value.cpu, 1),
                    Math.Round(kv.Value.mem, 0), kv.Value.pids, kv.Value.prot))
                .OrderByDescending(g => g.MemMb).ToList();
        }
    }

    public (int killed, List<KillFail> failed) Kill(IEnumerable<int> pids)
    {
        int killed = 0; var failed = new List<KillFail>();
        foreach (int pid in pids.Distinct())
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (IsProtected(p)) { failed.Add(new KillFail(pid, "Proceso del sistema protegido")); continue; }
                p.Kill();
                killed++;
            }
            catch (ArgumentException) { }                 // ya no existe
            catch (Exception ex) { failed.Add(new KillFail(pid, ex.Message)); }
        }
        return (killed, failed);
    }

    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>Pide a Windows que recorte la memoria en uso de los procesos accesibles.</summary>
    public double TrimRam()
    {
        double before = HardwareService.AvailableMb();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.Id > 4 && p.Id != Environment.ProcessId) EmptyWorkingSet(p.Handle); } catch { }
            finally { p.Dispose(); }
        }
        Thread.Sleep(400);
        return Math.Max(0, Math.Round(HardwareService.AvailableMb() - before, 0));
    }
}

public sealed record PowerPlan(string Guid, string Name, bool Active);

public static class PowerService
{
    static string Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        string e = p.StandardError.ReadToEnd();
        p.WaitForExit(8000);
        if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(o)) throw new InvalidOperationException(string.IsNullOrWhiteSpace(e) ? $"Código de salida {p.ExitCode}" : e.Trim());
        return o;
    }

    public static List<PowerPlan> Plans()
    {
        var list = new List<PowerPlan>();
        try
        {
            foreach (Match m in Regex.Matches(Run("powercfg.exe", "/list"), @"([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})\s+\((.+?)\)\s*(\*)?"))
                list.Add(new PowerPlan(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success));
        }
        catch { }
        return list;
    }

    public static void SetPlan(string guid)
    {
        if (!System.Guid.TryParse(guid, out var g)) throw new ArgumentException("Plan no válido.");
        Run("powercfg.exe", "/setactive " + g);
    }

    public static void AddUltimate()
    {
        if (Plans().Any(p => p.Name.Contains("ximo rendimiento", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))) return;
        Run("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
    }

    public static void RebootToBios()
    {
        if (!SystemInfo.IsAdmin()) throw new InvalidOperationException("Hace falta abrir la app como administrador para reiniciar a la BIOS.");
        Run("shutdown.exe", "/r /fw /t 5");
    }
}
