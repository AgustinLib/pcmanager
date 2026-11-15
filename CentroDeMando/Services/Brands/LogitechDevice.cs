using System.Text;
using HidSharp;

namespace CentroDeMando.Services.Brands;

/// <summary>
/// Periféricos Logitech con HID++ 2.0 (por cable o receptor Lightspeed/Unifying/Bolt), sin G HUB.
/// Las funciones se localizan preguntando al dispositivo (ROOT.GetFeature) y los cambios de luz se
/// envían sin guardar en la memoria del ratón (byte de persistencia a 0).
/// </summary>
public sealed class LogitechDevice : BrandDevice
{
    const byte Long = 0x11, Sw = 0x0A;
    const ushort FName = 0x0005, FBattery = 0x1000, FUnifiedBattery = 0x1004, FDpi = 0x2201, FLed = 0x8070, FRgb = 0x8071;
    static readonly HashSet<int> Receivers = new() { 0xC52B, 0xC532, 0xC534, 0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC548 };
    public static bool IsReceiver(int pid) => Receivers.Contains(pid);

    readonly object _lock = new();
    readonly Dictionary<ushort, byte> _feat = new();
    byte _index = 0xFF;
    string _kindHint;
    string? _name;

    public LogitechDevice(int pid, string kindHint) { Vid = 0x046D; Pid = pid; _kindHint = kindHint; }

    HidStream Open()
    {
        foreach (var d in Hid(Vid, Pid))
        {
            try { if (d.GetMaxOutputReportLength() != 20 || d.GetMaxInputReportLength() != 20) continue; } catch { continue; }
            if (d.TryOpen(out var s)) { s.ReadTimeout = 300; s.WriteTimeout = 500; return s; }
        }
        throw new InvalidOperationException("No se puede abrir el periférico Logitech. Cierra Logitech G HUB si está abierto y vuelve a intentarlo.");
    }

    /// <summary>Petición larga; devuelve los 20 bytes de respuesta o null (sin respuesta / error).</summary>
    byte[]? Call(HidStream s, byte feat, byte func, params byte[] p)
    {
        var b = new byte[20];
        b[0] = Long; b[1] = _index; b[2] = feat; b[3] = (byte)(func << 4 | Sw);
        Array.Copy(p, 0, b, 4, Math.Min(16, p.Length));
        s.Write(b);
        var until = DateTime.UtcNow.AddMilliseconds(900);
        var r = new byte[20];
        while (DateTime.UtcNow < until)
        {
            int n; try { n = s.Read(r, 0, 20); } catch (TimeoutException) { continue; } catch { return null; }
            if (n < 7 || r[1] != _index) continue;
            if (r[2] == 0xFF && r[3] == feat && r[4] == b[3]) return null;          // error HID++ 2.0
            if (r[2] == 0x8F) return null;                                          // error HID++ 1.0 (dormido)
            if (r[2] == feat && r[3] == b[3]) return (byte[])r.Clone();
        }
        return null;
    }

    byte Feature(HidStream s, ushort id)
    {
        if (_feat.TryGetValue(id, out var f)) return f;
        var r = Call(s, 0x00, 0, (byte)(id >> 8), (byte)id);
        f = r == null ? (byte)0 : r[4];
        _feat[id] = f;
        return f;
    }

    /// <summary>Localiza el dispositivo: 0xFF por cable; 1..6 detrás de un receptor (ping de ROOT).</summary>
    void Locate(HidStream s)
    {
        if (!IsReceiver(Pid)) { _index = 0xFF; return; }
        if (_index != 0xFF && Call(s, 0x00, 1, 0, 0, 0xAA) != null) return;
        for (byte i = 1; i <= 6; i++)
        {
            _index = i; _feat.Clear();
            var r = Call(s, 0x00, 1, 0, 0, 0xAA);
            if (r != null && r[6] == 0xAA) return;
        }
        _index = 0xFF;
        throw new InvalidOperationException("El ratón o teclado Logitech está apagado o dormido. Muévelo o pulsa una tecla y vuelve a intentarlo.");
    }

    T With<T>(Func<HidStream, T> fn) { lock (_lock) { using var s = Open(); Locate(s); return fn(s); } }

    string ReadName(HidStream s)
    {
        byte f = Feature(s, FName); if (f == 0) return "Logitech";
        var r = Call(s, f, 0); if (r == null) return "Logitech";
        int len = r[4]; var sb = new StringBuilder();
        for (int off = 0; off < len && off < 64; off += 16)
        {
            var c = Call(s, f, 1, (byte)off); if (c == null) break;
            sb.Append(Encoding.ASCII.GetString(c, 4, Math.Min(16, len - off)));
        }
        var name = sb.ToString().Replace("\0", "").Trim();
        return name.Length == 0 ? "Logitech" : name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? name : "Logitech " + name;
    }

