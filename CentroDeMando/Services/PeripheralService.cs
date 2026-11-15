using HidSharp;
using HidSharp.Reports;

namespace CentroDeMando.Services;

/// <summary>
/// Detecta los periféricos USB/2,4 G conectados (teclados, ratones, mandos y auriculares) a partir de
/// los dispositivos HID de Windows, y marca los que la app sabe configurar.
/// </summary>
public static class PeripheralService
{
    public sealed record Peripheral(string Key, string Kind, string Name, int Vid, int Pid, string? Driver, bool Wireless);

    /// <summary>Fabricantes con soporte de configuración: VID → (tipo, controlador en la app).</summary>
    static readonly Dictionary<int, (string Kind, string Driver)> Supported = new()
    {
        [0x3151] = ("keyboard", "ajazz"),   // RongYuan: teclados AJAZZ y otras marcas de la misma plataforma
        [0x373B] = ("mouse", "vxe"),        // Compx: ratones VXE / ATK / VGN
    };

    static readonly HashSet<int> Ignored = new() { 0x0B05 };   // controladores internos de la placa (ASUS Aura)

    const uint UsageMouse = 0x00010002, UsageJoystick = 0x00010004, UsageGamepad = 0x00010005, UsageKeyboard = 0x00010006, UsageConsumer = 0x000C0001;

    public static List<Peripheral> List()
    {
        var groups = new Dictionary<(int, int), (string Name, HashSet<uint> Usages)>();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            int vid, pid;
            try { vid = d.VendorID; pid = d.ProductID; } catch { continue; }
            if (vid == 0 || Ignored.Contains(vid)) continue;
            if (!groups.TryGetValue((vid, pid), out var g)) { g = ("", new HashSet<uint>()); }
            if (string.IsNullOrWhiteSpace(g.Name)) { try { g.Name = Clean(d.GetProductName()); } catch { } }
            try { foreach (var item in d.GetReportDescriptor().DeviceItems) foreach (var u in item.Usages.GetAllValues()) g.Usages.Add(u); } catch { }
            groups[(vid, pid)] = g;
        }

        var list = new List<Peripheral>();
        foreach (var ((vid, pid), g) in groups)
        {
            string name = g.Name, lower = name.ToLowerInvariant();
            bool wireless = lower.Contains("dongle") || lower.Contains("receiver") || lower.Contains("wireless") || lower.Contains("2.4");
            string? kind = null, driver = null;
            if (Supported.TryGetValue(vid, out var s)) (kind, driver) = s;
            else if (Brands.BrandService.BrandOf(vid, pid) != null)
            {
                driver = "brand";
                kind = vid == Brands.BrandService.Holtek ? "mouse" : vid is Brands.BrandService.EVision or Brands.BrandService.Sonix ? "keyboard"
                     : vid == Brands.BrandService.Logitech && Brands.LogitechDevice.IsReceiver(pid) ? "mouse" : null;
            }
            if (kind != null) { }
            else if (lower.Contains("mouse") || lower.Contains("ratón")) kind = "mouse";
            else if (lower.Contains("keyboard") || lower.Contains("teclado")) kind = "keyboard";
            else if (lower.Contains("headset") || lower.Contains("headphone") || lower.Contains("auricular") || lower.Contains("audio")) kind = "headset";
            else if (g.Usages.Contains(UsageGamepad) || g.Usages.Contains(UsageJoystick)) kind = "gamepad";
            else if (g.Usages.Contains(UsageKeyboard)) kind = "keyboard";
            else if (g.Usages.Contains(UsageMouse)) kind = "mouse";
            else if (g.Usages.Contains(UsageConsumer) && wireless) kind = "headset";
            if (kind == null) continue;                                   // solo controladores internos / de fabricante
            if (string.IsNullOrWhiteSpace(name)) name = kind switch { "keyboard" => "Teclado", "mouse" => "Ratón", "gamepad" => "Mando", _ => "Auriculares" };
            if (vid == 0x3151 && pid == 0x5026) { wireless = true; }
            list.Add(new Peripheral($"{vid:X4}:{pid:X4}", kind, name, vid, pid, driver, wireless));
        }
        return list.OrderBy(p => p.Driver == null).ThenBy(p => p.Kind).ThenBy(p => p.Name).ToList();
    }

    static string Clean(string? s) => (s ?? "").Replace("\0", "").Trim();
}
