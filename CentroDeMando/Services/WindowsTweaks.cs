using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace CentroDeMando.Services;

public sealed record TweakState(string Id, bool? On, bool Available, bool Admin, string Restart, string? Error);

/// <summary>
/// Ajustes de Windows que se pueden activar y desactivar (privacidad, juegos, apariencia, sistema y servicios).
/// Cada ajuste sabe leer su estado real y cambiarlo. Antes de tocar un ajuste por primera vez se guarda su valor
/// original en %APPDATA%\CentroDeMando\windows-backup.json para poder deshacerlo todo.
/// </summary>
public static class WindowsTweaks
{
    sealed class Tweak
    {
        public string Id = "";
        public bool Admin;
        public string Restart = "";                 // "", "explorer", "reboot", "logoff"
        public Func<bool> Available = () => true;
        public Func<bool> Get = () => false;
        public Action<bool> Set = _ => { };
    }

    static readonly RegistryKey CU = Registry.CurrentUser, LM = Registry.LocalMachine;
    static readonly int Build = Environment.OSVersion.Version.Build;
    const string ADV = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string CDM = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    const string PERS = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string SEARCH = @"Software\Microsoft\Windows\CurrentVersion\Search";
    const string CLASSIC = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    /* ───────── Registro ───────── */
    static object? R(RegistryKey hive, string path, string name) { try { using var k = hive.OpenSubKey(path); return k?.GetValue(name); } catch { return null; } }
    static int? D(RegistryKey hive, string path, string name) => R(hive, path, name) switch
    {
        int i => i, long l => (int)l, string s when int.TryParse(s, out var n) => n, _ => null,
    };
    static void W(RegistryKey hive, string path, string name, object v, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        using var k = hive.CreateSubKey(path, true) ?? throw new InvalidOperationException("No se puede escribir en el registro.");
        k.SetValue(name, v, kind);
    }
    static void Del(RegistryKey hive, string path, string name) { using var k = hive.OpenSubKey(path, true); k?.DeleteValue(name, false); }
    static void Flag(RegistryKey hive, string path, string name, bool on, int onValue = 1, int offValue = 0) => W(hive, path, name, on ? onValue : offValue);
    /// <summary>Directiva: si está activada se borra el valor (comportamiento de Windows por defecto).</summary>
    static void Policy(RegistryKey hive, string path, string name, bool featureOn, int blockValue)
    {
        if (featureOn) Del(hive, path, name); else W(hive, path, name, blockValue);
    }

