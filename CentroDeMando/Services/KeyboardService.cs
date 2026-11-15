using System.Globalization;
using System.Text.Json;

namespace CentroDeMando.Services;

/// <summary>Estado, iluminación y recorrido (actuación / Rapid Trigger) del teclado AJAZZ.</summary>
public sealed class KeyboardService : IDisposable
{
    public sealed record Status(bool Found, bool Online, string Model, bool Wireless, int? Battery, string? Firmware, string? Error);
    public sealed record LedDto(int Mode, int Speed, int Brightness, int Option, bool Dazzle, string Color);
    public sealed record LedOption(int Id, string Name);
    public sealed record LedMode(int Id, string Name, bool HasColor, bool HasSpeed, LedOption[]? Options);
    public sealed record Limits(double ActMin, double ActMax, double RtMin, double RtMax, double DzMin, double DzMax, double Step);
    public sealed record KeyDto(int I, string Label, int Row, double X, double W, double Actuation, bool Rt, double RtPress, double RtRelease, double Deadzone);
    public sealed record ReadResult(LedDto Led, LedMode[] LedModes, Limits Limits, KeyDto[] Keys);

    static readonly Limits Lim = new(0.1, 3.4, 0.05, 2.0, 0, 1.0, 0.01);
    const int KeyCount = 90;      // posiciones de matriz usadas por el AK680 MAX

    static readonly LedOption[] Dir4 = { new(0, "Derecha"), new(1, "Izquierda"), new(2, "Abajo"), new(3, "Arriba") };
    static readonly LedOption[] DirLR = { new(0, "Derecha"), new(1, "Izquierda") };

    public static readonly LedMode[] Modes =
    {
        new(0, "Apagado", false, false, null),
        new(1, "Color fijo", true, false, null),
        new(2, "Respiración", true, true, null),
        new(3, "Neón", false, true, null),
        new(4, "Onda", true, true, Dir4),
        new(5, "Ondulación", true, true, new LedOption[] { new(0, "Completa"), new(1, "Una tecla") }),
        new(6, "Gotas de lluvia", true, true, null),
        new(7, "Serpiente", true, true, new LedOption[] { new(0, "Zigzag"), new(1, "Ida y vuelta") }),
        new(8, "Reactivo (al pulsar)", true, true, new LedOption[] { new(0, "Se apaga"), new(1, "Se enciende") }),
        new(9, "Convergencia", true, true, null),
        new(10, "Onda sinusoidal", true, true, null),
        new(11, "Caleidoscopio", true, true, new LedOption[] { new(0, "Hacia fuera"), new(1, "Hacia dentro") }),
        new(12, "Onda lineal", true, true, DirLR),
        new(14, "Láser", true, true, null),
        new(15, "Onda circular", true, true, new LedOption[] { new(0, "Antihorario"), new(1, "Horario") }),
        new(16, "Destellos", true, true, null),
        new(17, "Lluvia", true, true, null),
        new(18, "Meteoro", true, true, null),
        new(19, "Reactivo apagado", true, true, null),
        new(23, "Tren", true, true, null),
        new(24, "Fuegos artificiales", true, true, DirLR),
    };

    readonly object _lock = new();
    readonly System.Threading.Timer _timer;
    Status _status = new(false, false, "", false, null, null, null);
    string? _firmware;
    uint _firmwareFor;
    DateTime _lastStartTry;

    public event Action<Status>? StatusChanged;

    public KeyboardService()
    {
        // El primer sondeo es real; después solo se sondea al pedirlo la interfaz o mientras se espera a que despierte
        // (sondear sin parar mantendría despierta la radio del teclado y gastaría batería).
        bool first = true;
        _timer = new System.Threading.Timer(_ => { try { Refresh(first); first = false; } catch { } }, null, 1500, 3000);
    }

