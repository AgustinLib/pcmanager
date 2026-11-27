using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CentroDeMando.Services;

namespace CentroDeMando;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

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
        bool minimized = args.Contains("--minimized");

        // Captura de pantalla de diagnóstico: no cuenta como instancia (puede convivir con la app abierta).
        if (shot != null)
        {
            ApplicationConfiguration.Initialize();
            using var b = new Bridge();
            Application.Run(new MainForm(b, shot, page));
            return;
        }

        // Una sola instancia. Al relanzarse como administrador espera a que la anterior termine.
        // Si ya está abierta, se le pide que muestre su ventana (también si estaba escondida en la bandeja).
        Mutex mutex;
        bool owned;
        try
        {
            mutex = new Mutex(false, "CentroDeMando.Instancia");
            try { owned = mutex.WaitOne(args.Contains("--wait-previous") ? 8000 : 0); }
            catch (AbandonedMutexException) { owned = true; }
        }
        catch (UnauthorizedAccessException) { mutex = null!; owned = false; }
        if (!owned)
        {
            if (shot == null && !minimized) try { PostMessage((IntPtr)0xFFFF, MainForm.ShowMsg, IntPtr.Zero, IntPtr.Zero); } catch { }
            mutex?.Dispose();
            return;
        }

        // Temperatura de CPU, ventiladores y overclock necesitan permisos de administrador:
        // se pide al abrir. Si se rechaza el aviso de Windows, la app sigue en modo limitado.
        if (shot == null && !args.Contains("--no-elevate") && !args.Contains("--wait-previous") && !SystemInfo.IsAdmin())
        {
            try
            {
                mutex.ReleaseMutex();
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--wait-previous" + (minimized ? " --minimized" : "")) { UseShellExecute = true, Verb = "runas" });
                mutex.Dispose();
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
        try { Application.Run(new MainForm(bridge, shot, page, minimized)); }
        finally
        {
            bridge.Dispose();
            try { mutex.ReleaseMutex(); } catch { }
            mutex.Dispose();
        }
    }
}
