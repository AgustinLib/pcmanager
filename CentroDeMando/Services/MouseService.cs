using System.Text.Json;
using HidSharp;

namespace CentroDeMando.Services;

/// <summary>
/// Ratones con chip Compx (VID 373B: VXE, ATK, VGN, MCHOSE…), por cable o con su receptor 2,4 G.
/// Protocolo del configurador web oficial: informes de salida/entrada de 17 bytes con ID 8:
///   [08, cmd, estado, dirección (2, big endian), longitud, datos×10, checksum = 0x55 − suma(bytes 0..15)].
/// Cada valor de la EEPROM va acompañado de su byte de control (0x55 − valor).
/// Antes de escribir se lee siempre el bloque completo y se comprueba que sea válido.
/// </summary>
public sealed class MouseService
{
    const int Vid = 0x373B, ReportLen = 17;
    const byte CmdOnline = 0x03, CmdBattery = 0x04, CmdSetEe = 0x07, CmdGetEe = 0x08, CmdCidMid = 0x10, CmdVersion = 0x12;
    const int AddrMain = 0x00, AddrLod = 0x0A, AddrDpi = 0x0C, AddrFunc = 0xA9, AddrSensor = 0xB5;

    readonly object _lock = new();

    public static readonly int[] PollRates = { 125, 250, 500, 1000 };
    static readonly Dictionary<int, byte> RateCode = new() { [125] = 0x08, [250] = 0x04, [500] = 0x02, [1000] = 0x01, [2000] = 0x10, [4000] = 0x20, [8000] = 0x40 };
    public static readonly int[] Debounces = { 0, 1, 2, 4, 8, 15, 20 };
    public static readonly int[] SleepTimes = { 60, 180, 300, 600, 1800 };
    static readonly Dictionary<double, byte> LodCode = new() { [0.7] = 3, [1] = 1, [2] = 2 };

    /// <summary>Tabla DPI → código del sensor PAW3395SE (copiada del configurador oficial).</summary>
    static readonly (int Dpi, byte Code)[] Paw3395Se =
    {
       (50,1), (100,2), (150,3), (200,4), (250,5), (300,6), (350,8), (400,9), (450,10), (500,11)
       , (550,12), (600,14), (650,15), (700,16), (750,17), (800,18), (850,19), (900,21), (950,22), (1000,23)
       , (1050,24), (1100,25), (1150,27), (1200,28), (1250,29), (1300,30), (1350,31), (1400,32), (1450,34), (1500,35)
       , (1550,36), (1600,37), (1650,38), (1700,39), (1750,41), (1800,42), (1850,43), (1900,44), (1950,45), (2000,47)
       , (2050,48), (2100,49), (2150,50), (2200,51), (2250,52), (2300,54), (2350,55), (2400,56), (2450,57), (2500,58)
       , (2550,59), (2600,61), (2650,62), (2700,63), (2750,64), (2800,65), (2850,67), (2900,68), (2950,69), (3000,70)
       , (3050,71), (3100,72), (3150,74), (3200,75), (3250,76), (3300,77), (3350,78), (3400,79), (3450,81), (3500,82)
       , (3550,83), (3600,84), (3650,85), (3700,87), (3750,88), (3800,89), (3850,90), (3900,91), (3950,92), (4000,94)
       , (4050,95), (4100,96), (4150,97), (4200,98), (4250,99), (4300,101), (4350,102), (4400,103), (4450,104), (4500,105)
       , (4550,107), (4600,108), (4650,109), (4700,110), (4750,111), (4800,112), (4850,114), (4900,115), (4950,116), (5000,117)
       , (5050,118), (5100,119), (5150,121), (5200,122), (5250,123), (5300,124), (5350,125), (5400,127), (5450,128), (5500,129)
       , (5550,130), (5600,131), (5650,132), (5700,134), (5750,135), (5800,136), (5850,137), (5900,138), (5950,139), (6000,141)
       , (6050,142), (6100,143), (6150,144), (6200,145), (6250,147), (6300,148), (6350,149), (6400,150), (6450,151), (6500,152)
       , (6550,154), (6600,155), (6650,156), (6700,157), (6750,158), (6800,159), (6850,161), (6900,162), (6950,163), (7000,164)
       , (7050,165), (7100,167), (7150,168), (7200,169), (7250,170), (7300,171), (7350,172), (7400,174), (7450,175), (7500,176)
       , (7550,177), (7600,178), (7650,179), (7700,181), (7750,182), (7800,183), (7850,184), (7900,185), (7950,187), (8000,188)
       , (8050,189), (8100,190), (8150,191), (8200,192), (8250,194), (8300,195), (8350,196), (8400,197), (8450,198), (8500,199)
       , (8550,201), (8600,202), (8650,203), (8700,204), (8750,205), (8800,207), (8850,208), (8900,209), (8950,210), (9000,211)
       , (9050,212), (9100,214), (9150,215), (9200,216), (9250,217), (9300,218), (9350,219), (9400,221), (9450,222), (9500,223)
       , (9550,224), (9600,225), (9650,227), (9700,228), (9750,229), (9800,230), (9850,231), (9900,232), (9950,234), (10000,235)
       
    };

