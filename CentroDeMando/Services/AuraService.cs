using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using HidSharp;

namespace CentroDeMando.Services;

public sealed record AuraDevice(int Id, string Name, string Type, int Leds);
public sealed record AuraStatus(bool Available, string? Error, List<AuraDevice> Devices);

public sealed class AuraRequest
{
    public int[]? DeviceIds { get; set; }
    public string Effect { get; set; } = "static";   // static | breath | rainbow | cycle | temp | off
    public string Color { get; set; } = "#00d6aa";
    public int Speed { get; set; } = 3;              // 1..5
    public int Brightness { get; set; } = 100;       // 0..100
}

/// <summary>
/// RGB de la placa base ASUS (conector RGB de 12 V y conectores ARGB donde van tiras y ventiladores)
/// hablando directamente con el controlador USB "AURA LED Controller" de la placa (VID 0B05),
/// en modo directo: los colores se envían en vivo y no se graba nada en la memoria del controlador.
/// (El SDK COM de Aura no enumera dispositivos con las versiones actuales de Armoury Crate.)
/// </summary>
public sealed class AuraService : IDisposable
{
    const int Vid = 0x0B05;
    static readonly int[] Pids = { 0x1939, 0x18F3, 0x19AF, 0x1AA6, 0x1BED, 0x1867, 0x1872, 0x18A3, 0x18A5 };
    const int LedsPerArgbHeader = 60;

    sealed record Channel(string Name, string Type, byte EffectChannel, byte DirectChannel, int Leds);

    readonly BlockingCollection<Action> _queue = new();
    readonly Thread _thread;
    HidStream? _stream;
    string? _firmware;
    List<Channel> _channels = new();
    readonly HashSet<int> _directSet = new();
    AuraRequest? _active;
    double _phase;
    long _lastStatic;
    volatile bool _stop;
    public Func<(double? cpu, double? gpu)>? TempSource;

