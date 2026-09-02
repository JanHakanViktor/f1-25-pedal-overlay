using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void HubConstructsAtMinimumSizeNavigatesPagesAndRendersOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                SettingsWindow window = new(AppSettings.Default, _ => (true, string.Empty));
                Assert.Equal("Telemetry Hub", window.Title);
                Assert.Equal(780, window.MinWidth);
                Assert.Equal(600, window.MinHeight);
                Assert.Equal(Visibility.Visible, window.FindName("OverlaysPage") is StackPanel overlays
                    ? overlays.Visibility
                    : Visibility.Collapsed);
                Assert.NotNull(window.FindName("ArrangeOverlaysButton"));
                Assert.NotNull(window.FindName("PedalsEnabledToggle"));
                Assert.NotNull(window.FindName("TyreWearEnabledToggle"));
                Assert.NotNull(window.FindName("ConnectionStatusText"));

                foreach (string page in new[] { "Dashboard", "Overlays", "Connection", "Appearance", "Shortcuts" })
                {
                    window.Navigate(page);
                    Assert.Equal(page, window.FindName($"{page}NavigationButton") is Button button
                        ? (string)button.Tag
                        : string.Empty);
                }

                // Keep deterministic visual artifacts for parent review. They
                // are deliberately outside the repository and are not test
                // fixtures or release artifacts.
                window.Navigate("Overlays");
                window.Width = 780;
                window.Height = 600;
                window.Measure(new Size(780, 600));
                window.Arrange(new Rect(0, 0, 780, 600));
                window.Show();
                window.UpdateLayout();
                Assert.Equal(780, window.RenderSize.Width);
                Assert.Equal(600, window.RenderSize.Height);
                ScrollViewer settingsScroll = (ScrollViewer)window.FindName("SettingsScrollViewer")!;
                Assert.True(settingsScroll.ExtentWidth <= settingsScroll.ViewportWidth + 1,
                    $"Hub content overflows horizontally: extent={settingsScroll.ExtentWidth}, viewport={settingsScroll.ViewportWidth}.");
                Border footer = (Border)window.FindName("FooterBar")!;
                StackPanel footerActions = (StackPanel)window.FindName("FooterActions")!;
                TextBlock footerHint = (TextBlock)window.FindName("FooterHintText")!;
                Point actionsOrigin = footerActions.TranslatePoint(new Point(0, 0), footer);
                Point hintOrigin = footerHint.TranslatePoint(new Point(0, 0), footer);
                Assert.True(hintOrigin.X + footerHint.ActualWidth <= actionsOrigin.X + 1,
                    "Footer hint overlaps action buttons at minimum Hub size.");
                RenderTargetBitmap bitmap = new(780, 600, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                string path = Path.Combine(Path.GetTempPath(), "telemetry-hub-bitmap-smoke.png");
                if (File.Exists(path)) File.Delete(path);
                using (FileStream stream = File.Create(path))
                {
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }
                Assert.True(new FileInfo(path).Length > 0);

                // Also preserve the default-size view so visual review can
                // inspect both overlay cards without scrolling.
                window.Width = 1000;
                window.Height = 720;
                window.Measure(new Size(1000, 720));
                window.Arrange(new Rect(0, 0, 1000, 720));
                window.UpdateLayout();
                Assert.Equal(1000, window.RenderSize.Width);
                Assert.Equal(720, window.RenderSize.Height);
                RenderTargetBitmap defaultBitmap = new(1000, 720, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                defaultBitmap.Render(window);
                string defaultPath = Path.Combine(Path.GetTempPath(), "telemetry-hub-default-bitmap-smoke.png");
                if (File.Exists(defaultPath)) File.Delete(defaultPath);
                using (FileStream stream = File.Create(defaultPath))
                {
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(defaultBitmap));
                    encoder.Save(stream);
                }
                Assert.True(new FileInfo(defaultPath).Length > 0);

                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA Hub thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void HubNavigationShowsOnlySelectedPageOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                SettingsWindow window = new(AppSettings.Default, _ => (true, string.Empty));
                string[] pages = ["Dashboard", "Overlays", "Connection", "Appearance", "Shortcuts"];

                foreach (string selectedPage in pages)
                {
                    window.Navigate(selectedPage);
                    foreach (string page in pages)
                    {
                        StackPanel panel = (StackPanel)window.FindName($"{page}Page")!;
                        Assert.Equal(page == selectedPage ? Visibility.Visible : Visibility.Collapsed, panel.Visibility);
                    }
                }

                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA navigation thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void ArrangeSavesBeforeBeginAndClosingRestoresOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                bool saved = false;
                bool began = false;
                int endCalls = 0;
                SettingsWindow window = new(AppSettings.Default, _ =>
                {
                    saved = true;
                    return (true, string.Empty);
                }, () =>
                {
                    Assert.True(saved);
                    began = true;
                    return true;
                }, () => endCalls++);

                Invoke(window, "ArrangeOverlaysClicked", new object?[] { window, new RoutedEventArgs() });
                Assert.True(began);
                Assert.True(window.IsArranging);
                window.Close();
                Assert.Equal(1, endCalls);
                Assert.False(window.IsArranging);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA arrange thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void FailedArrangeStartLeavesHubOutOfArrangeModeOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                bool beginCalled = false;
                SettingsWindow window = new(AppSettings.Default, _ => (true, string.Empty), () =>
                {
                    beginCalled = true;
                    return false;
                }, () => throw new Xunit.Sdk.XunitException("EndArrange must not run after a failed begin."));

                Invoke(window, "ArrangeOverlaysClicked", new object?[] { window, new RoutedEventArgs() });

                Assert.True(beginCalled);
                Assert.False(window.IsArranging);
                Assert.Equal(Visibility.Visible, window.FindName("ArrangeOverlaysButton") is Button arrange
                    ? arrange.Visibility
                    : Visibility.Collapsed);
                Assert.Equal(Visibility.Collapsed, window.FindName("DoneArrangingButton") is Button done
                    ? done.Visibility
                    : Visibility.Visible);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA failed-arrange thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void FailedArrangeSavePreventsArrangeStartOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                bool beginCalled = false;
                SettingsWindow window = new(AppSettings.Default, _ => (false, "Synthetic save failure"), () =>
                {
                    beginCalled = true;
                    return true;
                });

                // SavePending displays the product's warning dialog before it
                // returns false. Close only that native dialog so the test can
                // observe that the arrange callback was never reached.
                DispatcherTimer dialogCloser = new(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(25),
                };
                dialogCloser.Tick += (_, _) =>
                {
                    IntPtr dialog = FindWindow(null, "Settings not saved");
                    if (dialog == IntPtr.Zero) return;
                    PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
                    dialogCloser.Stop();
                };
                dialogCloser.Start();
                Invoke(window, "ArrangeOverlaysClicked", new object?[] { window, new RoutedEventArgs() });
                dialogCloser.Stop();

                Assert.False(beginCalled);
                Assert.False(window.IsArranging);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA failed-save thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void CancelClosesHubWithoutPersistingPendingCardChangesOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                int saves = 0;
                SettingsWindow window = new(AppSettings.Default, _ =>
                {
                    saves++;
                    return (true, string.Empty);
                });
                ((CheckBox)window.FindName("TyreWearEnabledToggle")!).IsChecked = true;
                ((Button)window.FindName("CancelButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, saves);
                Assert.False(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA cancel thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void OverlayCardControlsStayPendingUntilSaveAndResetPositionOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                AppSettings input = AppSettings.Default with
                {
                    PedalsOverlay = AppSettings.Default.PedalsOverlay with { Left = 120, Top = 80 },
                };
                AppSettings? candidate = null;
                SettingsWindow window = new(input, value =>
                {
                    candidate = value;
                    return (true, string.Empty);
                });
                CheckBox enabled = (CheckBox)window.FindName("PedalsEnabledToggle")!;
                Slider scale = (Slider)window.FindName("PedalsScaleSlider")!;
                Button reset = (Button)window.FindName("PedalsResetPositionButton")!;
                enabled.IsChecked = false;
                scale.Value = 1.4;
                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(candidate);

                Invoke(window, "SaveClicked", new object?[] { window, new RoutedEventArgs() });

                Assert.NotNull(candidate);
                Assert.False(candidate!.PedalsOverlay.Enabled);
                Assert.Equal(1.4, candidate.PedalsOverlay.Scale);
                Assert.Null(candidate.PedalsOverlay.Left);
                Assert.Null(candidate.PedalsOverlay.Top);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA card thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void LaterSaveKeepsLatestPositionFromCurrentSettingsProviderOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                AppSettings current = AppSettings.Default with
                {
                    PedalsOverlay = AppSettings.Default.PedalsOverlay with { Left = 120, Top = 80 },
                };
                List<AppSettings> saves = [];
                SettingsWindow window = new(current, value =>
                {
                    saves.Add(value);
                    current = value;
                    return (true, string.Empty);
                }, currentSettings: () => current);

                Invoke(window, "SaveClicked", new object?[] { window, new RoutedEventArgs() });
                current = current with
                {
                    PedalsOverlay = current.PedalsOverlay with { Left = 900, Top = 901 },
                };
                Slider scale = (Slider)window.FindName("PedalsScaleSlider")!;
                scale.Value = 1.2;
                Invoke(window, "SaveClicked", new object?[] { window, new RoutedEventArgs() });

                Assert.Equal(2, saves.Count);
                Assert.Equal(900, saves[1].PedalsOverlay.Left);
                Assert.Equal(901, saves[1].PedalsOverlay.Top);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA stale-baseline thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void HeaderStatusUsesConnectedListeningAndErrorStatesOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                OverlayStatus status = new(ConnectionState.Listening, "", 20777);
                SettingsWindow window = new(AppSettings.Default, _ => (true, string.Empty),
                    currentSettings: () => AppSettings.Default,
                    statusProvider: () => status);
                TextBlock header = (TextBlock)window.FindName("HeaderStatusText")!;

                Invoke(window, "RefreshStatus", Array.Empty<object?>());
                Assert.Equal("Waiting on UDP 20777", header.Text);
                status = new OverlayStatus(ConnectionState.Connected, "", 20777);
                Invoke(window, "RefreshStatus", Array.Empty<object?>());
                Assert.Equal("F1 25 · Connected", header.Text);
                status = new OverlayStatus(ConnectionState.Error, "Receiver stopped", 20777);
                Invoke(window, "RefreshStatus", Array.Empty<object?>());
                Assert.Equal("Error · Receiver stopped", header.Text);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA status thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void RestoreDefaultsResetsPendingWidgetStateBeforeSaveOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                AppSettings input = AppSettings.Default with
                {
                    PedalsOverlay = AppSettings.Default.PedalsOverlay with
                    {
                        Locked = true,
                        Opacity = 0.8,
                        Scale = 1.5,
                        Left = 120,
                        Top = 80,
                    },
                    TyreWearOverlay = AppSettings.Default.TyreWearOverlay with
                    {
                        Enabled = true,
                        Locked = true,
                        Opacity = 0.9,
                        Scale = 1.4,
                        Left = 600,
                        Top = 240,
                    },
                    LockupColorMode = LockupColorMode.Single,
                    LockupColors = new LockupColorSettings("#010203", "#040506", "#070809", "#0a0b0c"),
                };
                AppSettings? candidate = null;
                SettingsWindow window = new(input, value =>
                {
                    candidate = value;
                    return (true, string.Empty);
                });

                Invoke(window, "RestoreDefaultsClicked", new object?[] { window, new RoutedEventArgs() });
                Invoke(window, "SaveClicked", new object?[] { window, new RoutedEventArgs() });

                Assert.NotNull(candidate);
                Assert.Equal(AppSettings.Default.PedalsOverlay, candidate!.PedalsOverlay);
                Assert.Equal(AppSettings.Default.TyreWearOverlay, candidate.TyreWearOverlay);
                Assert.Equal(AppSettings.Default.LockupColorMode, candidate.LockupColorMode);
                Assert.Equal(AppSettings.Default.LockupColors, candidate.LockupColors);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA settings thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void Invoke(SettingsWindow window, string method, object?[] arguments) =>
        typeof(SettingsWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, arguments);

    private const uint WmClose = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);
}
