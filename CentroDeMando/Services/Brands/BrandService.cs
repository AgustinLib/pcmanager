using System.Text.Json;

namespace CentroDeMando.Services.Brands;

/// <summary>Crea y guarda el controlador de cada periférico de marca detectado (clave "VID:PID").</summary>
public sealed class BrandService
{
    public const int Logitech = 0x046D, Razer = 0x1532, EVision = 0x320F, Sonix = 0x0C45, SteelSeries = 0x1038, Holtek = 0x04D9;
    /// <summary>¿Hay controlador para este periférico? Devuelve también la marca para mostrarla.</summary>
    public static string? BrandOf(int vid, int pid) => vid switch
    {
        Logitech => "Logitech",
        Razer => "Razer",
        SteelSeries => SteelSeriesDevice.IsKnown(pid) ? "SteelSeries" : null,
        EVision or Sonix => EVisionDevice.Known.Contains((vid, pid)) ? "Redragon / EVision" : null,
        Holtek => RedragonMouseDevice.Models.ContainsKey(pid) ? "Redragon" : null,
        _ => null,
    };

    readonly Dictionary<string, BrandDevice> _devs = new();
    readonly object _lock = new();

    public BrandDevice Get(string key, string kind, string name)
    {
        lock (_lock)
        {
            if (_devs.TryGetValue(key, out var d)) return d;
            var parts = key.Split(':');
            if (parts.Length != 2) throw new ArgumentException("Periférico no válido.");
            int vid = Convert.ToInt32(parts[0], 16), pid = Convert.ToInt32(parts[1], 16);
            d = vid switch
            {
                Logitech => new LogitechDevice(pid, kind),
                Razer => new RazerDevice(pid, name, kind == "mouse"),
                EVision or Sonix => new EVisionDevice(pid, name, vid),
                Holtek => new RedragonMouseDevice(pid),
                SteelSeries => new SteelSeriesDevice(pid, name, kind),
                _ => throw new InvalidOperationException("Centro de Mando todavía no sabe configurar este periférico."),
            };
            _devs[key] = d;
            return d;
        }
    }

    static string S(JsonElement a, string n) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int I(JsonElement a, string n, int def) => a.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : def;

    BrandDevice From(JsonElement a) => Get(S(a, "key"), S(a, "kind"), S(a, "name"));

    public BrandInfo Info(JsonElement a) => From(a).Info();

    public bool Apply(JsonElement a)
    {
        var d = From(a);
        if (a.TryGetProperty("effect", out _))
        {
            var eff = S(a, "effect");
            if (eff is not ("static" or "breathing" or "spectrum" or "off")) throw new ArgumentException("Efecto no válido.");
            d.SetLighting(new LightRequest(eff, S(a, "color"), Math.Clamp(I(a, "brightness", 100), 0, 100), Math.Clamp(I(a, "speed", 3), 1, 5)));
        }
        if (a.TryGetProperty("dpi", out var dv) && dv.ValueKind == JsonValueKind.Number) d.SetDpi((int)dv.GetDouble());
        return true;
    }
}
