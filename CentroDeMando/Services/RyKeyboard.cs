namespace CentroDeMando.Services;

/// <summary>
/// Protocolo de los teclados magnéticos AJAZZ de plataforma RongYuan (familia "keyboard3123":
/// AK680 MAX y similares). Los comandos son informes de 64 bytes; el transporte es el servicio
/// local del driver (ver <see cref="IotDriver"/>), que añade el checksum y gestiona el receptor 2.4G.
/// </summary>
public sealed class RyKeyboard
{
    public const int SumBit7 = 0, SumBit8 = 1;

    const byte GET_REV = 0x8F;
    const byte SET_LEDPARAM = 0x07, GET_LEDPARAM = 0x87;
    const byte SET_KEY_MAG_MODE = 0x1D, GET_KEY_MAG_MODE = 0x9D;
    const byte SET_MAG_ARR = 0x65, GET_MAG_ARR = 0xE5;

    /// <summary>Sub-comandos de los arrays magnéticos (un valor por posición de la matriz).</summary>
    public enum MagParam : byte
    {
        Actuation = 0,   // punto de actuación (centésimas de mm)
        Release = 1,     // punto de liberación
        RtPress = 2,     // sensibilidad Rapid Trigger al pulsar
        RtRelease = 3,   // sensibilidad Rapid Trigger al soltar
        DeadZone = 6,    // zona muerta
        KeyMode = 7,     // modo de tecla (bit 7 = Rapid Trigger)
    }

    public const int MaxKeys = 128;
    const int MaxSpeed = 4;

    readonly IotDriver.Device _dev;
    readonly object _lock = new();

    public RyKeyboard(IotDriver.Device dev) { _dev = dev; }

    // ───────────── transporte ─────────────

    void Send(ReadOnlySpan<byte> payload, int sum, int preDelayMs = 10)
    {
        lock (_lock)
        {
            Thread.Sleep(preDelayMs);
            var err = IotDriver.Send(_dev, payload, sum);
            if (err != null) throw new IOException(Translate(err));
        }
    }