    /// <summary>(cid, mid) → nombre del modelo y si usa la tabla del PAW3395SE.</summary>
    static string? ModelName(int cid, int mid) => (cid, mid) switch
    {
        (2, 31) or (2, 32) or (2, 116) or (2, 136) => "VXE Dragonfly R1 SE+",
        (2, 130) or (2, 137) => "VXE Dragonfly R1 SE+ Silent",
        _ => null,
    };
    static bool UsesPawTable(int cid, int mid) => cid == 2 && mid is 31 or 32 or 116 or 130;

    // ───────────── transporte ─────────────

    static HidDevice? FindDevice()
    {
        foreach (var d in DeviceList.Local.GetHidDevices(Vid))
        {
            try { if (d.GetMaxOutputReportLength() == ReportLen && d.GetMaxInputReportLength() == ReportLen) return d; } catch { }
        }
        return null;
    }

    static byte[] Packet(byte cmd, int addr, int len, ReadOnlySpan<byte> data)
    {
        var b = new byte[ReportLen];
        b[0] = 8; b[1] = cmd; b[3] = (byte)(addr >> 8); b[4] = (byte)addr; b[5] = (byte)len;
        data.Slice(0, Math.Min(10, data.Length)).CopyTo(b.AsSpan(6));
        int sum = 0; for (int i = 0; i < 16; i++) sum += b[i];
        b[16] = (byte)(0x55 - (sum & 0xFF));
        return b;
    }

    /// <summary>Envía un comando y devuelve la respuesta (17 bytes) con el mismo código.</summary>
    static byte[] Transfer(HidStream s, byte cmd, int addr = 0, int len = 0, ReadOnlySpan<byte> data = default)
    {
        s.Write(Packet(cmd, addr, len, data));
        var until = DateTime.UtcNow.AddMilliseconds(700);
        var r = new byte[ReportLen];
        while (DateTime.UtcNow < until)
        {
            int n;
            try { n = s.Read(r, 0, ReportLen); } catch (TimeoutException) { break; }
            if (n >= 16 && r[0] == 8 && r[1] == cmd && (len == 0 || (r[3] << 8 | r[4]) == addr)) return (byte[])r.Clone();
        }
        throw new InvalidOperationException("El ratón no responde. Muévelo para despertarlo y vuelve a intentarlo.");
    }

    T WithStream<T>(Func<HidStream, T> fn)
    {
        lock (_lock)
        {
            var dev = FindDevice() ?? throw new InvalidOperationException("No se ha encontrado ningún ratón compatible.");
            using var s = dev.Open();
            s.ReadTimeout = 250; s.WriteTimeout = 500;
            return fn(s);
        }
    }

    static byte[] ReadBlock(HidStream s, int addr, int len)
    {
        var r = Transfer(s, CmdGetEe, addr, len);
        if (r[2] != 0) throw new InvalidOperationException("El ratón no ha podido leer su configuración.");
        return r.AsSpan(6, len).ToArray();
    }

    static void WriteBlock(HidStream s, int addr, byte[] data)
    {
        var r = Transfer(s, CmdSetEe, addr, data.Length, data);
        if (r[2] != 0) throw new InvalidOperationException("El ratón ha rechazado el cambio.");
        var back = ReadBlock(s, addr, data.Length);
        if (!back.AsSpan().SequenceEqual(data)) throw new InvalidOperationException("El ratón no ha guardado el cambio correctamente.");
    }

