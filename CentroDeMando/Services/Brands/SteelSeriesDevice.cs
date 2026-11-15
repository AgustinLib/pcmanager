using HidSharp;

namespace CentroDeMando.Services.Brands;

/// <summary>
/// Ratones y teclados SteelSeries (VID 1038) con protocolos sencillos de OpenRGB: Rival 3, Rival 100/110,
/// Rival 300, Rival 600, Aerox 3 y Apex 3 TKL. No se envía la orden de guardar en memoria.
/// </summary>
public sealed class SteelSeriesDevice : BrandDevice
{
    enum Proto { Rival3, Rival100, Rival300, Rival600, Aerox, ApexTkl }
    static readonly Dictionary<int, (Proto P, string Name, string Kind)> Models = new()
    {
        [0x1824] = (Proto.Rival3, "SteelSeries Rival 3", "mouse"), [0x184C] = (Proto.Rival3, "SteelSeries Rival 3", "mouse"),
        [0x1830] = (Proto.Rival3, "SteelSeries Rival 3 Wireless", "mouse"),
        [0x1702] = (Proto.Rival100, "SteelSeries Rival 100", "mouse"), [0x170C] = (Proto.Rival100, "SteelSeries Rival 100", "mouse"),
        [0x1729] = (Proto.Rival100, "SteelSeries Rival 110", "mouse"),
        [0x1710] = (Proto.Rival300, "SteelSeries Rival 300", "mouse"), [0x1394] = (Proto.Rival300, "SteelSeries Rival 300", "mouse"),
        [0x1716] = (Proto.Rival300, "SteelSeries Rival 300", "mouse"), [0x171A] = (Proto.Rival300, "SteelSeries Rival 300", "mouse"),
        [0x1392] = (Proto.Rival300, "SteelSeries Rival 300", "mouse"), [0x1718] = (Proto.Rival300, "SteelSeries Rival 300", "mouse"),
        [0x1724] = (Proto.Rival600, "SteelSeries Rival 600", "mouse"), [0x172E] = (Proto.Rival600, "SteelSeries Rival 600", "mouse"),
        [0x1836] = (Proto.Aerox, "SteelSeries Aerox 3", "mouse"), [0x1838] = (Proto.Aerox, "SteelSeries Aerox 3 Wireless", "mouse"),
        [0x183A] = (Proto.Aerox, "SteelSeries Aerox 3 Wireless", "mouse"),
        [0x1622] = (Proto.ApexTkl, "SteelSeries Apex 3 TKL", "keyboard"),
    };
    public static bool IsKnown(int pid) => Models.ContainsKey(pid);

    readonly object _lock = new();
    readonly string _fallback, _kind;
    public SteelSeriesDevice(int pid, string name, string kind) { Vid = 0x1038; Pid = pid; _fallback = name; _kind = kind; }

    HidStream Open()
    {
        if (!Models.TryGetValue(Pid, out var m)) throw new InvalidOperationException("Este modelo de SteelSeries todavía no está en la lista de compatibles.");
        HidDevice? d = m.P switch
        {
            Proto.Rival3 => Pid == 0x1830 ? HidPick.Find(Vid, Pid, -1, 0xFFC0) : HidPick.Find(Vid, Pid, 3),
            Proto.Aerox => HidPick.Find(Vid, Pid, 3, 0xFFC0),
            Proto.ApexTkl => HidPick.Find(Vid, Pid, 1, 0xFFC0),
            _ => HidPick.Find(Vid, Pid, 0, -1, x => { try { return x.GetMaxOutputReportLength() > 0; } catch { return false; } }),
        };
        if (d == null || !d.TryOpen(out var s)) throw new InvalidOperationException("No se puede abrir el periférico SteelSeries: cierra SteelSeries GG si está abierto.");
        s.WriteTimeout = 500;
        return s;
    }

    static void Out(HidStream s, params byte[] p)
    {
        int len = Math.Max(p.Length + 1, 2);
        try { len = Math.Max(len, s.Device.GetMaxOutputReportLength()); } catch { }
        var b = new byte[len]; Array.Copy(p, 0, b, 1, p.Length);   // b[0] = ID de informe 0
        s.Write(b); Thread.Sleep(10);
    }

    public override BrandInfo Info()
    {
        var m = Models.TryGetValue(Pid, out var x) ? x : (Proto.Rival3, _fallback, _kind);
        lock (_lock) { using var s = Open(); }
        bool breath = m.Item1 is Proto.Rival3 or Proto.Rival100 or Proto.Rival300;
        bool spectrum = m.Item1 == Proto.Rival3;
        var fx = new List<string> { "static" }; if (breath) fx.Add("breathing"); if (spectrum) fx.Add("spectrum"); fx.Add("off");
        return new BrandInfo("SteelSeries", m.Item2, m.Item3, true, fx.ToArray(), true, false, false, null, 0, 0, 0, null, null);
    }

    public override void SetLighting(LightRequest q)
    {
        var p = Models[Pid].P;
        var (r, g, b) = Rgb(q.Color);
        int bri = q.Effect == "off" ? 0 : Math.Clamp(q.Brightness, 0, 100);
        var (sr, sg, sb) = Scale((r, g, b), bri);
        lock (_lock)
        {
            using var s = Open();
            switch (p)
            {
                case Proto.Rival3:
                    for (byte z = 1; z <= 4; z++) Out(s, 0x05, 0x00, z, r, g, b, (byte)bri);
                    Out(s, 0x06, 0x00, q.Effect switch { "breathing" => (byte)0x02, "spectrum" => (byte)0x00, _ => (byte)0x04 });
                    break;
                case Proto.Rival100:
                    Out(s, 0x05, 0x00, sr, sg, sb);
                    Out(s, 0x07, 0x00, (byte)(q.Effect == "breathing" ? 3 : 1));
                    break;
                case Proto.Rival300:
                    for (byte z = 0; z < 2; z++) { Out(s, 0x08, (byte)(z + 1), sr, sg, sb); Out(s, 0x07, (byte)(z + 1), (byte)(q.Effect == "breathing" ? 3 : 1)); }
                    break;
                case Proto.Rival600:
                    for (int z = 0; z < 8; z++) Out(s, 0x1C, 0x27, 0x00, (byte)(1 << z), sr, sg, sb);
                    break;
                case Proto.Aerox:
                    try { s.SetFeature(new byte[] { 0x00, 0x2D }); } catch { }
                    Out(s, 0x21, 0x07, r, g, b, r, g, b, r, g, b);
                    Out(s, 0x23, (byte)bri);
                    break;
                case Proto.ApexTkl:
                    var data = new List<byte> { 0x21, 0xFF };
                    for (int z = 0; z < 8; z++) data.AddRange(new[] { r, g, b });
                    Out(s, data.ToArray());
                    Out(s, 0x23, (byte)Math.Round(bri * 0x10 / 100.0));
                    break;
            }
        }
    }
}
