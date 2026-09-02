using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using F1TelemetryOverlay.Core;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfPoint = System.Windows.Point;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using Button = System.Windows.Controls.Button;

namespace F1TelemetryOverlay.Wpf;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, (bool Ok, string Error)> _save;
    private readonly Func<bool>? _beginArrange;
    private readonly Action? _endArrange;
    private readonly Func<AppSettings>? _currentSettings;
    private readonly Func<OverlayStatus>? _statusProvider;
    private AppSettings _initial;
    // Widget state is a transactional form baseline. Normal saves preserve
    // these values (for example, positions moved while the settings window is
    // open), while Restore defaults deliberately replaces both baselines.
    private OverlayWidgetSettings _pendingPedalsOverlay;
    private OverlayWidgetSettings _pendingTyreWearOverlay;
    private LockupColorMode _pendingLockupColorMode;
    private LockupColorSettings _pendingLockupColors;
    private readonly CheckBox _steeringDefault = new();
    private readonly CheckBox _pedalsEnabled = new();
    private readonly CheckBox _pedalsLocked = new();
    private readonly CheckBox _tyreWearEnabled = new();
    private readonly CheckBox _tyreWearLocked = new();
    private readonly ComboBox _steeringPosition = new();
    private readonly Slider _transparency = new();
    private readonly Slider _pedalsScale = new();
    private readonly Slider _tyreWearOpacity = new();
    private readonly Slider _tyreWearScale = new();
    private readonly TextBox _udpPort = new();
    private readonly Slider _sensitivity = new();
    private readonly Slider _duration = new();
    private readonly TextBox _singleColor = new();
    private readonly TextBox _toggleVisibility = new();
    private readonly TextBox _toggleLock = new();
    private readonly TextBox _toggleDemo = new();
    private readonly TextBox _toggleSteering = new();
    private readonly TextBox _quit = new();
    private DispatcherTimer? _snackbarTimer;
    private DispatcherTimer? _statusTimer;
    private Button? _arrangeButton;
    private Button? _doneArrangeButton;
    private bool _arranging;
    private bool _pedalsPositionReset;
    private bool _tyreWearPositionReset;
    private readonly Dictionary<string, StackPanel> _pages = new(StringComparer.Ordinal);
    private TextBlock? _dashboardStatusText;
    private TextBlock? _dashboardOverlaySummary;
    private TextBlock? _connectionStatusText;

    private sealed class ColorPickerState
    {
        internal required TextBox Box { get; init; }
        internal required Grid Surface { get; init; }
        internal required WpfRectangle HueLayer { get; init; }
        internal required Ellipse Cursor { get; init; }
        internal required Slider HueSlider { get; init; }
        internal double Hue { get; set; }
        internal double Saturation { get; set; }
        internal double Value { get; set; }
        internal bool Updating { get; set; }
    }

    internal SettingsWindow(AppSettings settings, Func<AppSettings, (bool Ok, string Error)> save,
        Func<bool>? beginArrange = null, Action? endArrange = null,
        Func<AppSettings>? currentSettings = null, Func<OverlayStatus>? statusProvider = null)
    {
        _initial = settings;
        _pendingPedalsOverlay = settings.PedalsOverlay;
        _pendingTyreWearOverlay = settings.TyreWearOverlay;
        _pendingLockupColorMode = settings.LockupColorMode;
        _pendingLockupColors = settings.LockupColors;
        _save = save;
        _beginArrange = beginArrange;
        _endArrange = endArrange;
        _currentSettings = currentSettings;
        _statusProvider = statusProvider;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyNativeChromeTheme();
        BuildOptions();
        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += SaveClicked;
        RestoreButton.Click += RestoreDefaultsClicked;
    }

    protected override void OnClosed(EventArgs e)
    {
        _snackbarTimer?.Stop();
        _statusTimer?.Stop();
        if (_arranging)
        {
            _arranging = false;
            _endArrange?.Invoke();
        }
        base.OnClosed(e);
    }

    private void ApplyNativeChromeTheme()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // DWM COLORREF values are stored as 0x00BBGGRR rather than WPF's
        // #RRGGBB representation. Keep the native caption in lockstep with
        // the settings surface and use white text/icons for contrast.
        SetDwmColor(handle, NativeMethods.DwmwaCaptionColor, 0x001C1611u);
        SetDwmColor(handle, NativeMethods.DwmwaBorderColor, 0x002B3744u);
        SetDwmColor(handle, NativeMethods.DwmwaTextColor, 0x00FAF7F5u);

        uint darkMode = 1;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref darkMode,
            sizeof(uint));
    }

    private static void SetDwmColor(IntPtr handle, int attribute, uint colorRef)
    {
        _ = NativeMethods.DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(uint));
    }

    private void BuildOptions()
    {
        WireNavigation();
        _steeringPosition.Items.Add("Left side of graph");
        _steeringPosition.Items.Add("Right side of graph");
        _steeringPosition.PreviewMouseWheel += SteeringPositionPreviewMouseWheel;
        ConfigureSlider(_transparency, 0.2, 1, 0.01, 0.1, _initial.PedalsOverlay.Opacity);
        ConfigureSlider(_pedalsScale, 0.5, 2, 0.01, 0.1, _initial.PedalsOverlay.Scale);
        ConfigureSlider(_tyreWearOpacity, 0.2, 1, 0.01, 0.1, _initial.TyreWearOverlay.Opacity);
        ConfigureSlider(_tyreWearScale, 0.5, 2, 0.01, 0.1, _initial.TyreWearOverlay.Scale);
        ConfigureSlider(_sensitivity, 0.15, 0.9, 0.01, 0.1, _initial.LockupSensitivity);
        ConfigureSlider(_duration, 2, 15, 0.5, 1, _initial.GraphDurationSeconds);

        BuildDashboardPage();
        BuildOverlaysPage();
        BuildConnectionPage();
        BuildAppearancePage();
        BuildShortcutsPage();
        PopulateFromSettings(_initial);
        SelectPage("Overlays");
        RefreshStatus();
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
    }

    private void RestoreDefaultsClicked(object sender, RoutedEventArgs e)
    {
        PopulateFromSettings(AppSettings.Default);
        _pedalsPositionReset = true;
        _tyreWearPositionReset = true;
        ShowSnackbar("Defaults restored for this form.");
    }

    private void PopulateFromSettings(AppSettings settings)
    {
        _pendingPedalsOverlay = settings.PedalsOverlay;
        _pendingTyreWearOverlay = settings.TyreWearOverlay;
        _pendingLockupColorMode = settings.LockupColorMode;
        _pendingLockupColors = settings.LockupColors;
        _pedalsPositionReset = false;
        _tyreWearPositionReset = false;
        _steeringDefault.IsChecked = settings.SteeringEnabledByDefault;
        _pedalsEnabled.IsChecked = settings.PedalsOverlay.Enabled;
        _pedalsLocked.IsChecked = settings.PedalsOverlay.Locked;
        _tyreWearEnabled.IsChecked = settings.TyreWearOverlay.Enabled;
        _tyreWearLocked.IsChecked = settings.TyreWearOverlay.Locked;
        _steeringPosition.SelectedIndex = settings.SteeringPosition == SteeringPosition.Right ? 1 : 0;
        _transparency.Value = Math.Clamp(settings.PedalsOverlay.Opacity, _transparency.Minimum, _transparency.Maximum);
        _pedalsScale.Value = Math.Clamp(settings.PedalsOverlay.Scale, _pedalsScale.Minimum, _pedalsScale.Maximum);
        _tyreWearOpacity.Value = Math.Clamp(settings.TyreWearOverlay.Opacity, _tyreWearOpacity.Minimum, _tyreWearOpacity.Maximum);
        _tyreWearScale.Value = Math.Clamp(settings.TyreWearOverlay.Scale, _tyreWearScale.Minimum, _tyreWearScale.Maximum);
        _udpPort.Text = settings.UdpPort.ToString(CultureInfo.InvariantCulture);
        _sensitivity.Value = Math.Clamp(settings.LockupSensitivity, _sensitivity.Minimum, _sensitivity.Maximum);
        _duration.Value = Math.Clamp(settings.GraphDurationSeconds, _duration.Minimum, _duration.Maximum);

        _singleColor.Text = settings.LockupColors.Single;

        _toggleVisibility.Text = settings.Shortcuts.ToggleVisibility;
        _toggleLock.Text = settings.Shortcuts.ToggleLock;
        _toggleDemo.Text = settings.Shortcuts.ToggleDemo;
        _toggleSteering.Text = settings.Shortcuts.ToggleSteering;
        _quit.Text = settings.Shortcuts.Quit;
        RefreshOverlayStatus();
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        SavePending(showSuccess: true);
    }

    private bool SavePending(bool showSuccess)
    {
        // A drag is persisted by App immediately. Merge those latest positions
        // into this form before building another candidate, unless the user
        // deliberately pressed Reset position in this form.
        if (_currentSettings is not null)
        {
            AppSettings current = _currentSettings();
            if (!_pedalsPositionReset)
            {
                _pendingPedalsOverlay = _pendingPedalsOverlay with
                {
                    Left = current.PedalsOverlay.Left,
                    Top = current.PedalsOverlay.Top,
                };
            }
            if (!_tyreWearPositionReset)
            {
                _pendingTyreWearOverlay = _pendingTyreWearOverlay with
                {
                    Left = current.TyreWearOverlay.Left,
                    Top = current.TyreWearOverlay.Top,
                };
            }
        }
        if (!TryReadDouble(_transparency.Value, 0.2, 1, "Overlay transparency", out double transparency)
            || !TryReadInt(_udpPort, 1, 65535, "UDP port", out int port)
            || !TryReadDouble(_sensitivity.Value, 0.15, 0.9, "Lock-up sensitivity", out double sensitivity)
            || !TryReadDouble(_duration.Value, 2, 15, "Graph duration", out double duration)
            || !ValidateColors()) return false;

        AppSettings candidate = new(
            _steeringDefault.IsChecked == true,
            transparency,
            port,
            sensitivity,
            duration,
            new ShortcutSettings(_toggleVisibility.Text.Trim(), _toggleLock.Text.Trim(), _toggleDemo.Text.Trim(),
                _toggleSteering.Text.Trim(), _quit.Text.Trim()),
            _pendingLockupColorMode,
            _pendingLockupColors with { Single = _singleColor.Text.Trim() })
        {
            SteeringPosition = _steeringPosition.SelectedIndex == 1 ? SteeringPosition.Right : SteeringPosition.Left,
            PedalsOverlay = _pendingPedalsOverlay with
            {
                Enabled = _pedalsEnabled.IsChecked == true,
                Locked = _pedalsLocked.IsChecked == true,
                Opacity = transparency,
                Scale = Math.Clamp(_pedalsScale.Value, 0.5, 2),
            },
            TyreWearOverlay = _pendingTyreWearOverlay with
            {
                Enabled = _tyreWearEnabled.IsChecked == true,
                Locked = _tyreWearLocked.IsChecked == true,
                Opacity = Math.Clamp(_tyreWearOpacity.Value, 0.2, 1),
                Scale = Math.Clamp(_tyreWearScale.Value, 0.5, 2),
            },
        };

        (bool ok, string error) = _save(candidate);
        if (!ok)
        {
            WpfMessageBox.Show(this, error, "Settings not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _initial = _currentSettings?.Invoke() ?? candidate;
        _pendingPedalsOverlay = _initial.PedalsOverlay;
        _pendingTyreWearOverlay = _initial.TyreWearOverlay;
        _pendingLockupColorMode = _initial.LockupColorMode;
        _pendingLockupColors = _initial.LockupColors;
        _pedalsPositionReset = false;
        _tyreWearPositionReset = false;
        if (showSuccess) ShowSnackbar("Settings saved successfully.");
        RefreshOverlayStatus();
        return true;
    }

    internal void Navigate(string page) => SelectPage(page);

    internal bool IsArranging => _arranging;

    internal void SetArrangeMode(bool arranging)
    {
        _arranging = arranging;
        if (_arrangeButton is not null) _arrangeButton.Visibility = arranging ? Visibility.Collapsed : Visibility.Visible;
        if (_doneArrangeButton is not null) _doneArrangeButton.Visibility = arranging ? Visibility.Visible : Visibility.Collapsed;
        RefreshOverlayStatus();
    }

    private void ArrangeOverlaysClicked(object sender, RoutedEventArgs e)
    {
        // Arrangement is transactional: validate and persist the pending form
        // first. A failed save must never expose temporary drag state.
        if (!SavePending(showSuccess: false)) return;
        if (_beginArrange is not null && !_beginArrange())
        {
            ShowSnackbar("Arrange mode could not be started.");
            return;
        }

        SetArrangeMode(true);
        ShowSnackbar("Drag enabled overlays, then choose Done arranging.");
    }

    private void DoneArrangingClicked(object sender, RoutedEventArgs e)
    {
        _endArrange?.Invoke();
        SetArrangeMode(false);
        ShowSnackbar("Arrangement saved.");
    }

    private void WireNavigation()
    {
        Button[] buttons =
        [
            DashboardNavigationButton,
            OverlaysNavigationButton,
            ConnectionNavigationButton,
            AppearanceNavigationButton,
            ShortcutsNavigationButton,
        ];
        foreach (Button button in buttons)
        {
            button.Click += (_, _) => SelectPage((string)button.Tag);
        }
        _pages["Dashboard"] = DashboardPage;
        _pages["Overlays"] = OverlaysPage;
        _pages["Connection"] = ConnectionPage;
        _pages["Appearance"] = AppearancePage;
        _pages["Shortcuts"] = ShortcutsPage;
    }

    private void SelectPage(string page)
    {
        if (!_pages.TryGetValue(page, out StackPanel? selected)) return;
        foreach ((string name, StackPanel panel) in _pages)
        {
            panel.Visibility = ReferenceEquals(panel, selected) ? Visibility.Visible : Visibility.Collapsed;
        }

        PageTitleText.Text = page;
        PageSubtitleText.Text = page switch
        {
            "Dashboard" => "A concise view of connection health and enabled widgets.",
            "Overlays" => "Arrange and tune the widgets shown over F1 25.",
            "Connection" => "Choose the UDP port used by the telemetry receiver.",
            "Appearance" => "Tune steering placement, graph timing, and lock-up colour.",
            "Shortcuts" => "Capture global shortcuts for common overlay actions.",
            _ => string.Empty,
        };

        Button[] buttons =
        [
            DashboardNavigationButton,
            OverlaysNavigationButton,
            ConnectionNavigationButton,
            AppearanceNavigationButton,
            ShortcutsNavigationButton,
        ];
        foreach (Button button in buttons)
        {
            bool isSelected = string.Equals((string)button.Tag, page, StringComparison.Ordinal);
            button.Background = isSelected ? Brush("#32191B") : Brush("#0D1116");
            button.BorderBrush = isSelected ? Brush("#E10600") : Brush("#0D1116");
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        SettingsScrollViewer.ScrollToTop();
    }

    private void BuildDashboardPage()
    {
        TextBlock status = new()
        {
            Name = "DashboardConnectionStatus",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F5F7FA"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        TextBlock details = new()
        {
            Text = "Telemetry receiver status",
            Foreground = Brush("#AAB5C2"),
            FontSize = 12,
        };
        _dashboardStatusText = status;
        TextBlock summary = new()
        {
            Name = "DashboardOverlaySummary",
            Foreground = Brush("#D7DEE6"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        _dashboardOverlaySummary = summary;
        DashboardPage.Children.Add(Section("Connection", "Live state from the F1 25 UDP receiver.", status, details));
        DashboardPage.Children.Add(Section("Overlay summary", "Only enabled widgets are shown in-game.", summary));
        DashboardPage.Children.Add(Section("Quick navigation", "Use the sidebar to tune a page, then save when ready.",
            NavigationHint("Overlays", "Arrange, enable, lock, scale, and set opacity."),
            NavigationHint("Connection", "Change the UDP port and confirm receiver state."),
            NavigationHint("Appearance", "Place steering and calibrate lock-up colour."),
            NavigationHint("Shortcuts", "Capture and review global keyboard shortcuts.")));
    }

    private FrameworkElement NavigationHint(string page, string copy)
    {
        Button button = new()
        {
            Content = $"{page}  ›  {copy}",
            Tag = page,
            Style = (Style)FindResource("SidebarButtonStyle"),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1),
        };
        button.Click += (_, _) => SelectPage(page);
        return button;
    }

    private void BuildOverlaysPage()
    {
        TextBlock intro = new()
        {
            Text = "Each widget keeps its own visibility, position, lock, scale, and opacity.",
            Foreground = Brush("#AAB5C2"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };
        OverlaysPage.Children.Add(intro);
        OverlaysPage.Children.Add(CreateOverlayCard(
            "PEDALS & INPUTS",
            "Live throttle, brake, steering and input history.",
            false,
            CreatePedalPreview(),
            _pedalsEnabled,
            _pedalsLocked,
            _transparency,
            _pedalsScale,
            () => _pendingPedalsOverlay,
            value => _pendingPedalsOverlay = value));
        OverlaysPage.Children.Add(CreateOverlayCard(
            "TYRE WEAR",
            "Four-corner tyre degradation at a glance.",
            true,
            CreateTyrePreview(),
            _tyreWearEnabled,
            _tyreWearLocked,
            _tyreWearOpacity,
            _tyreWearScale,
            () => _pendingTyreWearOverlay,
            value => _pendingTyreWearOverlay = value));

        StackPanel arrangePanel = new() { Margin = new Thickness(0, 4, 0, 8) };
        _arrangeButton = new Button
        {
            Content = "ARRANGE OVERLAYS",
            Style = (Style)FindResource("PrimaryButtonStyle"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(18, 10, 18, 10),
            ToolTip = "Save the current form, then drag enabled overlays into place.",
        };
        RegisterDynamicName("ArrangeOverlaysButton", _arrangeButton);
        _arrangeButton.Click += ArrangeOverlaysClicked;
        _doneArrangeButton = new Button
        {
            Content = "Done arranging",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(18, 10, 18, 10),
            Visibility = Visibility.Collapsed,
        };
        RegisterDynamicName("DoneArrangingButton", _doneArrangeButton);
        _doneArrangeButton.Click += DoneArrangingClicked;
        arrangePanel.Children.Add(_arrangeButton);
        arrangePanel.Children.Add(_doneArrangeButton);
        arrangePanel.Children.Add(new TextBlock
        {
            Text = "Tip: enable only the widgets you want to move. Their saved lock choice returns when you finish.",
            Foreground = Brush("#AAB5C2"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        OverlaysPage.Children.Add(arrangePanel);
    }

    private FrameworkElement CreateOverlayCard(string title, string description, bool tyreWear,
        FrameworkElement preview, CheckBox enabled, CheckBox locked, Slider opacity, Slider scale,
        Func<OverlayWidgetSettings> read, Action<OverlayWidgetSettings> write)
    {
        enabled.Content = "Enabled";
        locked.Content = "Lock position";
        RegisterDynamicName(tyreWear ? "TyreWearEnabledToggle" : "PedalsEnabledToggle", enabled);
        RegisterDynamicName(tyreWear ? "TyreWearLockedToggle" : "PedalsLockedToggle", locked);
        RegisterDynamicName(tyreWear ? "TyreWearOpacitySlider" : "PedalsOpacitySlider", opacity);
        RegisterDynamicName(tyreWear ? "TyreWearScaleSlider" : "PedalsScaleSlider", scale);

        TextBlock status = new()
        {
            Foreground = Brush("#AAB5C2"),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
        };
        RegisterDynamicName(tyreWear ? "TyreWearStatusText" : "PedalsStatusText", status);

        enabled.Checked += (_, _) => { write(read() with { Enabled = true }); RefreshOverlayStatus(); };
        enabled.Unchecked += (_, _) => { write(read() with { Enabled = false }); RefreshOverlayStatus(); };
        locked.Checked += (_, _) => { write(read() with { Locked = true }); RefreshOverlayStatus(); };
        locked.Unchecked += (_, _) => { write(read() with { Locked = false }); RefreshOverlayStatus(); };
        opacity.ValueChanged += (_, _) => { if (opacity.IsLoaded) write(read() with { Opacity = opacity.Value }); };
        scale.ValueChanged += (_, _) => { if (scale.IsLoaded) write(read() with { Scale = scale.Value }); };

        Expander configure = new()
        {
            Header = new TextBlock { Text = "Configure", Foreground = Brush("#D7DEE6"), FontWeight = FontWeights.SemiBold },
            Foreground = Brush("#F5F7FA"),
            Margin = new Thickness(0, 12, 0, 0),
            IsExpanded = true,
        };
        StackPanel configureContent = new();
        configureContent.Children.Add(locked);
        configureContent.Children.Add(CompactSliderField("Opacity (0.2 - 1.0)", opacity));
        configureContent.Children.Add(CompactSliderField("Scale (0.5 - 2.0)", scale));
        Button reset = new()
        {
            Content = "Reset position",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 2, 0, 0),
        };
        RegisterDynamicName(tyreWear ? "TyreWearResetPositionButton" : "PedalsResetPositionButton", reset);
        reset.Click += (_, _) =>
        {
            write(read() with { Left = null, Top = null });
            if (tyreWear) _tyreWearPositionReset = true;
            else _pedalsPositionReset = true;
            status.Text = "Position reset. Save changes to apply.";
        };
        configureContent.Children.Add(reset);
        configure.Content = configureContent;

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(174) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Border previewChrome = new()
        {
            Width = 154,
            Height = 116,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brush("#0D141C"),
            BorderBrush = Brush("#2B3744"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8),
            Child = preview,
        };
        Grid.SetRowSpan(previewChrome, 2);
        Grid.SetColumn(previewChrome, 0);
        content.Children.Add(previewChrome);

        StackPanel metadata = new() { Margin = new Thickness(12, 0, 0, 0) };
        metadata.Children.Add(new TextBlock { Text = title, Foreground = Brush("#F5F7FA"), FontSize = 15, FontWeight = FontWeights.SemiBold });
        metadata.Children.Add(new TextBlock { Text = description, Foreground = Brush("#AAB5C2"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
        metadata.Children.Add(enabled);
        metadata.Children.Add(status);
        Grid.SetColumn(metadata, 1);
        Grid.SetRow(metadata, 0);
        content.Children.Add(metadata);
        Grid.SetColumn(configure, 1);
        Grid.SetRow(configure, 1);
        content.Children.Add(configure);
        return new Border
        {
            Background = Brush("#151C24"),
            BorderBrush = Brush("#2B3744"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content,
        };
    }

    private FrameworkElement CreatePedalPreview() => new HubPedalPreview { Width = 138, Height = 100, IsHitTestVisible = false };

    private FrameworkElement CreateTyrePreview()
    {
        TyreWearSurface preview = new() { Width = 100, Height = 100, IsHitTestVisible = false };
        preview.Initialize(_initial.TyreWearOverlay);
        return preview;
    }

    private void BuildConnectionPage()
    {
        _connectionStatusText = new TextBlock { Foreground = Brush("#AAB5C2"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        RegisterDynamicName("ConnectionStatusText", _connectionStatusText);
        ConnectionPage.Children.Add(Section("UDP connection", "The receiver listens for F1 25 telemetry on this local UDP port.",
            Field("UDP port", _udpPort, _initial.UdpPort.ToString(CultureInfo.InvariantCulture)), _connectionStatusText));
        ConnectionPage.Children.Add(Section("Connection guidance", "Start or resume a session in F1 25 after saving a new port. The header updates as packets arrive.",
            new TextBlock { Text = "No telemetry packets or receiver controls are added here; this page only edits the existing connection setting.", Foreground = Brush("#AAB5C2"), TextWrapping = TextWrapping.Wrap }));
    }

    private void BuildAppearancePage()
    {
        _steeringDefault.Content = "Enable steering by default";
        FrameworkElement colour = ColorField(_singleColor, _initial.LockupColors.Single, out _);
        AppearancePage.Children.Add(Section("Steering", "Choose the startup preference and which side of the graph carries the steering dial.",
            _steeringDefault, Field("Steering circle position", _steeringPosition)));
        AppearancePage.Children.Add(Section("Graph", "These values apply to the existing input history graph.",
            Field("Graph duration (2 - 15 seconds)", _duration)));
        AppearancePage.Children.Add(Section("Lock-up", "Tune the detector threshold and the single-colour mode colour. Axle mode keeps its existing front/rear/both colours.",
            Field("Lock-up sensitivity (0.15 - 0.9)", _sensitivity), colour));
        AppearancePage.Children.Add(Section("Colour calibration", "The colour picker changes the persisted value; exact in-game colour depends on the configured overlay transparency and game compositor.",
            new TextBlock { Text = "Use the HSV surface or enter a six-digit value such as #ffd84a.", Foreground = Brush("#AAB5C2"), TextWrapping = TextWrapping.Wrap }));
    }

    private void BuildShortcutsPage()
    {
        ShortcutsPage.Children.Add(Section("Global shortcuts", "Focus a field and press a modifier plus a key. Each shortcut must remain unique and available.",
            ShortcutField("Show / hide overlays", _toggleVisibility, _initial.Shortcuts.ToggleVisibility),
            ShortcutField("Lock / unlock positions", _toggleLock, _initial.Shortcuts.ToggleLock),
            ShortcutField("Toggle demo signal", _toggleDemo, _initial.Shortcuts.ToggleDemo),
            ShortcutField("Enable steering", _toggleSteering, _initial.Shortcuts.ToggleSteering),
            ShortcutField("Exit application", _quit, _initial.Shortcuts.Quit)));
    }

    private void RegisterDynamicName(string name, FrameworkElement control)
    {
        control.Name = name;
        RegisterName(name, control);
    }

    private void RefreshOverlayStatus()
    {
        if (_dashboardOverlaySummary is not null)
        {
            string pedals = _pendingPedalsOverlay.Enabled ? "enabled" : "disabled";
            string tyres = _pendingTyreWearOverlay.Enabled ? "enabled" : "disabled";
            _dashboardOverlaySummary.Text = $"Pedals & inputs: {pedals}\nTyre wear: {tyres}";
        }

        RefreshCardStatus(_pendingPedalsOverlay, "PedalsStatusText");
        RefreshCardStatus(_pendingTyreWearOverlay, "TyreWearStatusText");
    }

    private void RefreshCardStatus(OverlayWidgetSettings settings, string name)
    {
        if (FindName(name) is TextBlock text)
        {
            text.Text = settings.Enabled
                ? $"Enabled · {(settings.Locked ? "Locked" : "Unlocked")}"
                : "Disabled · hidden in-game";
        }
    }

    private void RefreshStatus()
    {
        OverlayStatus status = _statusProvider?.Invoke() ?? new OverlayStatus(ConnectionState.Listening, $"Waiting on UDP {_initial.UdpPort}", _initial.UdpPort);
        string text = status.State switch
        {
            ConnectionState.Connected => "F1 25 · Connected",
            ConnectionState.Listening => $"Waiting on UDP {status.Port}",
            ConnectionState.Error => string.IsNullOrWhiteSpace(status.Message) ? $"Error · UDP {status.Port}" : $"Error · {status.Message}",
            _ => status.Message,
        };
        HeaderStatusText.Text = text;
        HeaderStatusText.Foreground = Brush(status.State switch
        {
            ConnectionState.Connected => "#42E37C",
            ConnectionState.Error => "#FF8A7F",
            _ => "#AAB5C2",
        });
        if (_connectionStatusText is not null) _connectionStatusText.Text = text;
        if (_dashboardStatusText is not null) _dashboardStatusText.Text = text;
    }

    private static FrameworkElement CompactSliderField(string label, Slider slider)
    {
        TextBlock valueText = new()
        {
            MinWidth = 42,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Brush("#F5F7FA"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            FontWeight = FontWeights.SemiBold,
        };
        void UpdateValue() => valueText.Text = slider.Value.ToString("0.##", CultureInfo.InvariantCulture);
        slider.ValueChanged += (_, _) => UpdateValue();
        UpdateValue();

        Grid sliderRow = new() { Margin = new Thickness(0, 2, 0, 8) };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueText, 1);
        sliderRow.Children.Add(slider);
        sliderRow.Children.Add(valueText);

        StackPanel field = new() { Margin = new Thickness(0, 0, 0, 2) };
        field.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#D7DEE6"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
        });
        field.Children.Add(sliderRow);
        return field;
    }

    private bool ValidateColors()
    {
        foreach ((string Label, TextBox Box) field in new[] { ("Lock-up colour", _singleColor) })
        {
            string value = field.Box.Text.Trim();
            if (value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)) continue;
            WpfMessageBox.Show(this, $"{field.Label} must be a six-digit colour such as #ffd84a.",
                "Invalid setting", MessageBoxButton.OK, MessageBoxImage.Warning);
            field.Box.Focus();
            return false;
        }

        return true;
    }

    private bool TryReadDouble(double candidate, double minimum, double maximum, string label, out double value)
    {
        value = candidate;
        if (double.IsFinite(value) && value >= minimum && value <= maximum) return true;
        WpfMessageBox.Show(this, $"{label} must be between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.",
            "Invalid setting", MessageBoxButton.OK, MessageBoxImage.Warning);
        value = 0;
        return false;
    }

    private bool TryReadInt(TextBox box, int minimum, int maximum, string label, out int value)
    {
        if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= minimum && value <= maximum) return true;
        WpfMessageBox.Show(this, $"{label} must be between {minimum} and {maximum}.",
            "Invalid setting", MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        value = 0;
        return false;
    }

    private static void ConfigureSlider(Slider slider, double minimum, double maximum,
        double smallChange, double largeChange, double value)
    {
        slider.Minimum = minimum;
        slider.Maximum = maximum;
        slider.SmallChange = smallChange;
        slider.LargeChange = largeChange;
        slider.TickFrequency = smallChange;
        slider.IsSnapToTickEnabled = false;
        slider.IsMoveToPointEnabled = true;
        slider.Value = Math.Clamp(value, minimum, maximum);
        slider.SetResourceReference(FrameworkElement.StyleProperty, "ThemedSliderStyle");
    }

    private static FrameworkElement ShortcutField(string label, TextBox box, string value)
    {
        box.Text = value;
        box.IsReadOnly = true;
        box.Cursor = WpfCursors.Hand;
        box.ToolTip = "Focus this field and press a modifier plus a key.";
        box.PreviewKeyDown += CaptureShortcutPreviewKeyDown;
        return Field(label, box, value);
    }

    private static void CaptureShortcutPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!ShortcutManager.TryFormat(key, Keyboard.Modifiers, out string text)) return;
        box.Text = text;
        box.SelectAll();
        e.Handled = true;
    }

    private void SteeringPositionPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CloseSteeringPositionDropDown();
        ScrollSettings(e);
    }

    private void SteeringPositionDropDownPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CloseSteeringPositionDropDown();
        ScrollSettings(e);
    }

    private void SettingsScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // A Popup is not clipped by the settings ScrollViewer. Dismiss it as
        // soon as scrolling changes the content position so it cannot detach
        // from the field that opened it. This also covers scrollbar dragging,
        // keyboard/page scrolling, and programmatic offset changes.
        if (e.VerticalChange != 0 || e.HorizontalChange != 0)
        {
            CloseSteeringPositionDropDown();
        }
    }

    private void CloseSteeringPositionDropDown()
    {
        if (_steeringPosition.IsDropDownOpen)
        {
            _steeringPosition.IsDropDownOpen = false;
        }
    }

    private void ShowSnackbar(string message)
    {
        SnackbarText.Text = message;
        Snackbar.Visibility = Visibility.Visible;
        _snackbarTimer?.Stop();
        _snackbarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _snackbarTimer.Tick += (_, _) =>
        {
            _snackbarTimer?.Stop();
            Snackbar.Visibility = Visibility.Collapsed;
        };
        _snackbarTimer.Start();
    }

    private void ScrollSettings(MouseWheelEventArgs e)
    {
        double nextOffset = SettingsScrollViewer.VerticalOffset - (e.Delta / 3.0);
        SettingsScrollViewer.ScrollToVerticalOffset(Math.Clamp(nextOffset, 0, SettingsScrollViewer.ScrollableHeight));
        e.Handled = true;
    }

    private static FrameworkElement ColorField(TextBox box, string value, out ColorPickerState state)
    {
        box.Text = value;
        PrepareControl(box);
        box.Padding = new Thickness(8, 5, 8, 5);
        box.Foreground = Brush("#f4f7fa");
        box.Background = Brush("#1a1e24");
        box.BorderBrush = Brush("#3b4552");
        box.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;

        Grid surface = new()
        {
            Height = 42,
            MinWidth = 0,
            ClipToBounds = true,
            Background = Brush("#101419"),
            ToolTip = "Choose saturation and brightness",
        };
        WpfRectangle hueLayer = new();
        WpfRectangle whiteLayer = new()
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new WpfPoint(0, 0.5),
                EndPoint = new WpfPoint(1, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new(Colors.White, 0),
                    new(Colors.Transparent, 1),
                },
            },
        };
        WpfRectangle shadeLayer = new()
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new WpfPoint(0.5, 0),
                EndPoint = new WpfPoint(0.5, 1),
                GradientStops = new GradientStopCollection
                {
                    new(WpfColor.FromArgb(0, 0, 0, 0), 0),
                    new(WpfColor.FromArgb(255, 0, 0, 0), 1),
                },
            },
        };
        Ellipse cursor = new()
        {
            Width = 12,
            Height = 12,
            Fill = WpfBrushes.Transparent,
            Stroke = WpfBrushes.White,
            StrokeThickness = 1.5,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        surface.Children.Add(hueLayer);
        surface.Children.Add(whiteLayer);
        surface.Children.Add(shadeLayer);
        surface.Children.Add(cursor);

        Slider hueSlider = new()
        {
            Minimum = 0,
            Maximum = 360,
            Height = 22,
            SmallChange = 1,
            LargeChange = 30,
            IsMoveToPointEnabled = true,
            IsSnapToTickEnabled = false,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            ToolTip = "Choose hue",
        };
        hueSlider.SetResourceReference(FrameworkElement.StyleProperty, "ColorHueSliderStyle");

        ColorPickerState picker = new()
        {
            Box = box,
            Surface = surface,
            HueLayer = hueLayer,
            Cursor = cursor,
            HueSlider = hueSlider,
        };
        state = picker;

        box.TextChanged += (_, _) =>
        {
            if (!picker.Updating) SyncColorPickerFromText(picker);
        };
        hueSlider.ValueChanged += (_, _) =>
        {
            if (picker.Updating) return;
            picker.Hue = hueSlider.Value;
            ApplyColorPickerSelection(picker);
        };
        surface.MouseLeftButtonDown += (_, e) =>
        {
            UpdateColorPickerSelection(picker, e.GetPosition(surface));
            surface.CaptureMouse();
            e.Handled = true;
        };
        surface.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            UpdateColorPickerSelection(picker, e.GetPosition(surface));
            e.Handled = true;
        };
        surface.MouseLeftButtonUp += (_, e) =>
        {
            surface.ReleaseMouseCapture();
            e.Handled = true;
        };
        surface.SizeChanged += (_, _) => UpdateColorPickerVisuals(picker);
        SyncColorPickerFromText(picker);

        Grid editor = new();
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        box.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(box, 0);
        Grid.SetRow(box, 0);
        Grid.SetColumn(surface, 1);
        Grid.SetRow(surface, 0);
        Grid.SetColumn(hueSlider, 1);
        Grid.SetRow(hueSlider, 1);
        editor.Children.Add(box);
        editor.Children.Add(surface);
        editor.Children.Add(hueSlider);
        return Field(editor);
    }

    private static void SyncColorPickerFromText(ColorPickerState state)
    {
        if (!TryParseHexColor(state.Box.Text, out WpfColor color)) return;

        ToHsv(color, out double hue, out double saturation, out double value);
        state.Updating = true;
        state.Hue = hue;
        state.Saturation = saturation;
        state.Value = value;
        state.HueSlider.Value = hue;
        state.Updating = false;
        UpdateColorPickerVisuals(state);
    }

    private static void UpdateColorPickerSelection(ColorPickerState state, WpfPoint point)
    {
        double width = Math.Max(1, state.Surface.ActualWidth);
        double height = Math.Max(1, state.Surface.ActualHeight);
        state.Saturation = Math.Clamp(point.X / width, 0, 1);
        state.Value = Math.Clamp(1 - (point.Y / height), 0, 1);
        ApplyColorPickerSelection(state);
    }

    private static void ApplyColorPickerSelection(ColorPickerState state)
    {
        state.Updating = true;
        state.Box.Text = ToHex(HsvToColor(state.Hue, state.Saturation, state.Value));
        state.Updating = false;
        UpdateColorPickerVisuals(state);
    }

    private static void UpdateColorPickerVisuals(ColorPickerState state)
    {
        state.HueLayer.Fill = new SolidColorBrush(HsvToColor(state.Hue, 1, 1));
        double width = Math.Max(1, state.Surface.ActualWidth);
        double height = Math.Max(1, state.Surface.ActualHeight);
        state.Cursor.Margin = new Thickness(
            Math.Clamp(state.Saturation * width - state.Cursor.Width / 2, -state.Cursor.Width / 2, width - state.Cursor.Width / 2),
            Math.Clamp((1 - state.Value) * height - state.Cursor.Height / 2, -state.Cursor.Height / 2, height - state.Cursor.Height / 2),
            0,
            0);
    }

    private static bool TryParseHexColor(string? text, out WpfColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim();
        if (value.Length != 7 || value[0] != '#') return false;
        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)
            || !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)
            || !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return false;
        color = WpfColor.FromRgb(red, green, blue);
        return true;
    }

    private static string ToHex(WpfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void ToHsv(WpfColor color, out double hue, out double saturation, out double value)
    {
        double red = color.R / 255d;
        double green = color.G / 255d;
        double blue = color.B / 255d;
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;
        hue = 0;
        if (delta > 0)
        {
            if (maximum == red) hue = 60 * (((green - blue) / delta) % 6);
            else if (maximum == green) hue = 60 * (((blue - red) / delta) + 2);
            else hue = 60 * (((red - green) / delta) + 4);
            if (hue < 0) hue += 360;
        }

        saturation = maximum == 0 ? 0 : delta / maximum;
        value = maximum;
    }

    private static WpfColor HsvToColor(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        double match = value - chroma;
        (double Red, double Green, double Blue) rgb = hue switch
        {
            < 60 => (chroma, x, 0),
            < 120 => (x, chroma, 0),
            < 180 => (0, chroma, x),
            < 240 => (0, x, chroma),
            < 300 => (x, 0, chroma),
            _ => (chroma, 0, x),
        };
        return WpfColor.FromRgb(
            (byte)Math.Round((rgb.Red + match) * 255),
            (byte)Math.Round((rgb.Green + match) * 255),
            (byte)Math.Round((rgb.Blue + match) * 255));
    }

    private static FrameworkElement Section(string title, string description, params FrameworkElement[] children)
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("#f4f7fa"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new Border
        {
            Width = 34,
            Height = 2,
            Background = Brush("#e10600"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 9),
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = Brush("#aab5c2"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        foreach (FrameworkElement child in children)
        {
            content.Children.Add(child);
        }

        return new Border
        {
            Background = Brush("#151c24"),
            BorderBrush = Brush("#2b3744"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Child = content,
        };
    }

    private static FrameworkElement Field(string label, Control control, string? value = null)
    {
        PrepareControl(control);

        if (control is TextBox textBox)
        {
            textBox.Text = value ?? string.Empty;
            textBox.Padding = new Thickness(8, 5, 8, 5);
            textBox.Foreground = Brush("#f4f7fa");
            textBox.Background = Brush("#1a1e24");
            textBox.BorderBrush = Brush("#3b4552");
            textBox.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        }
        else if (control is Slider slider)
        {
            slider.SetResourceReference(FrameworkElement.StyleProperty, "ThemedSliderStyle");
        }
        else
        {
            control.Foreground = Brush("#f4f7fa");
            control.Background = Brush("#1a1e24");
            control.BorderBrush = Brush("#3b4552");
        }

        FrameworkElement editor = control;
        if (control is Slider valueSlider)
        {
            Grid sliderEditor = new();
            sliderEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sliderEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock valueText = new()
            {
                MinWidth = 48,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brush("#f4f7fa"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                FontWeight = FontWeights.SemiBold,
            };
            void UpdateSliderValue() => valueText.Text = valueSlider.Value.ToString("0.##", CultureInfo.InvariantCulture);
            valueSlider.ValueChanged += (_, _) => UpdateSliderValue();
            UpdateSliderValue();
            Grid.SetColumn(valueSlider, 0);
            Grid.SetColumn(valueText, 1);
            sliderEditor.Children.Add(valueSlider);
            sliderEditor.Children.Add(valueText);
            editor = sliderEditor;
        }

        return Field(label, editor);
    }

    private static FrameworkElement Field(string label, FrameworkElement editor)
    {
        editor.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        editor.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        editor.MinWidth = 0;
        editor.Margin = new Thickness(0);

        Grid grid = new()
        {
            Margin = new Thickness(0, 0, 0, 9),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        TextBlock caption = new()
        {
            Text = label,
            Foreground = Brush("#d7dee6"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(caption);
        grid.Children.Add(editor);
        return grid;
    }

    private static FrameworkElement Field(FrameworkElement editor)
    {
        editor.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        editor.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        editor.MinWidth = 0;
        editor.Margin = new Thickness(0);

        Grid grid = new()
        {
            Margin = new Thickness(0, 0, 0, 9),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(editor, 0);
        grid.Children.Add(editor);
        return grid;
    }

    private static void PrepareControl(Control control)
    {
        control.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        control.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        control.MinWidth = 0;
        control.Margin = new Thickness(0);
    }

    private static SolidColorBrush Brush(string value)
    {
        SolidColorBrush brush = new((WpfColor)WpfColorConverter.ConvertFromString(value)!);
        brush.Freeze();
        return brush;
    }
}