    byte[] Query(ReadOnlySpan<byte> payload, int sum, byte? expectEcho = null)
    {
        lock (_lock)
        {
            Exception? last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Thread.Sleep(10);
                    var err = IotDriver.Send(_dev, payload, sum);
                    if (err != null) throw new IOException(Translate(err));
                    Thread.Sleep(_dev.ViaDongle ? 25 : 8);
                    var r = IotDriver.Read(_dev, out var rerr);
                    if (r == null) throw new IOException(Translate(rerr ?? "sin respuesta"));
                    if (r.Length < 64) Array.Resize(ref r, 64);
                    if (expectEcho.HasValue && r[0] != expectEcho.Value) throw new IOException("Respuesta inesperada del teclado.");
                    return r;
                }
                catch (IOException ex) { last = ex; if (ex.Message.Contains("dormido")) break; Thread.Sleep(60); }
            }
            throw last ?? new IOException("El teclado no responde.");
        }
    }

    /// <summary>
    /// Las páginas de arrays no llevan eco del comando, así que por el receptor 2.4G no se puede distinguir
    /// una respuesta atrasada. Se repite la consulta hasta obtener dos respuestas seguidas idénticas.
    /// </summary>
    byte[] QueryStable(ReadOnlySpan<byte> payload, int sum)
    {
        var q = payload.ToArray();
        byte[] prev = Query(q, sum);
        for (int i = 0; i < 4; i++)
        {
            byte[] next = Query(q, sum);
            if (next.AsSpan().SequenceEqual(prev)) return next;
            prev = next;
        }
        throw new IOException("El teclado devuelve datos inconsistentes; vuelve a intentarlo.");
    }

    static string Translate(string err) =>
        err.Contains("not online", StringComparison.OrdinalIgnoreCase)
            ? "El teclado está dormido o apagado: pulsa una tecla para despertarlo."
            : "Error del teclado: " + err;

    /// <summary>Tras escribir, la app oficial pausa un instante el sondeo inalámbrico para que el teclado guarde.</summary>
    void Settle()
    {
        if (!_dev.ViaDongle) { Thread.Sleep(50); return; }
        IotDriver.SetWirelessLoopLock(true);
        Thread.Sleep(60);
        IotDriver.SetWirelessLoopLock(false);
        Thread.Sleep(60);
    }

    // ───────────── información ─────────────

    public (uint id, int firmware) GetInfo()
    {
        var q = new byte[64]; q[0] = GET_REV;
        var r = Query(q, SumBit7, GET_REV);
        return (BitConverter.ToUInt32(r, 1), BitConverter.ToUInt16(r, 7));
    }

    // ───────────── iluminación ─────────────

    public sealed record Led(int Mode, int Speed, int Brightness, int Option, bool Dazzle, int R, int G, int B);

    public Led GetLed()
    {
        var q = new byte[64]; q[0] = GET_LEDPARAM;
        var a = Query(q, SumBit7, GET_LEDPARAM);
        int opt = a[4];
        int r = a[5], g = a[6], b = a[7];
        if (r == 0xFA && g == 0xFF && b == 0xFA) { r = g = b = 255; }
        return new Led(a[1], Math.Clamp(MaxSpeed - a[2], 0, MaxSpeed), a[3], opt >> 4, (opt & 0x0F) == 8, r, g, b);
    }

    public void SetLed(Led led)
    {
        var a = new byte[64];
        a[0] = SET_LEDPARAM;
        a[1] = (byte)Math.Clamp(led.Mode, 0, 25);
        a[2] = (byte)(MaxSpeed - Math.Clamp(led.Speed, 0, MaxSpeed));
        a[3] = (byte)Math.Clamp(led.Brightness, 0, 4);
        a[4] = (byte)(((led.Option & 0x0F) << 4) | (led.Dazzle ? 8 : 7));
        int r = led.R, g = led.G, b = led.B;
        if (r == 255 && g == 255 && b == 255) { r = 0xFA; g = 0xFF; b = 0xFA; }   // el firmware reserva el blanco puro
        a[5] = (byte)r; a[6] = (byte)g; a[7] = (byte)b;
        Send(a, SumBit8, 50);
        Settle();
    }

    // ───────────── modo magnético global ─────────────

    public (int mode, bool keyboardDisable, bool gamepadEnable) GetMagMode()
    {
        var q = new byte[64]; q[0] = GET_KEY_MAG_MODE;
        var a = Query(q, SumBit7, GET_KEY_MAG_MODE);
        return (a[1], a[2] == 1, a[3] == 1);
    }

    public void SetMagMode(int mode, bool keyboardDisable, bool gamepadEnable)
    {
        var a = new byte[64];
        a[0] = SET_KEY_MAG_MODE; a[1] = (byte)mode; a[2] = (byte)(keyboardDisable ? 1 : 0); a[3] = (byte)(gamepadEnable ? 1 : 0);
        Send(a, SumBit7);
    }

    // ───────────── arrays por tecla ─────────────

    public ushort[] ReadArray16(MagParam p)
    {
        var raw = new byte[256];
        var q = new byte[8]; q[0] = GET_MAG_ARR; q[1] = (byte)p; q[2] = 1;
        for (int page = 0; page < 4; page++)
        {
            q[3] = (byte)page;
            var r = QueryStable(q, SumBit7);
            Array.Copy(r, 0, raw, page * 64, 64);
        }
        var vals = new ushort[MaxKeys];
        for (int i = 0; i < MaxKeys; i++) vals[i] = BitConverter.ToUInt16(raw, i * 2);
        return vals;
    }

    public byte[] ReadArray8(MagParam p)
    {
        var raw = new byte[128];
        var q = new byte[8]; q[0] = GET_MAG_ARR; q[1] = (byte)p; q[2] = 1;
        for (int page = 0; page < 2; page++)
        {
            q[3] = (byte)page;
            var r = QueryStable(q, SumBit7);
            Array.Copy(r, 0, raw, page * 64, 64);
        }
        return raw;
    }

    /// <summary>Escribe un array de 16 bits; cada paquete lleva 28 teclas (56 bytes).</summary>
    public void WriteArray16(MagParam p, ushort[] vals, int keyCount, bool commit)
    {
        var raw = new byte[5 * 56];
        for (int i = 0; i < MaxKeys; i++) BitConverter.TryWriteBytes(raw.AsSpan(i * 2, 2), vals[i]);
        int lastPage = Math.Clamp((keyCount - 1) / 28, 0, 4);
        for (int page = 0; page <= lastPage; page++)
        {
            var pkt = new byte[64];
            pkt[0] = SET_MAG_ARR; pkt[1] = (byte)p; pkt[2] = 1; pkt[3] = (byte)page;
            pkt[4] = (byte)(commit && page == lastPage ? 1 : 0);
            Array.Copy(raw, page * 56, pkt, 8, 56);
            Send(pkt, SumBit7, 15);
        }
    }

    /// <summary>Escribe un array de 8 bits; cada paquete lleva 56 teclas.</summary>
    public void WriteArray8(MagParam p, byte[] vals, int keyCount, bool commit)
    {
        var raw = new byte[3 * 56];
        Array.Copy(vals, raw, Math.Min(vals.Length, MaxKeys));
        int lastPage = Math.Clamp((keyCount - 1) / 56, 0, 2);
        for (int page = 0; page <= lastPage; page++)
        {
            var pkt = new byte[64];
            pkt[0] = SET_MAG_ARR; pkt[1] = (byte)p; pkt[2] = 1; pkt[3] = (byte)page;
            pkt[4] = (byte)(commit && page == lastPage ? 1 : 0);
            Array.Copy(raw, page * 56, pkt, 8, 56);
            Send(pkt, SumBit7, 15);
        }
    }

    public void FinishWrite() => Settle();
}