    public override BrandInfo Info() => With(s =>
    {
        _name ??= ReadName(s);
        int? dpi = null, batt = null; int min = 100, max = 25600, step = 50;
        byte fd = Feature(s, FDpi);
        if (fd != 0)
        {
            var r = Call(s, fd, 2, 0);
            if (r != null) dpi = r[5] << 8 | r[6];
            var l = Call(s, fd, 1, 0);
            if (l != null)
            {
                var vals = new List<int>();
                for (int i = 5; i + 1 < 20; i += 2) { int v = l[i] << 8 | l[i + 1]; if (v == 0) break; vals.Add(v); }
                var plain = vals.Where(v => (v & 0xE000) != 0xE000).ToList();
                var st = vals.FirstOrDefault(v => (v & 0xE000) == 0xE000);
                if (plain.Count > 0) { min = plain.Min(); max = plain.Max(); }
                if (st != 0) step = Math.Max(1, st & 0x1FFF);
            }
        }
        byte fb = Feature(s, FUnifiedBattery);
        if (fb != 0) { var r = Call(s, fb, 1); if (r != null) batt = r[4]; }
        else if ((fb = Feature(s, FBattery)) != 0) { var r = Call(s, fb, 0); if (r != null && r[4] > 0) batt = r[4]; }
        bool rgb = Feature(s, FRgb) != 0 || Feature(s, FLed) != 0;
        string kind = _kindHint == "keyboard" || _name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) || _name.Contains(" G9", StringComparison.Ordinal) ? "keyboard" : dpi != null ? "mouse" : _kindHint;
        return new BrandInfo("Logitech", _name, kind, true,
            rgb ? new[] { "static", "breathing", "spectrum", "off" } : Array.Empty<string>(), rgb, false, dpi != null,
            dpi, min, max, step, batt, rgb ? null : "Este modelo no tiene iluminación RGB.");
    });

    public override void SetLighting(LightRequest q) => With(s =>
    {
        var (r, g, b) = Rgb(q.Color);
        int bri = Math.Clamp(q.Brightness, 1, 100);
        var (sr, sg, sb) = Scale((r, g, b), bri);
        byte[] P(byte effect) => effect switch
        {
            0x01 => new byte[] { sr, sg, sb, 0x00 },
            0x0A => new byte[] { r, g, b, 0x0B, 0xB8, 0x00, (byte)bri },              // respiración: 3000 ms
            0x03 => new byte[] { 0, 0, 0, 0, 0, 0x27, 0x10, (byte)bri },              // ciclo: 10000 ms
            _ => Array.Empty<byte>(),
        };
        byte effectId = q.Effect switch { "off" => 0x00, "breathing" => 0x0A, "spectrum" => 0x03, _ => 0x01 };

        byte f = Feature(s, FRgb); byte func = 1;
        if (f == 0) { f = Feature(s, FLed); func = 3; }
        if (f == 0) throw new InvalidOperationException("Este modelo Logitech no tiene iluminación RGB.");

        // Número de zonas (0x8070: GetInfo → [4]; 0x8071: GetInfo de cluster 0xFF → [4])
        var info = func == 3 ? Call(s, f, 0) : Call(s, f, 0, 0xFF, 0xFF);
        int zones = info == null ? 1 : Math.Clamp((int)info[4], 1, 8);
        bool any = false;
        for (byte z = 0; z < zones; z++)
        {
            var args = new byte[13]; args[0] = z; args[1] = effectId;
            P(effectId).CopyTo(args, 2);
            args[12] = 0x00;                                                         // sin guardar en memoria
            if (Call(s, f, func, args) != null) any = true;
        }
        if (!any) throw new InvalidOperationException("El periférico Logitech no ha aceptado el cambio de iluminación.");
        return true;
    });

    public override void SetDpi(int dpi) => With(s =>
    {
        byte f = Feature(s, FDpi);
        if (f == 0) throw new InvalidOperationException("Este periférico no permite cambiar los DPI.");
        dpi = Math.Clamp(dpi, 50, 32000);
        var r = Call(s, f, 3, 0, (byte)(dpi >> 8), (byte)dpi);
        if (r == null) throw new InvalidOperationException("El ratón Logitech no ha aceptado los DPI.");
        return true;
    });
}
