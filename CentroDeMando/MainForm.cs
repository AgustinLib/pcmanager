using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CentroDeMando;

public sealed class MainForm : Form
{
    readonly WebView2 _web = new();
    readonly Bridge _bridge;
    readonly string? _screenshotPath;
    readonly string? _screenshotPage;
    bool _ready;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm(Bridge bridge, string? screenshotPath = null, string? screenshotPage = null)
    {
        _bridge = bridge;
        _screenshotPath = screenshotPath;
        _screenshotPage = screenshotPage;

        Text = "Centro de Mando";
        BackColor = Color.FromArgb(11, 13, 18);
        MinimumSize = new Size(1100, 680);
        ClientSize = new Size(1320, 840);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = BackColor;
        Controls.Add(_web);

        _bridge.Post = json =>
        {
            if (!_ready || IsDisposed) return;
            try { BeginInvoke(() => { try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch { } }); } catch { }
        };
        _bridge.RequestClose = () => { try { BeginInvoke(Close); } catch { } };

        Load += async (_, _) => await InitAsync();
        FormClosed += (_, _) => _bridge.Dispose();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));            // barra de título oscura
            int caption = 0x00120D0B;                                           // 0x00BBGGRR = #0b0d12
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        }
        catch { }
    }

    async Task InitAsync()
    {
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CentroDeMando", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            core.SetVirtualHostNameToFolderMapping("app.local", root, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.WebMessageReceived += (_, e) =>
            {
                try { _bridge.Handle(e.WebMessageAsJson); } catch { }
            };
            // Los enlaces externos se abren en el navegador del sistema, no dentro de la app.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
            };
            core.NavigationCompleted += async (_, _) =>
            {
                _ready = true;
                if (_screenshotPath != null) await ScreenshotAndExit();
            };
            _bridge.Start();
            core.Navigate("https://app.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo iniciar la interfaz (WebView2):\n\n" + ex.Message, "Centro de Mando", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    async Task ScreenshotAndExit()
    {
        try
        {
            await Task.Delay(2500);
            if (!string.IsNullOrEmpty(_screenshotPage))
            {
                await _web.CoreWebView2.ExecuteScriptAsync($"document.querySelector('[data-page=\"{_screenshotPage}\"]')?.click();");
                await Task.Delay(4500);
            }
            else await Task.Delay(2500);
            using var fs = File.Create(_screenshotPath!);
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(_screenshotPath + ".error.txt", ex.ToString()); } catch { }
        }
        Close();
    }
}