    /// <summary>Bloque de pares [valor, 0x55 − valor]: comprueba que todos los pares sean coherentes.</summary>
    static bool PairsValid(byte[] b, int pairs)
    {
        for (int i = 0; i < pairs; i++) if ((byte)(b[2 * i] + b[2 * i + 1]) != 0x55) return false;
        return true;
    }
    static void SetPair(byte[] b, int i, int v) { b[2 * i] = (byte)v; b[2 * i + 1] = (byte)(0x55 - v); }

    // ───────────── API ─────────────

    public object Status()
    {
        if (FindDevice() == null) return new { found = false, online = false };
        try
        {
            return WithStream<object>(s =>
            {
                var on = Transfer(s, CmdOnline);
                bool online = on[6] == 1;
                if (!online) return new { found = true, online = false, model = (string?)null };
                var bat = Transfer(s, CmdBattery);
                var id = Transfer(s, CmdCidMid);
                string? fw = null;
                try { var v = Transfer(s, CmdVersion); fw = $"{v[6]}.{v[7]:D2}"; } catch { }
                int cid = id[6], mid = id[7];
                return new
                {
                    found = true, online = true,
                    model = ModelName(cid, mid) ?? $"Ratón Compx ({cid}/{mid})",
                    battery = (int)bat[6], charging = bat[7] == 1, firmware = fw,
                };
            });
        }
        catch (Exception ex) { return new { found = true, online = false, error = ex.Message }; }
    }

    public object Read() => WithStream<object>(s =>
    {
        var id = Transfer(s, CmdCidMid);
        bool paw = UsesPawTable(id[6], id[7]);
        var main = ReadBlock(s, AddrMain, 10);
        var lod = ReadBlock(s, AddrLod, 2);
        var func = ReadBlock(s, AddrFunc, 10);
        if (!PairsValid(main, 5) || !PairsValid(func, 5)) throw new InvalidOperationException("La configuración leída del ratón no es válida.");
        var stages = new List<int?>();
        for (int k = 0; k < 4; k++)
        {
            var pair = ReadBlock(s, AddrDpi + 8 * k, 8);
            for (int j = 0; j < 2; j++) stages.Add(paw ? DecodeDpi(pair.AsSpan(4 * j, 4)) : null);
        }
        int rate = RateCode.FirstOrDefault(kv => kv.Value == main[0]).Key;
        return new
        {
            pollRate = rate, pollRates = PollRates,
            stageCount = (int)main[2], activeStage = (int)main[4],
            stages, dpiEditable = paw, dpiMin = 50, dpiMax = 10000, dpiStep = 50,
            lod = LodCode.FirstOrDefault(kv => kv.Value == lod[0]).Key,
            debounce = (int)func[0], debounces = Debounces,
            motionSync = func[2] == 1, sleep = func[4] * 10, sleepTimes = SleepTimes,
            angleSnap = func[6] == 1, ripple = func[8] == 1,
        };
    });

    static int? DecodeDpi(ReadOnlySpan<byte> st)
    {
        if ((byte)(0x55 - (st[0] + st[1] + st[2])) != st[3] || st[2] != 0) return null;
        foreach (var (dpi, code) in Paw3395Se) if (code == st[0]) return dpi;
        return null;
    }
    static byte[] EncodeDpi(int dpi)
    {
        var (_, code) = Paw3395Se.MinBy(e => Math.Abs(e.Dpi - dpi));
        return new byte[] { code, code, 0, (byte)(0x55 - (code * 2)) };
    }

    /// <summary>Aplica solo los campos presentes en la petición.</summary>
    public object Apply(JsonElement a)
    {
        int? I(string n) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
        double? D(string n) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        bool? B(string n) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        List<int>? stagesIn = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("stages", out var sv) && sv.ValueKind == JsonValueKind.Array
            ? sv.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (int)x.GetDouble()).ToList() : null;