    /* ───────── Avisos a Windows ───────── */
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);
    static void Broadcast(string area) { try { SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, area, 2, 1000, out _); } catch { } }
    static void ShellRefresh() { try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { } Broadcast("ShellState"); }

    [StructLayout(LayoutKind.Sequential)] struct FlagsInfo { public uint Size, Flags; }
    [StructLayout(LayoutKind.Sequential)] struct FilterKeys { public uint Size, Flags, Wait, Delay, Repeat, Bounce; }
    [StructLayout(LayoutKind.Sequential)] struct AnimationInfo { public uint Size; public int MinAnimate; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint p, int[] v, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint p, ref FlagsInfo v, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint p, ref FilterKeys v, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint p, ref AnimationInfo v, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint p, ref int v, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint p, IntPtr v, uint f);
    const uint SPIF = 3;   // guardar en el perfil y avisar a las aplicaciones

    static bool HotkeysOn()
    {
        var s = new FlagsInfo { Size = 8 };
        return SystemParametersInfo(0x3A, 8, ref s, 0) && (s.Flags & 4) != 0;
    }
    static void SetHotkeys(bool on)
    {
        static uint F(uint flags, bool on) => on ? flags | 4 : flags & ~4u;
        var s = new FlagsInfo { Size = 8 };
        if (SystemParametersInfo(0x3A, 8, ref s, 0)) { s.Flags = F(s.Flags, on); SystemParametersInfo(0x3B, 8, ref s, SPIF); }
        var t = new FlagsInfo { Size = 8 };
        if (SystemParametersInfo(0x34, 8, ref t, 0)) { t.Flags = F(t.Flags, on); SystemParametersInfo(0x35, 8, ref t, SPIF); }
        var k = new FilterKeys { Size = 24 };
        if (SystemParametersInfo(0x32, 24, ref k, 0)) { k.Flags = F(k.Flags, on); SystemParametersInfo(0x33, 24, ref k, SPIF); }
    }

    /* ───────── Servicios ───────── */
    static string Sc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return o;
    }
    static int? SvcStart(string name) => D(LM, @"SYSTEM\CurrentControlSet\Services\" + name, "Start");
    static Tweak Service(string id, string def, params string[] names) => new()
    {
        Id = id, Admin = true,
        Available = () => names.Any(n => SvcStart(n) != null),
        Get = () => names.Where(n => SvcStart(n) != null).Any(n => SvcStart(n) != 4),
        Set = on =>
        {
            foreach (var n in names.Where(n => SvcStart(n) != null))
            {
                if (on)
                {
                    Sc($"config {n} start= {def}");
                    if (def.Contains("auto")) Sc("start " + n);
                }
                else
                {
                    var o = Sc($"config {n} start= disabled");
                    if (SvcStart(n) != 4) throw new InvalidOperationException("Windows no ha dejado cambiar el servicio " + n + ". " + o.Trim());
                    Sc("stop " + n);
                }
            }
        },
    };

    static readonly List<Tweak> All = Create();

    static List<Tweak> Create()
    {
        var l = new List<Tweak>();
        void Add(string id, Func<bool> get, Action<bool> set, bool admin = false, string restart = "", Func<bool>? avail = null)
            => l.Add(new Tweak { Id = id, Get = get, Set = set, Admin = admin, Restart = restart, Available = avail ?? (() => true) });

        /* Privacidad */
        Add("telemetry",
            () => D(LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") != 0,
            on => { Policy(LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", on, 0); }, admin: true);
        Add("adId",
            () => D(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") != 0,
            on => Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", on));
        Add("tailored",
            () => D(CU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled") != 0,
            on => Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", on));
        Add("activity",
            () => D(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities") != 0,
            on =>
            {
                foreach (var n in new[] { "PublishUserActivities", "UploadUserActivities", "EnableActivityFeed" })
                    Policy(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", n, on, 0);
            }, admin: true);
        string[] startAds = { "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SubscribedContent-353694Enabled",
                              "SubscribedContent-353696Enabled", "SystemPaneSuggestionsEnabled", "SilentInstalledAppsEnabled", "SoftLandingEnabled" };
        Add("startAds",
            () => startAds.Any(n => D(CU, CDM, n) != 0),
            on =>
            {
                foreach (var n in startAds) Flag(CU, CDM, n, on);
                Flag(CU, ADV, "Start_IrisRecommendations", on);
                Flag(CU, ADV, "ShowSyncProviderNotifications", on);
            });
        Add("lockAds",
            () => D(CU, CDM, "RotatingLockScreenOverlayEnabled") != 0 || D(CU, CDM, "SubscribedContent-338387Enabled") != 0,
            on => { Flag(CU, CDM, "RotatingLockScreenOverlayEnabled", on); Flag(CU, CDM, "SubscribedContent-338387Enabled", on); });
        Add("bingSearch",
            () => D(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions") != 1 && D(CU, SEARCH, "BingSearchEnabled") != 0,
            on => { Policy(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", on, 1); Flag(CU, SEARCH, "BingSearchEnabled", on); },
            restart: "explorer");
        Add("copilot",
            () => D(CU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot") != 1,
            on => { Policy(CU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", on, 1); Flag(CU, ADV, "ShowCopilotButton", on); },
            restart: "explorer", avail: () => Build >= 22000);
        Add("recall",
            () => D(CU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis") != 1,
            on => Policy(CU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", on, 1),
            avail: () => Build >= 26100);
        Add("feedback",
            () => D(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod") != 0,
            on => { if (on) { Del(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod"); Del(CU, @"Software\Microsoft\Siuf\Rules", "PeriodInNanoSeconds"); } else W(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0); });
        Add("location",
            () => R(CU, @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value") as string != "Deny",
            on => W(CU, @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", on ? "Allow" : "Deny", RegistryValueKind.String));

        /* Juegos */
        Add("gameMode",
            () => D(CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") != 0,
            on => Flag(CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on));
        Add("gameDvr",
            () => D(CU, @"System\GameConfigStore", "GameDVR_Enabled") != 0 && D(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") != 0,
            on =>
            {
                Flag(CU, @"System\GameConfigStore", "GameDVR_Enabled", on);
                Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on);
                if (!on) Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", false);
            });
        Add("gameBar",
            () => D(CU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled") != 0,
            on => Flag(CU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", on));
        Add("hags",
            () => D(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") is int v ? v == 2 : Build >= 22621,
            on => Flag(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on, 2, 1),
            admin: true, restart: "reboot", avail: () => Build >= 19041);
        Add("windowedOpt",
            () => (R(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings") as string ?? "").Contains("SwapEffectUpgradeEnable=1"),
            on =>
            {
                var cur = R(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings") as string ?? "";
                var parts = cur.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith("SwapEffectUpgradeEnable=")).ToList();
                parts.Add("SwapEffectUpgradeEnable=" + (on ? 1 : 0));
                W(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", string.Join(";", parts) + ";", RegistryValueKind.String);
            }, avail: () => Build >= 22000);
        Add("mouseAccel",
            () => { var a = new int[3]; return SystemParametersInfo(0x0003, 0, a, 0) && a[2] != 0; },
            on => { var a = on ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 }; if (!SystemParametersInfo(0x0004, 0, a, SPIF)) throw new InvalidOperationException("Windows no ha aceptado el cambio del ratón."); });
        Add("accessKeys", HotkeysOn, SetHotkeys);

        /* Apariencia y escritorio */
        Add("darkMode",
            () => D(CU, PERS, "AppsUseLightTheme") == 0,
            on => { Flag(CU, PERS, "AppsUseLightTheme", !on); Flag(CU, PERS, "SystemUsesLightTheme", !on); Broadcast("ImmersiveColorSet"); });
        Add("transparency",
            () => D(CU, PERS, "EnableTransparency") != 0,
            on => { Flag(CU, PERS, "EnableTransparency", on); Broadcast("ImmersiveColorSet"); });
        Add("animations",
            () => { int v = 1; SystemParametersInfo(0x1042, 0, ref v, 0); return v != 0; },
            on =>
            {
                SystemParametersInfo(0x1043, 0, (IntPtr)(on ? 1 : 0), SPIF);
                var a = new AnimationInfo { Size = 8, MinAnimate = on ? 1 : 0 };
                SystemParametersInfo(0x0049, 8, ref a, SPIF);
                Flag(CU, ADV, "TaskbarAnimations", on);
            });
        Add("fileExt",
            () => D(CU, ADV, "HideFileExt") == 0,
            on => { Flag(CU, ADV, "HideFileExt", !on); ShellRefresh(); });
        Add("hiddenFiles",
            () => D(CU, ADV, "Hidden") == 1,
            on => { Flag(CU, ADV, "Hidden", on, 1, 2); ShellRefresh(); });
        Add("launchThisPc",
            () => D(CU, ADV, "LaunchTo") == 1,
            on => Flag(CU, ADV, "LaunchTo", on, 1, 2));
        Add("classicMenu",
            () => { try { using var k = CU.OpenSubKey(CLASSIC + @"\InprocServer32"); return k != null; } catch { return false; } },
            on =>
            {
                if (on) { using var k = CU.CreateSubKey(CLASSIC + @"\InprocServer32", true); k.SetValue("", "", RegistryValueKind.String); }
                else CU.DeleteSubKeyTree(CLASSIC, false);
            }, restart: "explorer", avail: () => Build >= 22000);
        Add("taskbarLeft",
            () => D(CU, ADV, "TaskbarAl") == 0,
            on => { Flag(CU, ADV, "TaskbarAl", !on); Broadcast("TraySettings"); }, avail: () => Build >= 22000);
        Add("taskView",
            () => D(CU, ADV, "ShowTaskViewButton") != 0,
            on => { Flag(CU, ADV, "ShowTaskViewButton", on); Broadcast("TraySettings"); });
        Add("searchBox",
            () => D(CU, SEARCH, "SearchboxTaskbarMode") != 0,
            on => { Flag(CU, SEARCH, "SearchboxTaskbarMode", on, 1, 0); Broadcast("TraySettings"); });
        Add("widgets",
            () => D(LM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests") != 0 && D(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds") != 0,
            on => { Policy(LM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", on, 0); Policy(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", on, 0); },
            admin: true, restart: "explorer");
        Add("endTask",
            () => D(CU, ADV + @"\TaskbarDeveloperSettings", "TaskbarEndTask") == 1,
            on => Flag(CU, ADV + @"\TaskbarDeveloperSettings", "TaskbarEndTask", on), restart: "explorer", avail: () => Build >= 22631);
        Add("secondsClock",
            () => D(CU, ADV, "ShowSecondsInSystemClock") == 1,
            on => { Flag(CU, ADV, "ShowSecondsInSystemClock", on); Broadcast("TraySettings"); }, restart: "explorer", avail: () => Build >= 22621);

        /* Sistema */
        Add("hibernate",
            () => D(LM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 1,
            on => PowerService.Run("powercfg.exe", "/hibernate " + (on ? "on" : "off")), admin: true);
        Add("fastStartup",
            () => D(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") == 1,
            on => Flag(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", on), admin: true);
        Add("startupDelay",
            () => D(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec") != 0,
            on => { if (on) Del(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec"); else W(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0); });
        Add("notifications",
            () => D(CU, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") != 0,
            on => Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", on), restart: "explorer");
        Add("bgApps",
            () => D(CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") != 1,
            on => Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", !on));
        Add("clipboard",
            () => D(CU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory") == 1,
            on => Flag(CU, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", on));
        Add("storageSense",
            () => D(CU, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01") == 1,
            on => Flag(CU, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", on));
        Add("autoReboot",
            () => D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers") != 1,
            on => Policy(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", on, 1), admin: true);
        Add("p2pUpdates",
            () => D(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode") != 0,
            on => Policy(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", on, 0), admin: true);
        Add("driverUpdates",
            () => D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate") != 1,
            on => Policy(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", on, 1), admin: true);

        /* Servicios */
        l.Add(Service("svc.diagtrack", "auto", "DiagTrack", "dmwappushservice"));
        l.Add(Service("svc.sysmain", "auto", "SysMain"));
        l.Add(Service("svc.wsearch", "delayed-auto", "WSearch"));
        l.Add(Service("svc.spooler", "auto", "Spooler"));
        l.Add(Service("svc.fax", "demand", "Fax"));
        l.Add(Service("svc.wer", "demand", "WerSvc"));
        l.Add(Service("svc.maps", "delayed-auto", "MapsBroker"));
        l.Add(Service("svc.xbox", "demand", "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"));
        l.Add(Service("svc.geo", "demand", "lfsvc"));
        l.Add(Service("svc.retail", "demand", "RetailDemo"));
        l.Add(Service("svc.remotereg", "demand", "RemoteRegistry"));
        return l;
    }

    /* ───────── Copia de los valores originales ───────── */
    static readonly object _lock = new();
    static string BackupFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CentroDeMando", "windows-backup.json");
    static Dictionary<string, bool> LoadBackup()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(BackupFile)) ?? new(); } catch { return new(); }
    }
    static void SaveBackup(Dictionary<string, bool> b)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!); File.WriteAllText(BackupFile, JsonSerializer.Serialize(b)); } catch { }
    }

    static TweakState State(Tweak t)
    {
        bool avail = false; bool? on = null; string? err = null;
        try { avail = t.Available(); if (avail) on = t.Get(); } catch (Exception ex) { err = ex.Message; }
        return new TweakState(t.Id, on, avail, t.Admin, t.Restart, err);
    }

    public static object List()
    {
        lock (_lock) return new { items = All.Select(State).ToList(), backup = LoadBackup().Count };
    }

    static Tweak Find(string id) => All.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("Ajuste desconocido: " + id);

    public static TweakState Set(string id, bool on)
    {
        lock (_lock)
        {
            var t = Find(id);
            if (t.Admin && !SystemInfo.IsAdmin()) throw new InvalidOperationException("Este ajuste necesita abrir la app como administrador.");
            if (!t.Available()) throw new InvalidOperationException("Este ajuste no está disponible en tu versión de Windows.");
            var b = LoadBackup();
            if (!b.ContainsKey(id)) { b[id] = t.Get(); SaveBackup(b); }
            try { t.Set(on); }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException("Windows no ha dado permiso para cambiar este ajuste."); }
            catch (System.Security.SecurityException) { throw new InvalidOperationException("Windows no ha dado permiso para cambiar este ajuste."); }
            return State(t);
        }
    }

    /// <summary>Vuelve a dejar todos los ajustes cambiados por la app como estaban.</summary>
    public static object Restore()
    {
        lock (_lock)
        {
            var b = LoadBackup(); var failed = new List<string>(); int done = 0;
            bool admin = SystemInfo.IsAdmin();
            foreach (var (id, orig) in b.ToList())
            {
                var t = All.FirstOrDefault(x => x.Id == id);
                if (t == null) { b.Remove(id); continue; }
                if (t.Admin && !admin) { failed.Add(id); continue; }
                try { if (t.Get() != orig) t.Set(orig); b.Remove(id); done++; } catch { failed.Add(id); }
            }
            SaveBackup(b);
            return new { restored = done, failed };
        }
    }

    /* ───────── Información y herramientas ───────── */
    public static object Info()
    {
        string caption = "Windows", version = "", build = Build.ToString(), name = Environment.MachineName;
        DateTime? installed = null, boot = null;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Caption, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
            foreach (var o in s.Get())
            {
                caption = (o["Caption"]?.ToString() ?? caption).Replace("Microsoft ", "").Trim();
                try { installed = ManagementDateTimeConverter.ToDateTime(o["InstallDate"]?.ToString()); } catch { }
                try { boot = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"]?.ToString()); } catch { }
            }
        }
        catch { }
        const string NT = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        version = R(LM, NT, "DisplayVersion") as string ?? R(LM, NT, "ReleaseId") as string ?? "";
        if (D(LM, NT, "UBR") is int ubr) build += "." + ubr;
        return new
        {
            caption, version, build, name,
            installed = installed?.ToString("o"),
            uptimeSec = Environment.TickCount64 / 1000,
            boot = boot?.ToString("o"),
        };
    }

    static readonly Dictionary<string, (string File, string Args)> Tools = new()
    {
        ["devmgmt"] = ("devmgmt.msc", ""), ["services"] = ("services.msc", ""), ["msconfig"] = ("msconfig.exe", ""),
        ["regedit"] = ("regedit.exe", ""), ["control"] = ("control.exe", ""), ["taskmgr"] = ("taskmgr.exe", ""),
        ["eventvwr"] = ("eventvwr.msc", ""), ["resmon"] = ("resmon.exe", ""), ["cleanmgr"] = ("cleanmgr.exe", ""),
        ["diskmgmt"] = ("diskmgmt.msc", ""), ["reliability"] = ("perfmon.exe", "/rel"), ["msinfo"] = ("msinfo32.exe", ""),
        ["sysprops"] = ("SystemPropertiesAdvanced.exe", ""), ["netconn"] = ("control.exe", "ncpa.cpl"), ["sound"] = ("control.exe", "mmsys.cpl"),
        ["update"] = ("ms-settings:windowsupdate", ""), ["apps"] = ("ms-settings:appsfeatures", ""), ["graphics"] = ("ms-settings:display-advancedgraphics", ""),
        ["restore"] = ("SystemPropertiesProtection.exe", ""), ["power"] = ("control.exe", "powercfg.cpl"),
    };

    public static void OpenTool(string id)
    {
        if (!Tools.TryGetValue(id, out var t)) throw new ArgumentException("Herramienta desconocida.");
        Process.Start(new ProcessStartInfo(t.File, t.Args) { UseShellExecute = true });
    }

    /// <summary>Abre una consola de administrador que comprueba y repara los archivos de Windows.</summary>
    public static void Repair()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k sfc /scannow & DISM /Online /Cleanup-Image /RestoreHealth") { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Has cancelado el permiso de administrador."); }
    }

    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } finally { p.Dispose(); } }
        // Windows vuelve a abrir el Explorador solo; si no lo hace, se abre sin permisos de administrador.
        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(250);
            var ps = Process.GetProcessesByName("explorer"); bool any = ps.Length > 0; foreach (var p in ps) p.Dispose();
            if (any) return;
        }
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        try { Process.Start(new ProcessStartInfo("runas.exe", $"/trustlevel:0x20000 \"{exe}\"") { UseShellExecute = false, CreateNoWindow = true }); }
        catch { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
    }

    /// <summary>Crea un punto de restauración del sistema (necesita la protección del sistema activada en C:).</summary>
    public static void CreateRestorePoint()
    {
        if (!SystemInfo.IsAdmin()) throw new InvalidOperationException("Para crear un punto de restauración hay que abrir la app como administrador.");
        const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        var prev = R(LM, key, "SystemRestorePointCreationFrequency");
        try { W(LM, key, "SystemRestorePointCreationFrequency", 0); } catch { }
        try
        {
            using var mc = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            var inp = mc.GetMethodParameters("CreateRestorePoint");
            inp["Description"] = "Centro de Mando";
            inp["RestorePointType"] = 12;   // MODIFY_SETTINGS
            inp["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
            var outp = mc.InvokeMethod("CreateRestorePoint", inp, null);
            uint rv = Convert.ToUInt32(outp["ReturnValue"]);
            if (rv == 1058 || rv == 0x80070422) throw new InvalidOperationException("La protección del sistema está desactivada. Actívala en Protección del sistema y vuelve a intentarlo.");
            if (rv != 0) throw new InvalidOperationException($"Windows no ha podido crear el punto de restauración (código {rv}).");
        }
        catch (ManagementException) { throw new InvalidOperationException("La protección del sistema está desactivada. Actívala en Protección del sistema y vuelve a intentarlo."); }
        finally
        {
            try { if (prev is int p) W(LM, key, "SystemRestorePointCreationFrequency", p); else Del(LM, key, "SystemRestorePointCreationFrequency"); } catch { }
        }
    }
}
