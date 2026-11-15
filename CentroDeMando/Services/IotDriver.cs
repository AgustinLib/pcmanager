using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace CentroDeMando.Services;

/// <summary>
/// Cliente mínimo (gRPC-web sobre HTTP local) del servicio en segundo plano del driver de AJAZZ
/// (iot_driver.exe, puerto 3814). Ese servicio es quien habla con el teclado, tanto por cable como
/// a través del receptor 2.4G, así que lo usamos como transporte sin abrir la app oficial.
/// </summary>
public sealed class IotDriver
{
    const string Base = "http://127.0.0.1:3814/driver.DriverGrpc/";
    const int RyVendor = 0x3151;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public sealed record Device(string Path, uint Id, bool ViaDongle, bool Online, int Battery, int Vid, int Pid);

    // ───────────── protobuf (solo lo necesario) ─────────────

    static void WriteVarint(List<byte> o, ulong v)
    {
        do { byte b = (byte)(v & 0x7F); v >>= 7; if (v != 0) b |= 0x80; o.Add(b); } while (v != 0);
    }
    static void FStr(List<byte> o, int no, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        WriteVarint(o, (ulong)((no << 3) | 2)); WriteVarint(o, (ulong)b.Length); o.AddRange(b);
    }
    static void FBytes(List<byte> o, int no, ReadOnlySpan<byte> b)
    {
        WriteVarint(o, (ulong)((no << 3) | 2)); WriteVarint(o, (ulong)b.Length); o.AddRange(b.ToArray());
    }
    static void FVar(List<byte> o, int no, ulong v)
    {
        if (v == 0) return;
        WriteVarint(o, (ulong)(no << 3)); WriteVarint(o, v);
    }

    readonly record struct Field(int No, int Wire, ulong Varint, byte[]? Bytes);

    static List<Field> Parse(ReadOnlySpan<byte> b)
    {
        var list = new List<Field>();
        int i = 0;
        ulong ReadVar(ReadOnlySpan<byte> s)
        {
            ulong r = 0; int sh = 0;
            while (i < s.Length) { byte x = s[i++]; r |= (ulong)(x & 0x7F) << sh; if ((x & 0x80) == 0) break; sh += 7; }
            return r;
        }
        while (i < b.Length)
        {
            ulong tag = ReadVar(b);
            int no = (int)(tag >> 3), wt = (int)(tag & 7);
            switch (wt)
            {
                case 0: list.Add(new Field(no, 0, ReadVar(b), null)); break;
                case 2:
                    int len = (int)ReadVar(b);
                    if (len < 0 || i + len > b.Length) return list;
                    list.Add(new Field(no, 2, 0, b.Slice(i, len).ToArray())); i += len; break;
                case 5: i += 4; break;
                case 1: i += 8; break;
                default: return list;
            }
        }
        return list;
    }

    static byte[] Frame(List<byte> msg)
    {
        var f = new byte[5 + msg.Count];
        f[1] = (byte)(msg.Count >> 24); f[2] = (byte)(msg.Count >> 16); f[3] = (byte)(msg.Count >> 8); f[4] = (byte)msg.Count;
        msg.CopyTo(f, 5);
        return f;
    }

