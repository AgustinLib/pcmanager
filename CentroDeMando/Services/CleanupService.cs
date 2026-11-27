using System.Runtime.InteropServices;

namespace CentroDeMando.Services;

public sealed record CleanCat(string Id, long Bytes, int Files, bool Admin, bool Available);
public sealed record DriveSpace(string Name, string Label, string Format, long Total, long Free, bool System);

/// <summary>
/// Limpieza de archivos que sobran: temporales, cachés, informes de errores y papelera. Solo borra dentro de carpetas
/// de caché conocidas y se salta los archivos que estén en uso.
/// </summary>
public static class CleanupService
{
    sealed record Cat(string Id, bool Admin, Func<IEnumerable<string>> Dirs, string Pattern = "*", bool Recurse = true, TimeSpan? MinAge = null);

    static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
    static string Local => Env(Environment.SpecialFolder.LocalApplicationData);
    static string Roaming => Env(Environment.SpecialFolder.ApplicationData);
    static string Win => Env(Environment.SpecialFolder.Windows);
    static string ProgData => Env(Environment.SpecialFolder.CommonApplicationData);
    static string LocalLow => Path.Combine(Env(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");

    static IEnumerable<string> BrowserCaches()
    {
        string[] chromium =
        {
            Path.Combine(Local, @"Google\Chrome\User Data"), Path.Combine(Local, @"Microsoft\Edge\User Data"),
            Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data"), Path.Combine(Local, @"Vivaldi\User Data"),
        };
        foreach (var b in chromium.Where(Directory.Exists))
        {
            yield return Path.Combine(b, "ShaderCache"); yield return Path.Combine(b, "GrShaderCache");
            IEnumerable<string> profiles;
            try { profiles = Directory.EnumerateDirectories(b).Where(d => { var n = Path.GetFileName(d); return n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal); }).ToList(); }
            catch { continue; }
            foreach (var p in profiles)
                foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" }) yield return Path.Combine(p, c);
        }
        foreach (var o in new[] { "Opera Stable", "Opera GX Stable" })
        {
            yield return Path.Combine(Local, "Opera Software", o, "Cache");
            yield return Path.Combine(Roaming, "Opera Software", o, "GPUCache");
        }
        var ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ff))
        {
            List<string> prof; try { prof = Directory.EnumerateDirectories(ff).ToList(); } catch { prof = new(); }
            foreach (var p in prof) yield return Path.Combine(p, "cache2");
        }
    }

    static readonly Cat[] Cats =
    {
        new("temp", false, () => new[] { Path.GetTempPath() }, MinAge: TimeSpan.FromDays(1)),
        new("winTemp", true, () => new[] { Path.Combine(Win, "Temp") }, MinAge: TimeSpan.FromDays(1)),
        new("shaders", false, () => new[]
        {
            Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"),
            Path.Combine(LocalLow, @"NVIDIA\PerDriverVersion\DXCache"), Path.Combine(Local, @"AMD\DxCache"), Path.Combine(Local, @"AMD\DxcCache"),
            Path.Combine(Local, @"AMD\GLCache"), Path.Combine(Local, @"AMD\VkCache"), Path.Combine(LocalLow, @"Intel\ShaderCache"),
        }),
        new("browser", false, BrowserCaches),
        new("thumbs", false, () => new[] { Path.Combine(Local, @"Microsoft\Windows\Explorer") }, "thumbcache_*.db", Recurse: false),
        new("crash", false, () => new[]
        {
            Path.Combine(Local, "CrashDumps"), Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(Win, "Minidump"), Path.Combine(Win, "LiveKernelReports"),
        }),
        new("wuCache", true, () => new[] { Path.Combine(Win, @"SoftwareDistribution\Download") }),
        new("doCache", true, () => new[] { Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }),
    };

    static readonly EnumerationOptions Opts = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
    static readonly EnumerationOptions Flat = new() { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    static IEnumerable<FileInfo> Files(Cat c)
    {
        var limit = c.MinAge is TimeSpan a ? DateTime.UtcNow - a : DateTime.MaxValue;
        foreach (var d in c.Dirs())
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
            IEnumerable<FileInfo> files;
            try { files = new DirectoryInfo(d).EnumerateFiles(c.Pattern, c.Recurse ? Opts : Flat); } catch { continue; }
            using var en = files.GetEnumerator();
            while (true)
            {
                FileInfo f;
                try { if (!en.MoveNext()) break; f = en.Current; } catch { break; }
                if (c.MinAge != null && f.LastWriteTimeUtc > limit) continue;
                yield return f;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct RbInfo { public int Size; public long Bytes; public long Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string? root, ref RbInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    public static object Scan()
    {
        bool admin = SystemInfo.IsAdmin();
        var res = new List<CleanCat>();
        foreach (var c in Cats)
        {
            long bytes = 0; int n = 0; bool any = c.Dirs().Any(Directory.Exists);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (any && (admin || !c.Admin))
                foreach (var f in Files(c)) { try { bytes += f.Length; n++; } catch { } if (sw.ElapsedMilliseconds > 8000) break; }
            res.Add(new CleanCat(c.Id, bytes, n, c.Admin, any));
        }
        var rb = new RbInfo { Size = Marshal.SizeOf<RbInfo>() };
        try { SHQueryRecycleBin(null, ref rb); } catch { }
        res.Add(new CleanCat("recycle", rb.Bytes, (int)Math.Min(int.MaxValue, rb.Items), false, true));
        return new { items = res, drives = Drives() };
    }

    public static List<DriveSpace> Drives()
    {
        var sys = Path.GetPathRoot(Win) ?? "C:\\";
        var list = new List<DriveSpace>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                list.Add(new DriveSpace(d.Name.TrimEnd('\\'), d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace, string.Equals(d.Name, sys, StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }
        return list;
    }

    public static object Clean(IEnumerable<string> ids)
    {
        bool admin = SystemInfo.IsAdmin();
        long freed = 0; int deleted = 0, skipped = 0;
        var set = new HashSet<string>(ids);
        foreach (var c in Cats.Where(c => set.Contains(c.Id)))
        {
            if (c.Admin && !admin) continue;
            foreach (var f in Files(c).ToList())
            {
                try
                {
                    long len = f.Length;
                    if (f.IsReadOnly) f.IsReadOnly = false;
                    f.Delete(); freed += len; deleted++;
                }
                catch { skipped++; }
            }
            if (c.Recurse)
                foreach (var d in c.Dirs().Where(Directory.Exists)) RemoveEmptyDirs(d, c.MinAge);
        }
        if (set.Contains("recycle"))
        {
            var rb = new RbInfo { Size = Marshal.SizeOf<RbInfo>() };
            try { SHQueryRecycleBin(null, ref rb); } catch { }
            try { if (rb.Items > 0 && SHEmptyRecycleBin(IntPtr.Zero, null, 0x7) == 0) { freed += rb.Bytes; deleted += (int)rb.Items; } } catch { }
        }
        return new { freed, deleted, skipped };
    }

    static void RemoveEmptyDirs(string root, TimeSpan? minAge)
    {
        List<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root, "*", Opts).OrderByDescending(d => d.Length).ToList(); } catch { return; }
        var limit = minAge is TimeSpan a ? DateTime.UtcNow - a : DateTime.MaxValue;
        foreach (var d in dirs)
        {
            try
            {
                if (minAge != null && Directory.GetLastWriteTimeUtc(d) > limit) continue;
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            }
            catch { }
        }
    }
}