    IotDriver.Device? FindDevice(out string? error)
    {
        error = null;
        if (!IotDriver.IsServiceRunning())
        {
            // Si el servicio se cierra (por ejemplo, al cerrar la app de AJAZZ) se vuelve a arrancar, como mucho cada 15 s.
            if (DateTime.UtcNow - _lastStartTry < TimeSpan.FromSeconds(15)) { error = "El servicio del driver de AJAZZ no está en marcha."; return null; }
            _lastStartTry = DateTime.UtcNow;
            if (!IotDriver.EnsureRunning(out error)) return null;
        }
        List<IotDriver.Device>? devs;
        try { devs = IotDriver.GetDevices(); }
        catch (Exception ex) { error = "No se pudo contactar con el servicio del driver: " + ex.Message; return null; }

        // El servicio arranca con el sondeo inalámbrico bloqueado y, hasta que alguien lo desbloquea (la app oficial
        // lo hace al abrirse), no identifica el teclado que hay detrás del receptor 2.4G.
        if (devs != null && devs.Any(d => d.ViaDongle && d.Id == 0))
        {
            IotDriver.SetWirelessLoopLock(false);
            for (int i = 0; i < 14 && devs.Any(d => d.ViaDongle && d.Id == 0); i++)
            {
                Thread.Sleep(250);
                try { devs = IotDriver.GetDevices() ?? devs; } catch { }
            }
            // Último recurso: preguntar al propio teclado su identificador.
            for (int k = 0; k < devs.Count; k++)
            {
                var d = devs[k];
                if (!d.ViaDongle || d.Id != 0) continue;
                try { var (id, _) = new RyKeyboard(d).GetInfo(); if (id != 0) devs[k] = d with { Id = id | 0x80000000u, Online = true }; }
                catch { }
            }
            devs = devs.Where(d => d.Id != 0).ToList();
        }
        if (devs == null || devs.Count == 0) { error = "No se detecta ningún teclado AJAZZ. Conecta el cable o el receptor 2.4G y pulsa una tecla."; return null; }
        // Preferimos el que esté en línea; a igualdad, por cable.
        return devs.OrderByDescending(d => d.Online).ThenBy(d => d.ViaDongle).First();
    }

    bool _awake;                    // resultado del último sondeo real
    DateTime _wakeWatchUntil;       // mientras no pase esta hora, el temporizador sondea para detectar que despierta

    /// <summary>
    /// Estado del teclado. El servicio del driver marca "en línea" aunque el teclado inalámbrico se haya
    /// dormido, así que con <paramref name="probe"/> se le pregunta de verdad (un comando de lectura).
    /// </summary>
    public Status Refresh(bool probe = true)
    {
        Status s;
        lock (_lock)
        {
            var dev = FindDevice(out var err);
            if (dev == null) { _awake = false; s = new Status(false, false, "", false, null, null, err); }
            else
            {
                if (!dev.Online) _awake = false;
                else if (probe || DateTime.UtcNow < _wakeWatchUntil)
                {
                    try
                    {
                        var (_, fw) = new RyKeyboard(dev).GetInfo();
                        _firmware = $"{fw >> 8:X}.{fw & 0xFF:X2}"; _firmwareFor = dev.Id;
                        _awake = true; _wakeWatchUntil = default;
                    }
                    catch { _awake = false; }
                }
                s = new Status(true, _awake, KeyboardModels.Name(dev.Id), dev.ViaDongle,
                    dev.ViaDongle || dev.Battery > 0 ? dev.Battery : null, _firmwareFor == dev.Id ? _firmware : null, null);
            }
        }
        if (s != _status) { _status = s; StatusChanged?.Invoke(s); }
        return s;
    }

    /// <summary>Marca el teclado como dormido y vigila unos minutos para avisar a la interfaz cuando despierte.</summary>
    void NoteAsleep()
    {
        _awake = false;
        _wakeWatchUntil = DateTime.UtcNow.AddMinutes(5);
        var s = _status with { Online = false };
        if (s != _status) { _status = s; StatusChanged?.Invoke(s); }
    }

