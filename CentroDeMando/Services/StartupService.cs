using System.Diagnostics;
using System.Drawing.Imaging;
using Microsoft.Win32;

namespace CentroDeMando.Services;

public sealed record StartupItem(string Id, string Name, string Command, string Location, bool Enabled, bool Admin,
    string? Publisher, string? Description, string? Path, string? Icon);

/// <summary>
/// Programas que se abren al iniciar Windows (claves Run y carpetas de Inicio). Se activan y desactivan igual que
/// en el Administrador de tareas (claves StartupApproved), así que no se borra nada.
/// </summary>
public static class StartupService
{
    const string AP = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    sealed record Loc(string Id, RegistryKey Hive, string? RunPath, string? Folder, string Approved, bool Admin);

    static IEnumerable<Loc> Locations()
    {
        yield return new("user", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", null, AP + "Run", false);
        yield return new("machine", Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", null, AP + "Run", true);
        yield return new("machine32", Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", null, AP + "Run32", true);
        yield return new("folder", Registry.CurrentUser, null, Environment.GetFolderPath(Environment.SpecialFolder.Startup), AP + "StartupFolder", false);
        yield return new("folderAll", Registry.LocalMachine, null, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), AP + "StartupFolder", true);
    }

    static readonly Dictionary<string, (string? Pub, string? Desc, string? Icon)> _meta = new(StringComparer.OrdinalIgnoreCase);

    static bool IsEnabled(RegistryKey hive, string approved, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(approved);
            return k?.GetValue(name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;
        }
        catch { return true; }
    }

    /// <summary>Saca la ruta del ejecutable de una línea de órdenes («"C:\x\a.exe" --min», C:\x\a.exe /b…).</summary>
    public static string? ExePath(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : null; }
        var parts = cmd.Split(' ');
        for (int i = parts.Length; i >= 1; i--)
        {
            var p = string.Join(' ', parts.Take(i));
            if (File.Exists(p)) return p;
            if (File.Exists(p + ".exe")) return p + ".exe";
        }
        return parts.Length > 0 && parts[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? parts[0] : null;
    }

    static string? ShortcutTarget(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell"); if (t == null) return null;
            dynamic sh = Activator.CreateInstance(t)!;
            dynamic sc = sh.CreateShortcut(lnk);
            string target = sc.TargetPath, args = sc.Arguments;
            return string.IsNullOrWhiteSpace(target) ? null : string.IsNullOrWhiteSpace(args) ? $"\"{target}\"" : $"\"{target}\" {args}";
        }
        catch { return null; }
    }

    static (string? Pub, string? Desc, string? Icon) Meta(string? path)
    {
        if (string.IsNullOrEmpty(path)) return (null, null, null);
        lock (_meta)
        {
            if (_meta.TryGetValue(path, out var m)) return m;
            string? pub = null, desc = null, ico = null;
            try { if (File.Exists(path)) { var v = FileVersionInfo.GetVersionInfo(path); pub = v.CompanyName?.Trim(); desc = v.FileDescription?.Trim(); } } catch { }
            try
            {
                if (File.Exists(path))
                {
                    using var icon = Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        using var bmp = new Bitmap(icon.ToBitmap(), 32, 32);
                        using var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png);
                        ico = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { }
            return _meta[path] = (string.IsNullOrEmpty(pub) ? null : pub, string.IsNullOrEmpty(desc) ? null : desc, ico);
        }
    }

    public static List<StartupItem> List()
    {
        var list = new List<StartupItem>();
        foreach (var loc in Locations())
        {
            try
            {
                if (loc.RunPath != null)
                {
                    using var k = loc.Hive.OpenSubKey(loc.RunPath);
                    if (k == null) continue;
                    foreach (var name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name) || k.GetValue(name) is not string cmd || string.IsNullOrWhiteSpace(cmd)) continue;
                        var exe = ExePath(cmd); var m = Meta(exe);
                        list.Add(new StartupItem($"{loc.Id}|{name}", name, cmd, loc.Id, IsEnabled(loc.Hive, loc.Approved, name), loc.Admin, m.Pub, m.Desc, exe, m.Icon));
                    }
                }
                else if (loc.Folder != null && Directory.Exists(loc.Folder))
                {
                    foreach (var f in Directory.EnumerateFiles(loc.Folder))
                    {
                        var file = System.IO.Path.GetFileName(f);
                        if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        var cmd = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) ?? f : f;
                        var exe = ExePath(cmd) ?? f; var m = Meta(exe);
                        list.Add(new StartupItem($"{loc.Id}|{file}", System.IO.Path.GetFileNameWithoutExtension(f), cmd, loc.Id,
                            IsEnabled(loc.Hive, loc.Approved, file), loc.Admin, m.Pub, m.Desc, exe, m.Icon));
                    }
                }
            }
            catch { }
        }
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var (loc, name) = Parse(id);
        if (loc.Admin && !SystemInfo.IsAdmin()) throw new InvalidOperationException("Este programa se inicia para todos los usuarios: hace falta abrir la app como administrador.");
        var data = new byte[12];
        if (enabled) data[0] = 2;
        else { data[0] = 3; BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4); }
        try
        {
            using var k = loc.Hive.CreateSubKey(loc.Approved, true);
            k.SetValue(name, data, RegistryValueKind.Binary);
        }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("Windows no ha dado permiso para cambiar este programa de inicio."); }
    }

    public static void Reveal(string id)
    {
        var item = List().FirstOrDefault(i => i.Id == id) ?? throw new ArgumentException("No se ha encontrado el programa.");
        var p = item.Path;
        if (p == null || !File.Exists(p)) throw new InvalidOperationException("No se encuentra el archivo de este programa.");
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
    }

    static (Loc, string) Parse(string id)
    {
        int i = id.IndexOf('|');
        if (i <= 0) throw new ArgumentException("Programa no válido.");
        var loc = Locations().FirstOrDefault(l => l.Id == id[..i]) ?? throw new ArgumentException("Programa no válido.");
        return (loc, id[(i + 1)..]);
    }
}