    public AuraService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "aura" };
        _thread.Start();
    }

    void Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long last = sw.ElapsedMilliseconds;
        while (!_stop)
        {
            var req = _active;
            bool animated = req != null && req.Effect is "breath" or "rainbow" or "cycle" or "temp";
            if (_queue.TryTake(out var job, animated ? 70 : 400)) { try { job(); } catch { } last = sw.ElapsedMilliseconds; continue; }
            long now = sw.ElapsedMilliseconds;
            if (req == null) { last = now; continue; }
            try
            {
                if (animated) { Frame(req, (now - last) / 1000.0); last = now; }
                else if (now - _lastStatic > 2500) { Frame(req, 0); _lastStatic = now; last = now; }   // refresco por si otro programa pisa el color
            }
            catch { CloseDevice(); _active = null; }
        }
        CloseDevice();
    }

    T Invoke<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Add(() => { try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); } });
        if (!tcs.Task.Wait(8000)) throw new TimeoutException("El controlador de iluminación no responde.");
        return tcs.Task.GetAwaiter().GetResult();
    }

    // ───────────── dispositivo ─────────────

    void CloseDevice()
    {
        try { _stream?.Dispose(); } catch { }
        _stream = null; _directSet.Clear();
    }

    byte[]? Request(byte cmd, byte expect)
    {
        var s = _stream!;
        var buf = new byte[65]; buf[0] = 0xEC; buf[1] = cmd;
        s.Write(buf);
        for (int i = 0; i < 6; i++)
        {
            byte[] r;
            try { r = s.Read(); } catch (TimeoutException) { return null; }
            if (r.Length >= 2 && r[0] == 0xEC && r[1] == expect) return r;
        }
        return null;
    }

    void EnsureDevice()
    {
        if (_stream != null) return;
        HidDevice? dev = null;
        foreach (var d in DeviceList.Local.GetHidDevices(Vid))
        {
            if (!Pids.Contains(d.ProductID)) continue;
            try { if (d.GetMaxOutputReportLength() == 65 && d.GetMaxInputReportLength() == 65) { dev = d; break; } } catch { }
        }
        if (dev == null) throw new InvalidOperationException("No se ha encontrado el controlador Aura de la placa base (USB 0B05).");
        if (!dev.TryOpen(out var stream)) throw new InvalidOperationException("No se pudo abrir el controlador Aura de la placa base.");
        stream.ReadTimeout = 600; stream.WriteTimeout = 600;
        _stream = stream;

        var fw = Request(0x82, 0x02);
        if (fw == null) { CloseDevice(); throw new InvalidOperationException("El controlador Aura no responde a la identificación."); }
        _firmware = Encoding.ASCII.GetString(fw, 2, 16).TrimEnd('\0', ' ');

        var cfg = Request(0xB0, 0x30);
        if (cfg == null) { CloseDevice(); throw new InvalidOperationException("El controlador Aura no devuelve su configuración."); }
        var table = cfg.AsSpan(4, 60);
        int argbHeaders = table[0x02];
        int totalFixed = table[0x1B];
        int rgbHeaders = table[0x1D];
        if (totalFixed < rgbHeaders) rgbHeaders = 0;

        // Numeración de canales igual que OpenRGB (AsusAuraMainboardController): en el firmware AULA3-AR32
        // los conectores ARGB empiezan en 0 y la placa va detrás; en el resto la placa es el canal 0.
        argbHeaders = Math.Min(argbHeaders, 4);
        bool argbFromZero = _firmware.StartsWith("AULA3-AR32", StringComparison.Ordinal);
        int effect = 0;
        var ch = new List<Channel>();
        if (totalFixed > 0)
        {
            byte mbEffect = (byte)(argbFromZero ? argbHeaders : effect);
            ch.Add(new Channel(rgbHeaders > 0 && totalFixed == rgbHeaders ? "Conector RGB 12 V" : "Placa base y conector RGB", "Placa base", mbEffect, 0x04, totalFixed));
            if (!argbFromZero) effect++;
        }
        for (int i = 0; i < argbHeaders; i++)
            ch.Add(new Channel($"Conector ARGB {i + 1} (tiras / ventiladores)", "ARGB direccionable", (byte)effect++, (byte)i, LedsPerArgbHeader));
        _channels = ch;
        _gen1Sent = false;
    }

    bool _gen1Sent;

    /// <summary>Igual que OpenRGB al iniciar: pone los conectores direccionables en modo Gen1 para el control directo.</summary>
    void SetGen1()
    {
        if (_gen1Sent) return;
        var buf = new byte[65];
        buf[0] = 0xEC; buf[1] = 0x52; buf[2] = 0x53; buf[3] = 0x00; buf[4] = 0x01;
        _stream!.Write(buf);
        _gen1Sent = true;
    }

    public AuraStatus Status() => Invoke(() =>
    {
        try
        {
            EnsureDevice();
            var list = _channels.Select((c, i) => new AuraDevice(i, c.Name, c.Type, c.Leds)).ToList();
            return new AuraStatus(list.Count > 0, list.Count > 0 ? null : "El controlador Aura no declara ningún canal de iluminación.", list);
        }
        catch (Exception ex)
        {
            return new AuraStatus(false, ex.Message, new List<AuraDevice>());
        }
    });

    public string? Firmware => _firmware;

    public void Apply(AuraRequest req) => Invoke(() =>
    {
        EnsureDevice();
        if (_channels.Count == 0) throw new InvalidOperationException("No hay canales de iluminación en la placa.");
        req.Speed = Math.Clamp(req.Speed, 1, 5);
        req.Brightness = Math.Clamp(req.Brightness, 0, 100);
        _phase = 0;
        _active = req;
        Frame(req, 0);
        return true;
    });

    public void Release() => Invoke(() =>
    {
        _active = null;
        CloseDevice();
        return true;
    });

    // ───────────── envío ─────────────

    void SetDirectMode(int ci)
    {
        if (_directSet.Contains(ci)) return;
        SetGen1();
        var c = _channels[ci];
        var buf = new byte[65];
        buf[0] = 0xEC; buf[1] = 0x35; buf[2] = c.EffectChannel; buf[3] = 0x00; buf[4] = 0x00; buf[5] = 0xFF;   // modo directo
        _stream!.Write(buf);
        _directSet.Add(ci);
    }

    void SendDirect(Channel c, byte[] rgb, int leds)
    {
        int sent = 0;
        while (sent < leds)
        {
            int n = Math.Min(20, leds - sent);
            var buf = new byte[65];
            buf[0] = 0xEC; buf[1] = 0x40;
            buf[2] = (byte)((sent + n >= leds ? 0x80 : 0x00) | c.DirectChannel);
            buf[3] = (byte)sent; buf[4] = (byte)n;
            Array.Copy(rgb, sent * 3, buf, 5, n * 3);
            _stream!.Write(buf);
            sent += n;
        }
    }

    // ───────────── efectos ─────────────

    static (double r, double g, double b) ParseHex(string? s)
    {
        s = (s ?? "#ffffff").TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) v = 0xFFFFFF;
        return (((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
    }

    static (double r, double g, double b) Hsv(double h, double s, double v)
    {
        h = ((h % 1) + 1) % 1 * 6;
        int i = (int)Math.Floor(h); double f = h - i;
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        return i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
    }

    static void Put(byte[] rgb, int i, (double r, double g, double b) c, double k)
    {
        rgb[i * 3] = (byte)Math.Clamp(Math.Round(c.r * k * 255), 0, 255);
        rgb[i * 3 + 1] = (byte)Math.Clamp(Math.Round(c.g * k * 255), 0, 255);
        rgb[i * 3 + 2] = (byte)Math.Clamp(Math.Round(c.b * k * 255), 0, 255);
    }

    void Frame(AuraRequest req, double dt)
    {
        EnsureDevice();
        double bright = req.Brightness / 100.0;
        double rate = req.Speed switch { 1 => 0.08, 2 => 0.15, 3 => 0.25, 4 => 0.45, _ => 0.8 };   // ciclos por segundo
        _phase += dt * rate;
        var baseColor = ParseHex(req.Color);
        (double? cpu, double? gpu) temps = req.Effect == "temp" ? TempSource?.Invoke() ?? (null, null) : (null, null);

        for (int ci = 0; ci < _channels.Count; ci++)
        {
            if (req.DeviceIds != null && req.DeviceIds.Length > 0 && !req.DeviceIds.Contains(ci)) continue;
            var c = _channels[ci];
            var rgb = new byte[c.Leds * 3];
            for (int li = 0; li < c.Leds; li++)
            {
                switch (req.Effect)
                {
                    case "off": break;
                    case "breath":
                        Put(rgb, li, baseColor, bright * (0.06 + 0.94 * (0.5 - 0.5 * Math.Cos(_phase * 2 * Math.PI))));
                        break;
                    case "rainbow":
                        Put(rgb, li, Hsv(_phase + li / 24.0, 1, 1), bright);
                        break;
                    case "cycle":
                        Put(rgb, li, Hsv(_phase * 0.5, 1, 1), bright);
                        break;
                    case "temp":
                        // 40 °C azul-verdoso → 60 °C verde → 75 °C ámbar → 90 °C rojo
                        double t = Math.Max(temps.cpu ?? 0, temps.gpu ?? 0);
                        Put(rgb, li, Hsv(0.5 - 0.5 * Math.Clamp((t - 40) / 50.0, 0, 1), 1, 1), bright);
                        break;
                    default:
                        Put(rgb, li, baseColor, bright);
                        break;
                }
            }
            SetDirectMode(ci);
            SendDirect(c, rgb, c.Leds);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _queue.Add(() => { });
        if (_thread.IsAlive) _thread.Join(2500);
    }
}