    T Guard<T>(Func<T> f)
    {
        try { return f(); }
        catch (Exception ex) when (ex.Message.Contains("dormido")) { NoteAsleep(); throw; }
    }

    RyKeyboard Open(out IotDriver.Device dev)
    {
        var d = FindDevice(out var err) ?? throw new InvalidOperationException(err ?? "Teclado no encontrado.");
        if (!d.Online) throw new InvalidOperationException("El teclado está dormido o apagado: pulsa una tecla para despertarlo.");
        dev = d;
        return new RyKeyboard(d);
    }

    static string Hex(int r, int g, int b) => $"#{r:x2}{g:x2}{b:x2}";
    static (int r, int g, int b) ParseHex(string? s)
    {
        s = (s ?? "#ffffff").TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) v = 0xFFFFFF;
        return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }
    static double Mm(ushort v) => Math.Round(v / 100.0, 2);
    static ushort Raw(double mm, double min, double max) => (ushort)Math.Round(Math.Clamp(mm, min, max) * 100.0);

    public ReadResult Read() => Guard(() =>
    {
        lock (_lock)
        {
            var kb = Open(out var dev);
            var r = ReadCore(kb, dev);
            _awake = true;
            return r;
        }
    });

    public void SetLed(JsonElement a) => Guard(() => { SetLedCore(a); return true; });
    public ReadResult Apply(JsonElement a) => Guard(() => ApplyCore(a));

    ReadResult ReadCore(RyKeyboard kb, IotDriver.Device dev)
    {
        var led = kb.GetLed();
        var act = kb.ReadArray16(RyKeyboard.MagParam.Actuation);
        var rtp = kb.ReadArray16(RyKeyboard.MagParam.RtPress);
        var rtr = kb.ReadArray16(RyKeyboard.MagParam.RtRelease);
        var dz = kb.ReadArray16(RyKeyboard.MagParam.DeadZone);
        var mode = kb.ReadArray8(RyKeyboard.MagParam.KeyMode);
        var layout = KeyboardModels.HasLayout(dev.Id) ? KeyboardModels.Ak680() : GenericLayout(act);
        var keys = layout.Select(k => new KeyDto(k.Index, k.Label, k.Row, k.X, k.W,
            Mm(act[k.Index]), (mode[k.Index] & 0x80) != 0, Mm(rtp[k.Index]), Mm(rtr[k.Index]), Mm(dz[k.Index]))).ToArray();
        return new ReadResult(new LedDto(led.Mode, led.Speed, led.Brightness, led.Option, led.Dazzle, Hex(led.R, led.G, led.B)), Modes, Lim, keys);
    }

    /// <summary>Para modelos sin distribución conocida: una rejilla con las posiciones de la matriz.</summary>
    static List<KeyDef> GenericLayout(ushort[] act)
    {
        var list = new List<KeyDef>();
        for (int i = 0; i < KeyCount; i++)
        {
            int col = i / 6, row = i % 6;
            if (row == 0 || act[i] == 0) continue;
            list.Add(new KeyDef(i, (i).ToString(), row - 1, col, 1));
        }
        return list;
    }

    void SetLedCore(JsonElement a)
    {
        lock (_lock)
        {
            var kb = Open(out _);
            var (r, g, b) = ParseHex(a.TryGetProperty("color", out var c) ? c.GetString() : null);
            int I(string n, int def) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
            bool dazzle = a.TryGetProperty("dazzle", out var dz) && dz.ValueKind == JsonValueKind.True;
            int mode = I("mode", 1);
            if (!Modes.Any(m => m.Id == mode)) throw new ArgumentException("Modo de iluminación no válido.");
            kb.SetLed(new RyKeyboard.Led(mode, I("speed", 2), I("brightness", 4), I("option", 0), dazzle, r, g, b));
        }
    }

    ReadResult ApplyCore(JsonElement a)
    {
        lock (_lock)
        {
            var kb = Open(out var dev);
            var valid = (KeyboardModels.HasLayout(dev.Id) ? KeyboardModels.Ak680().Select(k => k.Index) : Enumerable.Range(0, KeyCount)).ToHashSet();
            var sel = new List<int>();
            if (a.TryGetProperty("keys", out var ks) && ks.ValueKind == JsonValueKind.Array)
                foreach (var k in ks.EnumerateArray()) { int i = k.GetInt32(); if (valid.Contains(i)) sel.Add(i); }
            if (sel.Count == 0) throw new ArgumentException("No hay teclas seleccionadas.");

            double? D(string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            bool? rt = a.TryGetProperty("rt", out var rv) ? rv.ValueKind == JsonValueKind.True ? true : rv.ValueKind == JsonValueKind.False ? false : null : null;
            double? actuation = D("actuation"), rtPress = D("rtPress"), rtRelease = D("rtRelease"), deadzone = D("deadzone");

            // Leer–modificar–escribir: así no se tocan las teclas no seleccionadas.
            var writes = new List<Action<bool>>();
            if (actuation.HasValue)
            {
                var arr = kb.ReadArray16(RyKeyboard.MagParam.Actuation);
                foreach (int i in sel) arr[i] = Raw(actuation.Value, Lim.ActMin, Lim.ActMax);
                writes.Add(c => kb.WriteArray16(RyKeyboard.MagParam.Actuation, arr, KeyCount, c));
            }
            if (rtPress.HasValue)
            {
                var arr = kb.ReadArray16(RyKeyboard.MagParam.RtPress);
                foreach (int i in sel) arr[i] = Raw(rtPress.Value, Lim.RtMin, Lim.RtMax);
                writes.Add(c => kb.WriteArray16(RyKeyboard.MagParam.RtPress, arr, KeyCount, c));
            }
            if (rtRelease.HasValue)
            {
                var arr = kb.ReadArray16(RyKeyboard.MagParam.RtRelease);
                foreach (int i in sel) arr[i] = Raw(rtRelease.Value, Lim.RtMin, Lim.RtMax);
                writes.Add(c => kb.WriteArray16(RyKeyboard.MagParam.RtRelease, arr, KeyCount, c));
            }
            if (deadzone.HasValue)
            {
                var arr = kb.ReadArray16(RyKeyboard.MagParam.DeadZone);
                foreach (int i in sel) arr[i] = Raw(deadzone.Value, Lim.DzMin, Lim.DzMax);
                writes.Add(c => kb.WriteArray16(RyKeyboard.MagParam.DeadZone, arr, KeyCount, c));
            }
            if (rt.HasValue)
            {
                var arr = kb.ReadArray8(RyKeyboard.MagParam.KeyMode);
                foreach (int i in sel)
                {
                    // Solo se cambia el bit de Rapid Trigger en teclas en modo normal (no DKS/MT/etc.).
                    if ((arr[i] & 0x7F) != 0) continue;
                    arr[i] = (byte)(rt.Value ? 0x80 : 0x00);
                }
                writes.Add(c => kb.WriteArray8(RyKeyboard.MagParam.KeyMode, arr, KeyCount, c));
            }
            if (writes.Count == 0) throw new ArgumentException("No hay cambios que aplicar.");
            for (int w = 0; w < writes.Count; w++) writes[w](w == writes.Count - 1);
            // Igual que la app oficial: los valores por tecla pertenecen al modo "personalizado" (3).
            try
            {
                var (mode, kbDisable, padEnable) = kb.GetMagMode();
                if (mode != 3) kb.SetMagMode(3, kbDisable, padEnable);
            }
            catch { }
            kb.FinishWrite();
            return ReadCore(kb, dev);
        }
    }

    public void Dispose() => _timer.Dispose();
}
