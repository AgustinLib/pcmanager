using System.Text;
using System.Text.Json;

namespace CentroDeMando;

/// <summary>
/// Modos de diagnóstico por línea de comandos (no abren la ventana):
///   --call &lt;comando&gt; [json de argumentos] [--wait ms] [--out archivo]
/// Ejecuta un comando del puente (los mismos que usa la interfaz) y vuelca el resultado en JSON.
/// </summary>
internal static class Cli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length < 2 || args[0] != "--call") return false;
        string cmd = args[1];
        string argsJson = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "{}";
        string? outFile = null; int wait = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--out") outFile = args[i + 1];
            if (args[i] == "--wait") int.TryParse(args[i + 1], out wait);
        }
        var sb = new StringBuilder();
        var pretty = new JsonSerializerOptions(Bridge.Json) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Bridge? bridge = null;
        try
        {
            bridge = new Bridge();
            if (cmd == "sensors")
            {
                bridge.Start();
                for (int i = 0; i < 80 && bridge.Hardware.Last == null; i++) Thread.Sleep(100);
                Thread.Sleep(Math.Max(wait, 2500));
                sb.AppendLine(JsonSerializer.Serialize(new { error = bridge.Hardware.OpenError, snapshot = bridge.Hardware.Last }, pretty));
            }
            else if (cmd == "kbtest")
            {
                // Espera a que el teclado esté despierto y prueba lectura + escritura real, restaurando los valores.
                void Log(string s) { sb.AppendLine(s); if (outFile != null) File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); }
                var kb = bridge.Keyboard;
                var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(wait, 60000));
                T Retry<T>(Func<T> f)
                {
                    while (true)
                    {
                        try { return f(); }
                        catch (Exception ex) when (DateTime.UtcNow < deadline && (ex.Message.Contains("dormido") || ex.Message.Contains("no responde") || ex.Message.Contains("inesperada") || ex.Message.Contains("inconsistentes")))
                        { Thread.Sleep(400); }
                    }
                }
                string Summ(Services.KeyboardService.ReadResult r)
                {
                    var rc = r.Keys.First(k => k.I == 71);
                    return $"teclas={r.Keys.Length} act=[{string.Join(",", r.Keys.Select(k => k.Actuation).Distinct())}] rt=[{string.Join(",", r.Keys.Select(k => k.Rt).Distinct())}] " +
                           $"dz=[{string.Join(",", r.Keys.Select(k => k.Deadzone).Distinct())}] {rc.Label}(71)={rc.Actuation} led={JsonSerializer.Serialize(r.Led, Bridge.Json)}";
                }
                var r0 = Retry(() => kb.Read());
                Log("1) lectura inicial: " + Summ(r0));
                double orig = r0.Keys.First(k => k.I == 71).Actuation;
                if (r0.Keys.Select(k => k.Actuation).Distinct().Count() == 1)
                {
                    string all = string.Join(",", r0.Keys.Select(k => k.I));
                    using var d1 = JsonDocument.Parse($"{{\"keys\":[{all}],\"actuation\":{orig.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
                    Log("2) reescribir el mismo valor en todas: " + Summ(Retry(() => kb.Apply(d1.RootElement))));
                }
                using var d2 = JsonDocument.Parse("{\"keys\":[71],\"actuation\":" + (orig - 0.1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                Log("3) tecla 71 -0,10 mm: " + Summ(Retry(() => kb.Apply(d2.RootElement))));
                using var d3 = JsonDocument.Parse("{\"keys\":[71],\"actuation\":" + orig.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                Log("4) tecla 71 restaurada: " + Summ(Retry(() => kb.Apply(d3.RootElement))));
                using var l1 = JsonDocument.Parse("{\"mode\":1,\"speed\":2,\"brightness\":4,\"option\":0,\"dazzle\":false,\"color\":\"#ff0000\"}");
                Retry(() => { kb.SetLed(l1.RootElement); return 0; });
                Thread.Sleep(1500);
                Log("5) LED rojo fijo -> lectura: " + JsonSerializer.Serialize(Retry(() => kb.Read()).Led, Bridge.Json));
                using var l0 = JsonDocument.Parse(JsonSerializer.Serialize(r0.Led, Bridge.Json));
                Retry(() => { kb.SetLed(l0.RootElement); return 0; });
                Log("6) LED restaurado -> lectura final: " + Summ(Retry(() => kb.Read())));
            }
            else if (cmd == "selftest")
            {
                // Sensores + prueba real de cada ventilador controlable: subir a un valor fijo, medir y devolver a automático.
                var savedFans = bridge.Settings.GetFans();      // la prueba no debe alterar la configuración guardada
                bridge.Start();
                for (int i = 0; i < 80 && bridge.Hardware.Last == null; i++) Thread.Sleep(100);
                Thread.Sleep(3000);
                var before = bridge.Hardware.Last;
                sb.AppendLine(JsonSerializer.Serialize(new { isAdmin = Services.SystemInfo.IsAdmin(), error = bridge.Hardware.OpenError, snapshot = before }, pretty));
                var results = new List<object>();
                foreach (var fan in before?.Fans.Where(f => f.Controllable) ?? Enumerable.Empty<Services.FanDto>())
                {
                    double test = fan.Pct is double p && p > 60 ? 35 : 75;
                    bridge.Hardware.SetFan(new Services.FanConfig { Id = fan.Id, Mode = "manual", Pct = test });
                    Thread.Sleep(7000);
                    var mid = bridge.Hardware.Last?.Fans.FirstOrDefault(f => f.Id == fan.Id);
                    bridge.Hardware.SetFan(new Services.FanConfig { Id = fan.Id, Mode = "auto" });
                    Thread.Sleep(5000);
                    var after = bridge.Hardware.Last?.Fans.FirstOrDefault(f => f.Id == fan.Id);
                    results.Add(new { fan.Id, fan.Name, pedido = test, antes = new { fan.Rpm, fan.Pct }, durante = new { mid?.Rpm, mid?.Pct, mid?.Mode }, despues = new { after?.Rpm, after?.Pct, after?.Mode } });
                }
                sb.AppendLine("FAN TEST:");
                sb.AppendLine(JsonSerializer.Serialize(results, pretty));
                bridge.Settings.SetFans(savedFans);
            }
            else
            {
                if (cmd.StartsWith("fans.")) { bridge.Start(); for (int i = 0; i < 80 && bridge.Hardware.Last == null; i++) Thread.Sleep(100); }
                var result = bridge.Invoke(cmd, argsJson);
                sb.AppendLine(JsonSerializer.Serialize(result, pretty));
                if (wait > 0) Thread.Sleep(wait);
                if (cmd.StartsWith("fans.")) sb.AppendLine(JsonSerializer.Serialize(bridge.Hardware.Last?.Fans, pretty));
            }
        }
        catch (Exception ex)
        {
            var e = ex is AggregateException a && a.InnerException != null ? a.InnerException : ex;
            sb.AppendLine("ERROR: " + e.GetType().Name + ": " + e.Message);
        }
        finally { try { bridge?.Dispose(); } catch { } }
        if (outFile != null) File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
        else Console.Write(sb.ToString());
        return true;
    }
}
