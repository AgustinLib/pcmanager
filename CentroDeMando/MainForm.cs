using System.Runtime.InteropServices;
using CentroDeMando.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CentroDeMando;

public sealed class MainForm : Form
{
    readonly WebView2 _web = new();
    readonly Bridge _bridge;
    readonly string? _screenshotPath;
    readonly string? _screenshotPage;
    readonly NotifyIcon _tray = new();
    readonly bool _startHidden;
    bool _ready, _exiting, _hintShown;

    /// <summary>Mensaje que manda una segunda copia de la app para que esta ventana vuelva a mostrarse.</summary>
    public static readonly uint ShowMsg = RegisterWindowMessage("CentroDeMando.Mostrar");

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr info);

    public MainForm(Bridge bridge, string? screenshotPath = null, string? screenshotPage = null, bool minimized = false)
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

        // Icono en la bandeja: abrir / salir, y avisos de temperatura.
        var tx = AppService.Texts(_bridge.Lang);
        _tray.Icon = Icon ?? SystemIcons.Application;
        _tray.Text = "Centro de Mando";
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add(tx.Open, null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add(tx.Exit, null, (_, _) => { _exiting = true; Close(); });
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.BalloonTipClicked += (_, _) => ShowFromTray();
        _tray.Visible = screenshotPath == null;
        _bridge.Notify += (title, text) =>
        {
            try { BeginInvoke(() => _tray.ShowBalloonTip(8000, title, text, ToolTipIcon.Warning)); } catch { }
        };

        // Arranque con Windows: empieza minimizada (en la bandeja si está activado «cerrar a la bandeja»).
        if (minimized)
        {
            _startHidden = CloseToTray;
            WindowState = FormWindowState.Minimized;
            if (_startHidden) ShowInTaskbar = false;
        }
        Shown += (_, _) => { if (_startHidden) Hide(); };

        _bridge.Post = json =>
        {
            if (!_ready || IsDisposed) return;
            try { BeginInvoke(() => { try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch { } }); } catch { }
        };
        _bridge.RequestClose = () => { try { BeginInvoke(() => { _exiting = true; Close(); }); } catch { } };

        Load += async (_, _) => await InitAsync();
        FormClosing += OnClosing;
        FormClosed += (_, _) => { _tray.Visible = false; _tray.Dispose(); _bridge.Dispose(); };
    }

    bool CloseToTray { get { try { return _bridge.Settings.Get("closeToTray")?.GetValue<bool>() == true; } catch { return false; } } }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing || !CloseToTray || _screenshotPath != null) return;
        e.Cancel = true;
        Hide();
        if (!_hintShown)
        {
            _hintShown = true;
            _tray.ShowBalloonTip(4000, "Centro de Mando", AppService.Texts(_bridge.Lang).Hidden, ToolTipIcon.Info);
        }
    }

    void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void WndProc(ref Message m)
    {
        if (ShowMsg != 0 && m.Msg == (int)ShowMsg) { ShowFromTray(); return; }
        base.WndProc(ref m);
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
        // Una copia sin permisos de administrador también puede pedir que esta ventana se muestre.
        try { if (ShowMsg != 0) ChangeWindowMessageFilterEx(Handle, ShowMsg, 1, IntPtr.Zero); } catch { }
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
                try
                {
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
                }
                catch { }
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
            _exiting = true;
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
        _exiting = true;
        Close();
    }
}