    static HttpRequestMessage Req(string method, List<byte> msg)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Base + method) { Content = new ByteArrayContent(Frame(msg)) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc-web+proto");
        req.Headers.Add("X-Grpc-Web", "1");
        return req;
    }

    static byte[]? FirstDataFrame(byte[] body)
    {
        int i = 0;
        while (i + 5 <= body.Length)
        {
            int flag = body[i];
            int len = (body[i + 1] << 24) | (body[i + 2] << 16) | (body[i + 3] << 8) | body[i + 4];
            if (len < 0 || i + 5 + len > body.Length) break;
            if (flag == 0) return body.AsSpan(i + 5, len).ToArray();
            i += 5 + len;
        }
        return null;
    }

    static byte[]? Unary(string method, List<byte> msg)
    {
        using var req = Req(method, msg);
        using var resp = Http.Send(req, HttpCompletionOption.ResponseContentRead);
        using var ms = new MemoryStream();
        resp.Content.ReadAsStream().CopyTo(ms);
        return FirstDataFrame(ms.ToArray());
    }

    // ───────────── API ─────────────

    public static bool IsServiceRunning() => Process.GetProcesses().Any(p => p.ProcessName.StartsWith("iot_driver", StringComparison.OrdinalIgnoreCase));

    /// <summary>Arranca el servicio del driver si está instalado y no está en marcha.</summary>
    public static bool EnsureRunning(out string? error)
    {
        error = null;
        if (IsServiceRunning()) return true;
        var candidates = new List<string>();
        try
        {
            var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AJAZZ Driver");
            if (Directory.Exists(roaming))
                candidates.AddRange(Directory.GetFiles(roaming, "iot_driver*.exe").OrderByDescending(File.GetLastWriteTimeUtc));
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AJAZZ Driver", "resources", "app", "iot_driver.exe");
            if (File.Exists(local)) candidates.Add(local);
        }
        catch { }
        if (candidates.Count == 0) { error = "No está instalado el driver de AJAZZ (hace falta su servicio en segundo plano)."; return false; }
        try
        {
            var exe = candidates[0];
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true });
            for (int i = 0; i < 20; i++) { Thread.Sleep(250); try { if (GetDevices() != null) return true; } catch { } }
            return true;
        }
        catch (Exception ex) { error = "No se pudo iniciar el servicio del driver: " + ex.Message; return false; }
    }

    /// <summary>Lista de dispositivos (primer mensaje del flujo watchDevList).</summary>
    public static List<Device>? GetDevices()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var req = Req("watchDevList", new List<byte>());
        using var resp = Http.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var stream = resp.Content.ReadAsStream(cts.Token);
        var head = new byte[5];
        if (!ReadExact(stream, head, cts.Token)) return null;
        int len = (head[1] << 24) | (head[2] << 16) | (head[3] << 8) | head[4];
        if (head[0] != 0 || len < 0 || len > 1 << 20) return null;
        var body = new byte[len];
        if (!ReadExact(stream, body, cts.Token)) return null;

        var result = new List<Device>();
        foreach (var dj in Parse(body).Where(f => f.No == 1 && f.Bytes != null))
        {
            foreach (var f in Parse(dj.Bytes!))
            {
                if (f.Bytes == null) continue;
                var inner = Parse(f.Bytes);
                string path = Str(inner, 3);
                int vid = (int)Var(inner, 7), pid = (int)Var(inner, 8);
                if (f.No == 1)
                {
                    // Device { devtype=1, is24=2, path=3, id=4, battery=5, isOnline=6, vid=7, pid=8 }
                    ulong devType = Var(inner, 1);
                    uint id = unchecked((uint)Var(inner, 4));
                    bool is24 = Var(inner, 2) != 0;
                    if (devType != 0 || id == 0) continue;           // 0 = teclado; 1 = boot; 2 = interfaz de eventos
                    result.Add(new Device(path, id, false, !is24 || Var(inner, 6) != 0, (int)Var(inner, 5), vid, pid));
                }
                else if (f.No == 2)
                {
                    // DangleCommon { keyboard=1{status=2{battery=1,isOnline=2}}, mouse=2, path=3, keyboardId=5, mouseId=6, vid=7, pid=8 }
                    uint kid = unchecked((uint)Var(inner, 5));
                    // kid == 0: receptor aún sin identificar (sondeo inalámbrico bloqueado). Se devuelve con Id 0 para
                    // que quien llame lo desbloquee; los receptores que solo llevan un ratón se ignoran.
                    if (kid == 0 && (Var(inner, 6) != 0 || vid != RyVendor)) continue;
                    bool online = false; int battery = 0;
                    var kb = inner.FirstOrDefault(x => x.No == 1 && x.Bytes != null);
                    if (kb.Bytes != null)
                    {
                        var st = Parse(kb.Bytes).FirstOrDefault(x => x.No == 2 && x.Bytes != null);
                        if (st.Bytes != null)
                        {
                            var s = Parse(st.Bytes);
                            battery = (int)Var(s, 1); online = Var(s, 2) != 0;
                        }
                    }
                    result.Add(new Device(path, kid, true, online, battery, vid, pid));
                }
            }
        }
        return result;
    }

    static bool ReadExact(Stream s, byte[] buf, CancellationToken ct)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.ReadAsync(buf.AsMemory(off), ct).AsTask().GetAwaiter().GetResult();
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }

    static ulong Var(List<Field> l, int no) { foreach (var f in l) if (f.No == no && f.Wire == 0) return f.Varint; return 0; }
    static string Str(List<Field> l, int no) { foreach (var f in l) if (f.No == no && f.Bytes != null) return Encoding.UTF8.GetString(f.Bytes); return ""; }

    /// <summary>Envía un comando al dispositivo. Devuelve el texto de error del servicio o null si fue bien.</summary>
    public static string? Send(Device dev, ReadOnlySpan<byte> payload, int checksumType)
    {
        var m = new List<byte>();
        FStr(m, 1, dev.Path); FBytes(m, 2, payload); FVar(m, 3, (ulong)checksumType); FVar(m, 4, dev.ViaDongle ? 1UL : 0UL);
        var r = Unary("sendMsg", m);
        if (r == null) return null;
        var err = Str(Parse(r), 1);
        return string.IsNullOrEmpty(err) ? null : err;
    }

    public static byte[]? Read(Device dev, out string? error)
    {
        var m = new List<byte>();
        FStr(m, 1, dev.Path);
        var r = Unary("readMsg", m);
        error = null;
        if (r == null) { error = "sin respuesta"; return null; }
        var f = Parse(r);
        var err = Str(f, 1);
        if (!string.IsNullOrEmpty(err)) { error = err; return null; }
        foreach (var x in f) if (x.No == 2 && x.Bytes != null) return x.Bytes;
        error = "respuesta vacía";
        return null;
    }

    /// <summary>Pausa/reanuda el sondeo inalámbrico del servicio (lo hace la app oficial tras cada escritura).</summary>
    public static void SetWirelessLoopLock(bool locked)
    {
        try
        {
            var m = new List<byte>();
            FVar(m, 1, locked ? 1UL : 0UL);
            Unary("changeWirelessLoopStatus", m);
        }
        catch { }
    }
}
