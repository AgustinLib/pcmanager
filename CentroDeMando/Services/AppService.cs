using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text.Json;

namespace CentroDeMando.Services;

/// <summary>Opciones de la propia app: iniciar con Windows, buscar actualizaciones y carpeta de datos.</summary>
public static class AppService
{
    const string TaskName = "CentroDeMando";
    public const string Repo = "AgustinLib/pcmanager";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static AppService() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("CentroDeMando"); }

    static (int Code, string Out) Schtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, o.Trim());
    }

    public static bool AutostartEnabled() => Schtasks($"/query /tn {TaskName}").Code == 0;

    /// <summary>
    /// Se usa una tarea programada «al iniciar sesión» con los permisos más altos: así la app arranca ya como
    /// administrador (temperaturas y ventiladores) sin que Windows pregunte cada vez.
    /// </summary>
    public static void SetAutostart(bool on)
    {
        if (!SystemInfo.IsAdmin()) throw new InvalidOperationException("Para iniciar con Windows hay que abrir la app como administrador.");
        if (!on) { if (AutostartEnabled()) Schtasks($"/delete /tn {TaskName} /f"); return; }
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No se pudo localizar el ejecutable.");
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Centro de Mando</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>6</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{SecurityElement.Escape(exe)}"</Command><Arguments>--minimized</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "cdm-task.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            var (code, o) = Schtasks($"/create /tn {TaskName} /xml \"{file}\" /f");
            if (code != 0) throw new InvalidOperationException("Windows no ha podido crear la tarea de inicio. " + o);
        }
        finally { try { File.Delete(file); } catch { } }
    }

    /// <summary>Si la app se ha movido de carpeta, actualiza la tarea de inicio para que apunte al nuevo sitio.</summary>
    public static void FixAutostartPath()
    {
        try
        {
            if (!SystemInfo.IsAdmin()) return;
            var (code, o) = Schtasks($"/query /tn {TaskName} /xml");
            if (code != 0) return;
            var exe = Environment.ProcessPath ?? "";
            if (!o.Contains(SecurityElement.Escape(exe) ?? exe, StringComparison.OrdinalIgnoreCase)) SetAutostart(true);
        }
        catch { }
    }

    public static async Task<object> CheckUpdate(string current)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if ((int)resp.StatusCode == 404) return new { latest = (string?)null, newer = false, url = $"https://github.com/{Repo}/releases" };
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        string tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string url = r.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : $"https://github.com/{Repo}/releases";
        bool newer = Version.TryParse(tag.TrimStart('v', 'V'), out var lv) && Version.TryParse(current, out var cv) && lv > cv;
        return new { latest = tag.TrimStart('v', 'V'), newer, url };
    }

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CentroDeMando");
    public static void OpenDataDir() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataDir}\"") { UseShellExecute = true });

    /// <summary>Textos del icono de la bandeja y de los avisos (fuera de la interfaz web).</summary>
    public static (string Open, string Exit, string HotTitle, string Hidden) Texts(string? lang) => lang switch
    {
        "en" => ("Open", "Exit", "High temperature", "Centro de Mando is still running in the background."),
        "fr" => ("Ouvrir", "Quitter", "Température élevée", "Centro de Mando continue de fonctionner en arrière-plan."),
        "de" => ("Öffnen", "Beenden", "Hohe Temperatur", "Centro de Mando läuft im Hintergrund weiter."),
        "it" => ("Apri", "Esci", "Temperatura elevata", "Centro de Mando continua a funzionare in background."),
        "pt" => ("Abrir", "Sair", "Temperatura elevada", "O Centro de Mando continua a funcionar em segundo plano."),
        "ca" => ("Obre", "Surt", "Temperatura alta", "Centro de Mando continua funcionant en segon pla."),
        "pl" => ("Otwórz", "Zakończ", "Wysoka temperatura", "Centro de Mando nadal działa w tle."),
        "ru" => ("Открыть", "Выход", "Высокая температура", "Centro de Mando продолжает работать в фоне."),
        "zh" => ("打开", "退出", "温度过高", "Centro de Mando 仍在后台运行。"),
        _ => ("Abrir", "Salir", "Temperatura alta", "Centro de Mando sigue funcionando en segundo plano."),
    };
}
