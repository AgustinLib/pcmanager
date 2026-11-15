using HidSharp;

namespace CentroDeMando.Services.Brands;

/// <summary>
/// Periféricos Razer (VID 1532) sin Synapse. Protocolo de OpenRazer/OpenRGB: informe de característica
/// de 91 bytes (ID 0 + razer_report de 90). Se prueba qué colección e identificador de transacción
/// responde con una lectura inocua (versión de firmware) antes de enviar nada.
/// </summary>
public sealed class RazerDevice : BrandDevice
{
    const int Len = 91;
    static readonly byte[] Tids = { 0x3F, 0x1F, 0xFF, 0x9F };
    readonly object _lock = new();
    HidDevice? _dev; byte _tid; bool? _extended;
    readonly bool _mouse;
    readonly string _name;

    public RazerDevice(int pid, string name, bool mouse) { Pid = pid; Vid = 0x1532; _name = name; _mouse = mouse; }

    static byte[] Report(byte tid, byte cls, byte id, params byte[] args)
    {
        var b = new byte[Len];
        b[2] = tid; b[6] = (byte)args.Length; b[7] = cls; b[8] = id;
        Array.Copy(args, 0, b, 9, Math.Min(80, args.Length));
        byte crc = 0; for (int i = 3; i <= 88; i++) crc ^= b[i];      // bytes 2..87 del informe (sin el ID 0)
        b[89] = crc;
        return b;
    }

    /// <summary>Envía y lee la respuesta; devuelve null si el dispositivo no responde con «correcto».</summary>
    static byte[]? Transfer(HidStream s, byte[] req, int waitMs)
    {
        s.SetFeature(req);
        for (int t = 0; t < 6; t++)
        {
            Thread.Sleep(waitMs);
            var r = new byte[Len];
            s.GetFeature(r);
            byte st = r[1];
            if (st == 0x01) continue;                                   // ocupado: volver a leer
            if (st == 0x02 && r[7] == req[7] && r[8] == req[8]) return r;
            return null;
        }
        return null;
    }

    HidStream Open()
    {
        if (_dev != null && _dev.TryOpen(out var cached)) { cached.ReadTimeout = 600; cached.WriteTimeout = 600; return cached; }
        foreach (var d in Hid(Vid, Pid))
        {
            int fl; try { fl = d.GetMaxFeatureReportLength(); } catch { continue; }
            if (fl != Len || !d.TryOpen(out var s)) continue;
            s.ReadTimeout = 600; s.WriteTimeout = 600;
            foreach (var tid in Tids)
            {
                try
                {
                    if (Transfer(s, Report(tid, 0x00, 0x81, 0, 0), 12) != null) { _dev = d; _tid = tid; return s; }
                }
                catch { }
            }
            s.Dispose();
        }
        throw new InvalidOperationException("El periférico Razer no responde. Cierra Razer Synapse si está abierto y vuelve a intentarlo.");
    }

    T With<T>(Func<HidStream, T> fn) { lock (_lock) { using var s = Open(); return fn(s); } }
    int Wait => _tid == 0x1F || _tid == 0x9F ? 40 : 10;

    public override BrandInfo Info() => With(s =>
    {
        int? dpi = null, batt = null;
        if (_mouse)
        {
            var r = Transfer(s, Report(_tid, 0x04, 0x85, 0x00, 0, 0, 0, 0, 0, 0), Wait);
            if (r != null) dpi = r[10] << 8 | r[11];
            var b = Transfer(s, Report(_tid, 0x07, 0x80, 0, 0), Wait);
            if (b != null && (_tid == 0x1F || _tid == 0x9F)) batt = (int)Math.Round(b[10] * 100 / 255.0);
        }
        return new BrandInfo("Razer", _name, _mouse ? "mouse" : "keyboard", true,
            new[] { "static", "breathing", "spectrum", "off" }, true, false, _mouse && dpi != null,
            dpi, 100, 20000, 50, batt, null);
    });

    public override void SetLighting(LightRequest q) => With(s =>
    {
        var (r, g, b) = Rgb(q.Color);
        byte bri = (byte)Math.Round(Math.Clamp(q.Brightness, 0, 100) * 2.55);
        // LED: 0x05 retroiluminación en teclados; en ratones se prueba «todo» (0x00) y después logotipo y rueda.
        byte[] leds = _mouse ? new byte[] { 0x00, 0x04, 0x01 } : new byte[] { 0x05 };
        bool any = false;
        foreach (var led in leds)
        {
            if (_extended != false)
            {
                byte[] args = q.Effect switch
                {
                    "off" => new byte[] { 0x01, led, 0x00, 0x00, 0x00, 0x00 },
                    "spectrum" => new byte[] { 0x01, led, 0x03, 0x00, 0x00, 0x00 },
                    "breathing" => new byte[] { 0x01, led, 0x02, 0x01, 0x00, 0x01, r, g, b },
                    _ => new byte[] { 0x01, led, 0x01, 0x00, 0x00, 0x01, r, g, b },
                };
                if (Transfer(s, Report(_tid, 0x0F, 0x02, args), Wait) != null)
                {
                    _extended = true; any = true;
                    Transfer(s, Report(_tid, 0x0F, 0x04, 0x01, led, bri), Wait);
                    if (_mouse && led == 0x00) break;                   // el LED «todo» ya cubre el ratón
                    continue;
                }
            }
            // Protocolo antiguo (clase 0x03)
            byte fx = q.Effect switch { "breathing" => 2, "spectrum" => 4, _ => 0 };
            bool ok = Transfer(s, Report(_tid, 0x03, 0x00, 0x01, led, (byte)(q.Effect == "off" ? 0 : 1)), Wait) != null;
            if (q.Effect != "off")
            {
                ok |= Transfer(s, Report(_tid, 0x03, 0x01, 0x01, led, r, g, b), Wait) != null;
                ok |= Transfer(s, Report(_tid, 0x03, 0x02, 0x01, led, fx), Wait) != null;
                ok |= Transfer(s, Report(_tid, 0x03, 0x03, 0x01, led, bri), Wait) != null;
            }
            if (ok) { _extended = false; any = true; }
        }
        if (!any) throw new InvalidOperationException("Este modelo Razer no ha aceptado el cambio de iluminación.");
        return true;
    });

    public override void SetDpi(int dpi)
    {
        if (!_mouse) base.SetDpi(dpi);
        dpi = Math.Clamp(dpi / 50 * 50, 100, 20000);
        With(s =>
        {
            byte hi = (byte)(dpi >> 8), lo = (byte)dpi;
            if (Transfer(s, Report(_tid, 0x04, 0x05, 0x00, hi, lo, hi, lo, 0, 0), Wait) == null)
                throw new InvalidOperationException("El ratón Razer no ha aceptado los DPI.");
            return true;
        });
    }
}
