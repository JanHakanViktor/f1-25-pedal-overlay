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

namespace F1TelemetryOverlay.Wpf;

public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, (bool Ok, string Error)> _save;
    private readonly AppSettings _initial;
    // Widget state is a transactional form baseline. Normal saves preserve
    // these values (for example, positions moved while the settings window is
    // open), while Restore defaults deliberately replaces both baselines.
    private OverlayWidgetSettings _pendingPedalsOverlay;
    private OverlayWidgetSettings _pendingTyreWearOverlay;
    private readonly CheckBox _steeringDefault = new();
    private readonly CheckBox _tyreWearEnabled = new();
    private readonly ComboBox _steeringPosition = new();
    private readonly Slider _transparency = new();
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

    internal SettingsWindow(AppSettings settings, Func<AppSettings, (bool Ok, string Error)> save)
    {
        _initial = settings;
        _pendingPedalsOverlay = settings.PedalsOverlay;
        _pendingTyreWearOverlay = settings.TyreWearOverlay;
        _save = save;
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
        _steeringDefault.Content = "Enable steering by default";
        _steeringDefault.IsChecked = _initial.SteeringEnabledByDefault;
        _tyreWearEnabled.Content = "Enable tyre wear overlay";
        _tyreWearEnabled.IsChecked = _initial.TyreWearOverlay.Enabled;
        _steeringPosition.Items.Add("Left side of graph");
        _steeringPosition.Items.Add("Right side of graph");
        _steeringPosition.SelectedIndex = _initial.SteeringPosition == SteeringPosition.Right ? 1 : 0;
        _steeringPosition.PreviewMouseWheel += SteeringPositionPreviewMouseWheel;
        ConfigureSlider(_transparency, 0.2, 1, 0.01, 0.1, _initial.OverlayTransparency);
        ConfigureSlider(_sensitivity, 0.15, 0.9, 0.01, 0.1, _initial.LockupSensitivity);
        ConfigureSlider(_duration, 2, 15, 0.5, 1, _initial.GraphDurationSeconds);

        Options.Children.Add(Section(
            "Overlay",
            "Set the defaults used when the overlay starts.",
            _steeringDefault,
            _tyreWearEnabled,
            Field("Steering circle position", _steeringPosition),
            Field("Overlay transparency (0.2 - 1.0)", _transparency),
            Field("UDP port", _udpPort, _initial.UdpPort.ToString(CultureInfo.InvariantCulture)),
            Field("Lock-up sensitivity (0.15 - 0.9)", _sensitivity),
            Field("Graph duration in seconds (2 - 15)", _duration)));

        // The lock-up section has one colour editor, so a second caption would
        // only repeat information already conveyed by the section heading.
        FrameworkElement singleField = ColorField(_singleColor, _initial.LockupColors.Single, out _);

        Options.Children.Add(Section(
            "Lock-up colours",
            "Choose the colour used whenever a lock-up is detected.",
            singleField));

        Options.Children.Add(Section(
            "Global shortcuts",
            "Use combinations such as Control+Shift+H. Each shortcut must be unique and available.",
            ShortcutField("Show / hide overlay", _toggleVisibility, _initial.Shortcuts.ToggleVisibility),
            ShortcutField("Lock / unlock position", _toggleLock, _initial.Shortcuts.ToggleLock),
            ShortcutField("Toggle demo signal", _toggleDemo, _initial.Shortcuts.ToggleDemo),
            ShortcutField("Enable steering", _toggleSteering, _initial.Shortcuts.ToggleSteering),
            ShortcutField("Exit application", _quit, _initial.Shortcuts.Quit)));

    }

    private void RestoreDefaultsClicked(object sender, RoutedEventArgs e)
    {
        PopulateFromSettings(AppSettings.Default);
    }

    private void PopulateFromSettings(AppSettings settings)
    {
        _pendingPedalsOverlay = settings.PedalsOverlay;
        _pendingTyreWearOverlay = settings.TyreWearOverlay;
        _steeringDefault.IsChecked = settings.SteeringEnabledByDefault;
        _tyreWearEnabled.IsChecked = settings.TyreWearOverlay.Enabled;
        _steeringPosition.SelectedIndex = settings.SteeringPosition == SteeringPosition.Right ? 1 : 0;
        _transparency.Value = Math.Clamp(settings.OverlayTransparency, _transparency.Minimum, _transparency.Maximum);
        _udpPort.Text = settings.UdpPort.ToString(CultureInfo.InvariantCulture);
        _sensitivity.Value = Math.Clamp(settings.LockupSensitivity, _sensitivity.Minimum, _sensitivity.Maximum);
        _duration.Value = Math.Clamp(settings.GraphDurationSeconds, _duration.Minimum, _duration.Maximum);

        _singleColor.Text = settings.LockupColors.Single;

        _toggleVisibility.Text = settings.Shortcuts.ToggleVisibility;
        _toggleLock.Text = settings.Shortcuts.ToggleLock;
        _toggleDemo.Text = settings.Shortcuts.ToggleDemo;
        _toggleSteering.Text = settings.Shortcuts.ToggleSteering;
        _quit.Text = settings.Shortcuts.Quit;

    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadDouble(_transparency.Value, 0.2, 1, "Overlay transparency", out double transparency)
            || !TryReadInt(_udpPort, 1, 65535, "UDP port", out int port)
            || !TryReadDouble(_sensitivity.Value, 0.15, 0.9, "Lock-up sensitivity", out double sensitivity)
            || !TryReadDouble(_duration.Value, 2, 15, "Graph duration", out double duration)
            || !ValidateColors()) return;

        AppSettings candidate = new(
            _steeringDefault.IsChecked == true,
            transparency,
            port,
            sensitivity,
            duration,
            new ShortcutSettings(_toggleVisibility.Text.Trim(), _toggleLock.Text.Trim(), _toggleDemo.Text.Trim(),
                _toggleSteering.Text.Trim(), _quit.Text.Trim()),
            _initial.LockupColorMode,
            new LockupColorSettings(_initial.LockupColors.Front, _initial.LockupColors.Rear, _initial.LockupColors.Both, _singleColor.Text.Trim()))
        {
            SteeringPosition = _steeringPosition.SelectedIndex == 1 ? SteeringPosition.Right : SteeringPosition.Left,
            PedalsOverlay = _pendingPedalsOverlay with { Opacity = transparency },
            TyreWearOverlay = _pendingTyreWearOverlay with { Enabled = _tyreWearEnabled.IsChecked == true },
        };

        (bool ok, string error) = _save(candidate);
        if (!ok)
        {
            WpfMessageBox.Show(this, error, "Settings not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _pendingPedalsOverlay = candidate.PedalsOverlay;
        _pendingTyreWearOverlay = candidate.TyreWearOverlay;
        ShowSnackbar("Settings saved successfully.");
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