/// <summary>Distribución física de un modelo: posición en la matriz del firmware + etiqueta + ancho.</summary>
public sealed record KeyDef(int Index, string Label, int Row, double X, double W);

public static class KeyboardModels
{
    /// <summary>Nombre comercial por id de dispositivo (el bit alto indica conexión por receptor).</summary>
    public static string Name(uint id) => (id & 0x7FFFFFFF) switch
    {
        2343 or 2371 or 2336 or 2317 or 2255 or 2264 => "AJAZZ AK680 MAX",
        2230 or 1694 => "AJAZZ AK820 MAX",
        1773 => "AJAZZ AK870",
        2405 or 2404 or 2403 or 2303 => "AJAZZ ALUX68",
        var n => $"AJAZZ (modelo {n})",
    };

    public static bool HasLayout(uint id) => (id & 0x7FFFFFFF) is 2343 or 2336;

    // Matriz del AK680 MAX (ry5088_sg9005): 6 filas por columna; índice = columna*6 + fila.
    // Código HID por posición (0 = hueco). 1000 = tecla Fn.
    static readonly int[][] Ak680Rows =
    {
        //  fila 1: Esc 1..0 - = Retroceso Inicio
        new[] { 41, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 45, 46, 42, 74 },
        new[] { 43, 20, 26, 8, 21, 23, 28, 24, 12, 18, 19, 47, 48, 49, 76 },
        new[] { 57, 4, 22, 7, 9, 10, 11, 13, 14, 15, 51, 52, 0, 40, 75 },
        new[] { 225, 0, 29, 27, 6, 25, 5, 17, 16, 54, 55, 56, 229, 82, 78 },
        new[] { 224, 0, 227, 226, 0, 0, 44, 0, 0, 230, 1000, 228, 80, 81, 79 },
    };

    static (string label, double w) Describe(int code) => code switch
    {
        41 => ("Esc", 1), 42 => ("⌫", 2), 43 => ("Tab", 1.5), 44 => ("Espacio", 6.25), 40 => ("Intro", 2.25),
        57 => ("Bloq", 1.75), 225 => ("Shift", 2.25), 229 => ("Shift", 1.75),
        224 => ("Ctrl", 1.25), 227 => ("Win", 1.25), 226 => ("Alt", 1.25), 230 => ("Alt", 1), 228 => ("Ctrl", 1), 1000 => ("Fn", 1),
        74 => ("Inicio", 1), 76 => ("Supr", 1), 75 => ("RePág", 1), 78 => ("AvPág", 1),
        82 => ("↑", 1), 80 => ("←", 1), 81 => ("↓", 1), 79 => ("→", 1),
        45 => ("-", 1), 46 => ("=", 1), 47 => ("[", 1), 48 => ("]", 1), 49 => ("\\", 1.5), 51 => (";", 1), 52 => ("'", 1),
        54 => (",", 1), 55 => (".", 1), 56 => ("/", 1),
        >= 30 and <= 38 => (((char)('1' + code - 30)).ToString(), 1), 39 => ("0", 1),
        >= 4 and <= 29 => (((char)('A' + code - 4)).ToString(), 1),
        _ => ("?", 1),
    };

    public static List<KeyDef> Ak680()
    {
        var list = new List<KeyDef>();
        for (int row = 0; row < Ak680Rows.Length; row++)
        {
            double x = 0;
            for (int col = 0; col < Ak680Rows[row].Length; col++)
            {
                int code = Ak680Rows[row][col];
                if (code == 0) continue;
                var (label, w) = Describe(code);
                list.Add(new KeyDef(col * 6 + (row + 1), label, row, x, w));
                x += w;
            }
        }
        return list;
    }
}