        // Validación previa: nada se envía si algún valor está fuera de rango.
        int? rate = I("pollRate"), active = I("activeStage"), deb = I("debounce"), sleep = I("sleep");
        double? lod = D("lod");
        if (rate != null && !PollRates.Contains(rate.Value)) throw new ArgumentException("Frecuencia de sondeo no válida.");
        if (deb != null && !Debounces.Contains(deb.Value)) throw new ArgumentException("Tiempo de rebote no válido.");
        if (sleep != null && !SleepTimes.Contains(sleep.Value)) throw new ArgumentException("Tiempo de reposo no válido.");
        if (lod != null && !LodCode.ContainsKey(lod.Value)) throw new ArgumentException("Distancia de levantamiento no válida.");
        if (stagesIn != null && (stagesIn.Count is < 1 or > 8 || stagesIn.Any(d => d is < 50 or > 10000))) throw new ArgumentException("Niveles de DPI no válidos (de 1 a 8 niveles, entre 50 y 10 000).");

        return WithStream<object>(s =>
        {
            var id = Transfer(s, CmdCidMid);
            if (stagesIn != null && !UsesPawTable(id[6], id[7])) throw new InvalidOperationException("Este modelo de ratón no permite cambiar los DPI desde la app.");

            // Bloque principal: frecuencia, número de niveles y nivel activo.
            if (rate != null || active != null || stagesIn != null)
            {
                var main = ReadBlock(s, AddrMain, 10);
                if (!PairsValid(main, 5)) throw new InvalidOperationException("La configuración leída del ratón no es válida; no se ha cambiado nada.");
                var nb = (byte[])main.Clone();
                int count = stagesIn?.Count ?? main[2];
                if (rate != null) SetPair(nb, 0, RateCode[rate.Value]);
                if (stagesIn != null) SetPair(nb, 1, count);
                int act = Math.Clamp(active ?? main[4], 0, count - 1);
                SetPair(nb, 2, act);

                if (stagesIn != null)
                {
                    for (int k = 0; k < (count + 1) / 2; k++)
                    {
                        var pair = ReadBlock(s, AddrDpi + 8 * k, 8);
                        var np = (byte[])pair.Clone();
                        for (int j = 0; j < 2; j++)
                        {
                            int idx = 2 * k + j;
                            if (idx < count) EncodeDpi(stagesIn[idx]).CopyTo(np, 4 * j);
                        }
                        if (!np.AsSpan().SequenceEqual(pair)) WriteBlock(s, AddrDpi + 8 * k, np);
                    }
                }
                if (!nb.AsSpan().SequenceEqual(main)) WriteBlock(s, AddrMain, nb);
            }

            if (lod != null)
            {
                var cur = ReadBlock(s, AddrLod, 2);
                var nb = new byte[2]; SetPair(nb, 0, LodCode[lod.Value]);
                if (!nb.AsSpan().SequenceEqual(cur)) WriteBlock(s, AddrLod, nb);
            }

            bool? ms = B("motionSync"), snap = B("angleSnap"), rip = B("ripple");
            if (deb != null || sleep != null || ms != null || snap != null || rip != null)
            {
                var func = ReadBlock(s, AddrFunc, 10);
                if (!PairsValid(func, 5)) throw new InvalidOperationException("La configuración leída del ratón no es válida; no se ha cambiado nada.");
                var nb = (byte[])func.Clone();
                if (deb != null) SetPair(nb, 0, deb.Value);
                if (ms != null) SetPair(nb, 1, ms.Value ? 1 : 0);
                if (sleep != null) SetPair(nb, 2, sleep.Value / 10);
                if (snap != null) SetPair(nb, 3, snap.Value ? 1 : 0);
                if (rip != null) SetPair(nb, 4, rip.Value ? 1 : 0);
                if (!nb.AsSpan().SequenceEqual(func)) WriteBlock(s, AddrFunc, nb);

                // El configurador oficial guarda también el reposo del sensor con el mismo tiempo.
                if (sleep != null)
                {
                    var sen = ReadBlock(s, AddrSensor, 6);
                    if (PairsValid(sen, 3))
                    {
                        var ns = (byte[])sen.Clone();
                        SetPair(ns, 0, 1); SetPair(ns, 1, sleep.Value / 10);
                        if (!ns.AsSpan().SequenceEqual(sen)) WriteBlock(s, AddrSensor, ns);
                    }
                }
            }
            return true;
        });
    }
}
