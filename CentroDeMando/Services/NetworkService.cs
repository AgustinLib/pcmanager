using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CentroDeMando.Services;

public sealed record NetAdapter(string Id, string Name, string Desc, string Type, long SpeedMbps, string Mac, List<string> Ipv4, string? Gateway,
    List<string> Dns, string DnsMode, string? Ssid, int? Signal);
public sealed record PingResult(string Name, string Host, double? Avg, double? Min, double? Max, double? Jitter, int Loss);

/// <summary>Red: adaptadores, DNS, ping, test de velocidad y reparaciones rápidas.</summary>
public static class NetworkService
{
    static readonly Dictionary<string, (string[] V4, string[] V6)> Presets = new()
    {
        ["cloudflare"] = (new[] { "1.1.1.1", "1.0.0.1" }, new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }),
        ["google"] = (new[] { "8.8.8.8", "8.8.4.4" }, new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }),
        ["quad9"] = (new[] { "9.9.9.9", "149.112.112.112" }, new[] { "2620:fe::fe", "2620:fe::9" }),
        ["adguard"] = (new[] { "94.140.14.14", "94.140.15.15" }, new[] { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" }),
        ["opendns"] = (new[] { "208.67.222.222", "208.67.220.220" }, new[] { "2620:119:35::35", "2620:119:53::53" }),
    };

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static NetworkService() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("CentroDeMando"); }

    static string Run(string file, string args, int timeout = 20000)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(timeout);
        if (p.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(o) ? $"Código de salida {p.ExitCode}" : o.Trim());
        return o;
    }

    static IEnumerable<NetworkInterface> Active() => NetworkInterface.GetAllNetworkInterfaces().Where(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) && !n.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
        && !n.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) && !n.Description.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase));

    public static object Info()
    {
        (string? ssid, int? signal) wifi = (null, null);
        try
        {
            var o = Run("netsh.exe", "wlan show interfaces", 5000);
            var m = Regex.Match(o, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
            var s = Regex.Match(o, @":\s*(\d{1,3})\s*%", RegexOptions.Multiline);
            if (m.Success) wifi = (m.Groups[1].Value, s.Success ? int.Parse(s.Groups[1].Value) : null);
        }
        catch { }
        var list = new List<NetAdapter>();
        foreach (var n in Active())
        {
            try
            {
                var ip = n.GetIPProperties();
                var v4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList();
                if (v4.Count == 0) continue;
                var gw = ip.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString();
                var dns = ip.DnsAddresses.Select(a => a.ToString()).Where(s => !s.StartsWith("fec0:", StringComparison.OrdinalIgnoreCase)).ToList();
                bool wireless = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                string mac = string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                list.Add(new NetAdapter(n.Id, n.Name, n.Description, wireless ? "wifi" : n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "ethernet" : "other",
                    n.Speed > 0 ? n.Speed / 1_000_000 : 0, mac, v4, gw, dns, DnsMode(n.Id, dns), wireless ? wifi.ssid : null, wireless ? wifi.signal : null));
            }
            catch { }
        }
        return new { adapters = list.OrderBy(a => a.Gateway == null).ThenBy(a => a.Type == "wifi").ToList(), isAdmin = SystemInfo.IsAdmin() };
    }

    static string DnsMode(string id, List<string> dns)
    {
        string stat = "";
        try { using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + id); stat = k?.GetValue("NameServer") as string ?? ""; } catch { }
        if (string.IsNullOrWhiteSpace(stat)) return "auto";
        var first = stat.Split(',', ' ')[0].Trim();
        foreach (var (k, p) in Presets) if (p.V4.Contains(first)) return k;
        return "custom";
    }

    public static void SetDns(string adapterId, string preset)
    {
        if (!SystemInfo.IsAdmin()) throw new InvalidOperationException("Para cambiar los DNS hay que abrir la app como administrador.");
        var n = Active().FirstOrDefault(a => a.Id == adapterId) ?? throw new ArgumentException("No se ha encontrado el adaptador de red.");
        string name = n.Name.Replace("\"", "");
        if (preset == "auto")
        {
            Run("netsh.exe", $"interface ipv4 set dnsservers name=\"{name}\" source=dhcp");
            try { Run("netsh.exe", $"interface ipv6 set dnsservers name=\"{name}\" source=dhcp"); } catch { }
        }
        else
        {
            if (!Presets.TryGetValue(preset, out var p)) throw new ArgumentException("Servidor DNS desconocido.");
            Run("netsh.exe", $"interface ipv4 set dnsservers name=\"{name}\" source=static address={p.V4[0]} register=primary validate=no");
            Run("netsh.exe", $"interface ipv4 add dnsservers name=\"{name}\" address={p.V4[1]} index=2 validate=no");
            try
            {
                Run("netsh.exe", $"interface ipv6 set dnsservers name=\"{name}\" source=static address={p.V6[0]} register=primary validate=no");
                Run("netsh.exe", $"interface ipv6 add dnsservers name=\"{name}\" address={p.V6[1]} index=2 validate=no");
            }
            catch { }
        }
        FlushDns();
    }

    [DllImport("dnsapi.dll")] static extern bool DnsFlushResolverCache();
    public static void FlushDns() { if (!DnsFlushResolverCache()) Run("ipconfig.exe", "/flushdns"); }

    public static void Renew() => Run("ipconfig.exe", "/renew", 60000);

    public static void ResetStack()
    {
        if (!SystemInfo.IsAdmin()) throw new InvalidOperationException("Para restablecer la red hay que abrir la app como administrador.");
        Run("netsh.exe", "winsock reset");
        try { Run("netsh.exe", "int ip reset"); } catch { /* algunas claves protegidas no se pueden restablecer: no pasa nada */ }
        FlushDns();
    }

    static readonly Regex HostRx = new(@"^[A-Za-z0-9]([A-Za-z0-9\-\.]{0,251}[A-Za-z0-9])?$|^[0-9a-fA-F:]+$");

    public static async Task<List<PingResult>> Ping(string? custom)
    {
        var hosts = new List<(string, string)> { ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8") };
        var gw = Active().Select(n => n.GetIPProperties().GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any)))
                         .FirstOrDefault(a => a != null);
        if (gw != null) hosts.Insert(0, ("Router", gw.ToString()));
        custom = custom?.Trim();
        if (!string.IsNullOrEmpty(custom))
        {
            if (!HostRx.IsMatch(custom)) throw new ArgumentException("Dirección no válida.");
            hosts.Add((custom, custom));
        }
        var tasks = hosts.Select(async h =>
        {
            var times = new List<double>(); int lost = 0;
            using var p = new Ping();
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    var r = await p.SendPingAsync(h.Item2, 1500);
                    if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
                }
                catch { lost++; }
                await Task.Delay(120);
            }
            double? jitter = times.Count > 1 ? Math.Round(times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average(), 1) : null;
            return new PingResult(h.Item1, h.Item2, times.Count > 0 ? Math.Round(times.Average(), 1) : null, times.Count > 0 ? times.Min() : null,
                times.Count > 0 ? times.Max() : null, jitter, (int)Math.Round(lost * 100.0 / 6));
        });
        return (await Task.WhenAll(tasks)).ToList();
    }

    public static async Task<string> PublicIp() => (await Http.GetStringAsync("https://api.ipify.org")).Trim();

    /// <summary>Test de velocidad contra los servidores de Cloudflare (unos 10 s de bajada y 8 s de subida).</summary>
    public static async Task<object> SpeedTest()
    {
        double? ping = null;
        try
        {
            using var p = new Ping(); var t = new List<double>();
            for (int i = 0; i < 4; i++) { var r = await p.SendPingAsync("1.1.1.1", 1500); if (r.Status == IPStatus.Success) t.Add(r.RoundtripTime); }
            if (t.Count > 0) ping = Math.Round(t.Average(), 0);
        }
        catch { }

        // Bajada: 4 conexiones en paralelo durante ~8 s
        long down = 0; var sw = Stopwatch.StartNew(); var limit = TimeSpan.FromSeconds(8);
        using (var cts = new CancellationTokenSource(limit))
        {
            var dl = Enumerable.Range(0, 4).Select(async _ =>
            {
                var buf = new byte[81920];
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        using var resp = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        resp.EnsureSuccessStatusCode();
                        using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
                        int n; while ((n = await s.ReadAsync(buf, cts.Token)) > 0) Interlocked.Add(ref down, n);
                    }
                }
                catch (OperationCanceledException) { }
                catch (HttpRequestException) { }
            });
            await Task.WhenAll(dl);
        }
        double downSec = Math.Max(0.5, Math.Min(sw.Elapsed.TotalSeconds, limit.TotalSeconds));
        if (down == 0) throw new InvalidOperationException("No se ha podido conectar con el servidor del test de velocidad.");

        // Subida: 3 conexiones en paralelo con bloques de 1 MB durante ~7 s
        long up = 0; sw.Restart(); var upLimit = TimeSpan.FromSeconds(7);
        var payload = new byte[1_000_000]; Random.Shared.NextBytes(payload);
        using (var cts = new CancellationTokenSource(upLimit))
        {
            var ul = Enumerable.Range(0, 3).Select(async _ =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        using var content = new ByteArrayContent(payload);
                        using var resp = await Http.PostAsync("https://speed.cloudflare.com/__up", content, cts.Token);
                        if (resp.IsSuccessStatusCode) Interlocked.Add(ref up, payload.Length);
                    }
                }
                catch (OperationCanceledException) { }
                catch (HttpRequestException) { }
            });
            await Task.WhenAll(ul);
        }
        double upSec = Math.Max(0.5, Math.Min(sw.Elapsed.TotalSeconds, upLimit.TotalSeconds));
        return new
        {
            ping,
            downMbps = Math.Round(down * 8 / downSec / 1e6, 1),
            upMbps = up > 0 ? Math.Round(up * 8 / upSec / 1e6, 1) : (double?)null,
        };
    }
}
