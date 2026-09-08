using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class TemperatureWindowTests
{
    [Fact]
    public void WindowAppliesGeometryNativeStylesArrangementAndRenderingLifecycleOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                TemperatureWindow window = new();
                TemperatureSurface surface = SurfaceOf(window);
                AppSettings settings = AppSettings.Default with
                {
                    TemperatureOverlay = AppSettings.Default.TemperatureOverlay with
                    {
                        Enabled = true,
                        Locked = true,
                        Opacity = 0.8,
                        Scale = 0.5,
                    },
                };

                window.ApplySettings(settings);
                Assert.Equal(TemperatureWindow.BaseWidth * 0.5, window.Width);
                Assert.Equal(TemperatureWindow.BaseHeight * 0.5, window.Height);
                Assert.Equal(0.8, window.Opacity);
                Assert.True(surface.IsLocked);

                window.SetArrangeMode(true);
                Assert.True(surface.IsArranging);
                Assert.False(surface.IsLocked);
                Assert.Equal(Cursors.SizeAll, window.Cursor);
                window.SetArrangeMode(false);
                Assert.False(surface.IsArranging);
                Assert.True(surface.IsLocked);
                Assert.Equal(Cursors.Arrow, window.Cursor);

                window.Left = 10;
                window.Top = 10;
                window.Show();
                Assert.True(surface.IsRendering);
                window.UpdateTemperatures(Sample(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                IntPtr handle = new WindowInteropHelper(window).Handle;
                long extendedStyles = TyreWearNativeMethods.GetWindowLongPtr(
                    handle, TyreWearNativeMethods.GwlExStyle).ToInt64();
                Assert.False(window.ShowActivated);
                Assert.False(window.ShowInTaskbar);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.AllowsTransparency);
                Assert.NotEqual(0, extendedStyles & TyreWearNativeMethods.WsExNoActivate);
                Assert.NotEqual(0, extendedStyles & TyreWearNativeMethods.WsExToolWindow);

                window.Hide();
                Assert.False(surface.IsRendering);
                window.Show();
                Assert.True(surface.IsRendering);
                window.ClearTemperatures();

                double virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
                double virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
                window.Left = virtualRight + 1000;
                window.Top = virtualBottom + 1000;
                window.EnsureVisiblePosition();
                Assert.True(window.Left < virtualRight);
                Assert.True(window.Top < virtualBottom);
                Assert.True(window.Left + window.Width > SystemParameters.VirtualScreenLeft);
                Assert.True(window.Top + window.Height > SystemParameters.VirtualScreenTop);

                window.Close();
                Assert.False(surface.IsRendering);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA temperature-window thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void LostMouseCaptureCompletesAnActiveDrag()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                TemperatureWindow window = new() { Left = 35, Top = 45 };
                TemperatureSurface surface = SurfaceOf(window);
                (double left, double top)? completed = null;
                window.DragCompleted += (left, top) => completed = (left, top);
                typeof(TemperatureWindow).GetField("_dragging", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, true);

                MethodInfo handler = typeof(TemperatureWindow).GetMethod("SurfaceLostMouseCapture",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                handler.Invoke(window, [surface, new MouseEventArgs(Mouse.PrimaryDevice, 0)]);

                Assert.Equal((35d, 45d), completed);
                Assert.False((bool)typeof(TemperatureWindow).GetField("_dragging",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA lost-capture thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static TemperatureSurface SurfaceOf(TemperatureWindow window) =>
        (TemperatureSurface)typeof(TemperatureWindow).GetField("_surface",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static TemperatureTelemetry Sample(long timestamp) => new(
        new WheelTemperatures(200, 70, 75),
        new WheelTemperatures(800, 100, 105),
        new WheelTemperatures(1000, 110, 115),
        new WheelTemperatures(1200, 120, 125),
        timestamp);
}
