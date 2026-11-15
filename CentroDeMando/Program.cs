using System.Diagnostics;
using System.Text;
using CentroDeMando.Services;

namespace CentroDeMando;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // "--then-gui": tras un diagnóstico por línea de comandos, abre la ventana en el mismo proceso (ya elevado).
        if (Cli.TryRun(args) && !args.Contains("--then-gui")) return;

        string? shot = null, page = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--screenshot") shot = args[i + 1];
            if (args[i] == "--page") page = args[i + 1];
        }

        // Una sola instancia. Al relanzarse como administrador espera a que la anterior termine.
        using var mutex = new Mutex(false, "CentroDeMando.Instancia");
        bool owned;
        try { owned = mutex.WaitOne(args.Contains("--wait-previous") ? 8000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            MessageBox.Show("Centro de Mando ya está abierto.", "Centro de Mando", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Temperatura de CPU, ventiladores y overclock necesitan permisos de administrador:
        // se pide al abrir. Si se rechaza el aviso de Windows, la app sigue en modo limitado.
        if (shot == null && !args.Contains("--no-elevate") && !args.Contains("--wait-previous") && !SystemInfo.IsAdmin())
        {
            try
            {
                mutex.ReleaseMutex();
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-previous") { UseShellExecute = true, Verb = "runas" });
                return;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) return;
            }
        }

        ApplicationConfiguration.Initialize();
        var bridge = new Bridge();
        try { Application.Run(new MainForm(bridge, shot, page)); }
        finally
        {
            bridge.Dispose();
            try { mutex.ReleaseMutex(); } catch { }
        }
    }
}
