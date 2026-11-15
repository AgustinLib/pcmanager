using HidSharp;

namespace CentroDeMando.Services.Brands;

/// <summary>Petición de iluminación común a todas las marcas.</summary>
public sealed record LightRequest(string Effect, string Color, int Brightness, int Speed);

/// <summary>Lo que un periférico de marca sabe hacer y su estado actual.</summary>
public sealed record BrandInfo(
    string Brand, string Model, string Kind, bool Online,
    string[] Effects, bool HasBrightness, bool HasSpeed, bool HasDpi,
    int? Dpi, int DpiMin, int DpiMax, int DpiStep, int? Battery, string? Note);

/// <summary>
/// Controlador de un periférico de una marca concreta (Logitech, Razer, Redragon/EVision, SteelSeries).
/// Cada controlador valida las respuestas del dispositivo y lanza una excepción con un texto claro
/// si algo no cuadra; nunca escribe en la memoria permanente salvo lo que haga el propio efecto.
/// </summary>
public abstract class BrandDevice
{
    public string Key { get; init; } = "";
    public int Vid { get; init; }
    public int Pid { get; init; }

    public abstract BrandInfo Info();
    public abstract void SetLighting(LightRequest req);
    public virtual void SetDpi(int dpi) => throw new InvalidOperationException("Este periférico no permite cambiar los DPI.");

    protected static (byte R, byte G, byte B) Rgb(string hex)
    {
        var h = (hex ?? "").TrimStart('#');
        if (h.Length != 6) return (255, 255, 255);
        int v = Convert.ToInt32(h, 16);
        return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Aplica el brillo (0–100) directamente sobre el color, para los que no tienen brillo propio.</summary>
    protected static (byte R, byte G, byte B) Scale((byte R, byte G, byte B) c, int brightness)
    {
        double k = Math.Clamp(brightness, 0, 100) / 100.0;
        return ((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    protected static IEnumerable<HidDevice> Hid(int vid, int pid)
    {
        try { return DeviceList.Local.GetHidDevices(vid, pid).ToList(); } catch { return Array.Empty<HidDevice>(); }
    }
}
