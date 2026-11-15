using HidSharp;

namespace CentroDeMando.Services.Brands;

/// <summary>Utilidades para localizar la colección HID correcta.</summary>
static class HidPick
{
    public static int Interface(HidDevice d)
    {
        var p = d.DevicePath.ToLowerInvariant(); int i = p.IndexOf("mi_", StringComparison.Ordinal);
        return i >= 0 && i + 5 <= p.Length && int.TryParse(p.AsSpan(i + 3, 2), System.Globalization.NumberStyles.HexNumber, null, out var n) ? n : -1;
    }
    public static bool HasPage(HidDevice d, int page)
    {
        try { return d.GetReportDescriptor().DeviceItems.Any(it => it.Usages.GetAllValues().Any(u => (u >> 16) == page)); } catch { return false; }
    }
    public static HidDevice? Find(int vid, int pid, int iface, int page = -1, Func<HidDevice, bool>? extra = null)
    {
        try
        {
            return DeviceList.Local.GetHidDevices(vid, pid).FirstOrDefault(d =>
                (iface < 0 || Interface(d) == iface) && (page < 0 || HasPage(d, page)) && (extra == null || extra(d)));
        }
        catch { return null; }
    }
}

/// <summary>
/// Teclados con chip EVision v1 (Redragon, Glorious, Tecware y muchas marcas; VID 320F y Sonix 0C45).
/// OpenRGB EVisionKeyboardController: informe de salida 0x04 de 64 bytes en la interfaz 1, página 0xFF1C;
/// bloque de modo (comando 0x06): modo, brillo 0–4, velocidad 0–5, dirección, aleatorio, R, G, B.
/// </summary>
public sealed class EVisionDevice : BrandDevice
{
    public static readonly HashSet<(int, int)> Known = new()
    {
        (0x320F, 0x5000), (0x320F, 0x502A), (0x320F, 0x505B), (0x320F, 0x5064), (0x320F, 0x5078), (0x320F, 0x5084),
        (0x0C45, 0x5004), (0x0C45, 0x5104), (0x0C45, 0x5204), (0x0C45, 0x652F), (0x0C45, 0x7672), (0x0C45, 0x7698), (0x0C45, 0x8520),
    };
    readonly object _lock = new();
    readonly string _name;

    public EVisionDevice(int pid, string name, int vid = 0x320F) { Vid = vid; Pid = pid; _name = string.IsNullOrWhiteSpace(name) ? "Teclado RGB" : name; }

    HidStream Open()
    {
        if (!Known.Contains((Vid, Pid))) throw new InvalidOperationException("Este modelo con chip EVision todavía no está en la lista de compatibles.");
        var d = HidPick.Find(Vid, Pid, 1, 0xFF1C, x => { try { return x.GetMaxOutputReportLength() >= 64; } catch { return false; } })
                ?? throw new InvalidOperationException("No se ha encontrado la interfaz de iluminación del teclado.");
        if (!d.TryOpen(out var s)) throw new InvalidOperationException("No se puede abrir el teclado: cierra el programa del fabricante si está abierto.");
        s.ReadTimeout = 400; s.WriteTimeout = 500;
        return s;
    }

    static void Send(HidStream s, byte cmd, byte offset, params byte[] data)
    {
        var b = new byte[64];
        b[0] = 0x04; b[3] = cmd; b[4] = (byte)data.Length; b[5] = offset;
        Array.Copy(data, 0, b, 8, data.Length);
        int sum = 0; for (int i = 3; i < 64; i++) sum += b[i];
        b[1] = (byte)sum; b[2] = (byte)(sum >> 8);
        s.Write(b);
        try { var r = new byte[64]; s.Read(r, 0, 64); } catch { /* el teclado no siempre confirma */ }
    }

    public override BrandInfo Info()
    {
        lock (_lock) { using var s = Open(); }
        return new BrandInfo("EVision", _name, "keyboard", true, new[] { "static", "breathing", "spectrum", "off" },
            true, true, false, null, 0, 0, 0, null, "Teclado con chip EVision (Redragon y otras marcas). Los cambios se guardan en el teclado.");
    }

    public override void SetLighting(LightRequest q)
    {
        var (r, g, b) = Rgb(q.Color);
        byte mode = q.Effect switch { "breathing" => 0x05, "spectrum" => 0x04, _ => 0x06 };
        byte bri = q.Effect == "off" ? (byte)0 : (byte)Math.Clamp((int)Math.Round(q.Brightness / 25.0), 1, 4);
        byte speed = (byte)Math.Clamp(5 - q.Speed, 0, 5);                     // 0 = más rápido
        byte random = (byte)(q.Effect == "spectrum" ? 1 : 0);
        lock (_lock)
        {
            using var s = Open();
            Send(s, 0x06, 0x00, mode, bri, speed, 0x00, random, r, g, b);
        }
    }
}

/// <summary>Ratones Redragon con chip Holtek (VID 04D9). OpenRGB RedragonMouseController: informe de característica 0x02 de 16 bytes.</summary>
public sealed class RedragonMouseDevice : BrandDevice
{
    public static readonly Dictionary<int, string> Models = new()
    {
        [0xFC30] = "Redragon M711", [0xFC39] = "Redragon M715", [0xFC3A] = "Redragon M716", [0xFC4D] = "Redragon M908",
        [0xFC38] = "Redragon M602", [0xFC5F] = "Redragon M808", [0xFC58] = "Redragon M801", [0xFA7E] = "Redragon M810",
        [0xFC69] = "Redragon M987", [0xFC40] = "Redragon M921", [0xFC62] = "Redragon M711-FPS", [0xFC5E] = "Redragon M901-K-2",
    };
    readonly object _lock = new();
    public RedragonMouseDevice(int pid) { Vid = 0x04D9; Pid = pid; }

    HidStream Open()
    {
        var d = HidPick.Find(Vid, Pid, 2, 0xFFA0) ?? throw new InvalidOperationException("No se ha encontrado la interfaz de iluminación del ratón.");
        if (!d.TryOpen(out var s)) throw new InvalidOperationException("No se puede abrir el ratón: cierra el programa de Redragon si está abierto.");
        return s;
    }
    static void Feature(HidStream s, params byte[] p) { var b = new byte[16]; Array.Copy(p, b, Math.Min(16, p.Length)); s.SetFeature(b); Thread.Sleep(20); }

    public override BrandInfo Info()
    {
        lock (_lock) { using var s = Open(); }
        return new BrandInfo("Redragon", Models.GetValueOrDefault(Pid, "Ratón Redragon"), "mouse", true,
            new[] { "static", "breathing", "spectrum", "off" }, true, true, false, null, 0, 0, 0, null, null);
    }

    public override void SetLighting(LightRequest q)
    {
        var (r, g, b) = Scale(Rgb(q.Color), q.Brightness);
        byte mode = q.Effect switch { "breathing" => 0x04, "spectrum" => 0x08, _ => 0x02 };
        byte on = (byte)(q.Effect == "off" ? 0 : 1), speed = (byte)Math.Clamp(q.Speed - 1, 0, 4);
        lock (_lock)
        {
            using var s = Open();
            Feature(s, 0x02, 0xF3, 0x49, 0x04, 0x06, 0, 0, 0, r, g, b, on, speed, mode);
            Feature(s, 0x02, 0xF1, 0x02, 0x04);
        }
    }
}
