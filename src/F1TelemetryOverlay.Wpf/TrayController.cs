using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace F1TelemetryOverlay.Wpf;

internal sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _visibilityItem;
    private readonly ToolStripMenuItem _lockItem;
    private readonly ToolStripMenuItem _steeringItem;
    private readonly ToolStripMenuItem _tyreWearItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;
    private bool _disposed;

    internal TrayController(App app)
    {
        _app = app;
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Color.FromArgb(32, 35, 40),
            ForeColor = Color.White,
        };
        _settingsItem = new ToolStripMenuItem("Settings");
        _visibilityItem = new ToolStripMenuItem();
        _lockItem = new ToolStripMenuItem();
        _steeringItem = new ToolStripMenuItem("Enable steering") { CheckOnClick = true };
        _tyreWearItem = new ToolStripMenuItem("Enable tyre wear overlay") { CheckOnClick = true };
        _exitItem = new ToolStripMenuItem("Exit");

        _settingsItem.Click += (_, _) => _app.OpenSettings();
        _visibilityItem.Click += (_, _) => _app.ToggleOverlayVisibility();
        _lockItem.Click += (_, _) => _app.SetLocked(!_app.IsLocked);
        _steeringItem.Click += (_, _) => _app.SetSteeringEnabled(_steeringItem.Checked);
        _tyreWearItem.Click += (_, _) => _app.SetTyreWearEnabled(_tyreWearItem.Checked);
        _exitItem.Click += (_, _) => _app.Shutdown();

        _menu.Items.AddRange([_settingsItem, _visibilityItem, _lockItem, _steeringItem, _tyreWearItem,
            new ToolStripSeparator(), _exitItem]);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "F1 25 Telemetry Overlay",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _app.ToggleOverlayVisibility();
        Refresh();
    }

    internal void Refresh()
    {
        _visibilityItem.Text = _app.IsOverlayVisible ? "Hide overlays" : "Show overlays";
        _visibilityItem.ShortcutKeyDisplayString = _app.Settings.Shortcuts.ToggleVisibility;
        _lockItem.Text = _app.IsLocked ? "Unlock position" : "Lock position";
        _lockItem.ShortcutKeyDisplayString = _app.Settings.Shortcuts.ToggleLock;
        _steeringItem.Checked = _app.IsSteeringEnabled;
        _steeringItem.ShortcutKeyDisplayString = _app.Settings.Shortcuts.ToggleSteering;
        _tyreWearItem.Text = _app.IsTyreWearEnabled ? "Disable tyre wear overlay" : "Enable tyre wear overlay";
        _tyreWearItem.Checked = _app.IsTyreWearEnabled;
        _exitItem.ShortcutKeyDisplayString = _app.Settings.Shortcuts.Quit;
    }

    internal void ShowAtCursor()
    {
        Refresh();
        Point point = Cursor.Position;
        _menu.Show(point);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _menu.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static Icon LoadIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "app-icon.ico");
        if (File.Exists(path)) return new Icon(path);
        return SystemIcons.Application;
    }
}
