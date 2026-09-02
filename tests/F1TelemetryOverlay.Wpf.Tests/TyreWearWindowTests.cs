using System.Windows;
using System.Windows.Interop;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class TyreWearWindowTests
{
    [Fact]
    public void WindowConstructsAndAppliesWidgetGeometryOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                TyreWearWindow window = new();
                AppSettings settings = AppSettings.Default with
                {
                    TyreWearOverlay = AppSettings.Default.TyreWearOverlay with
                    {
                        Enabled = true,
                        Opacity = 0.8,
                        Scale = 1.5,
                    },
                };

                window.ApplySettings(settings);

                Assert.Equal(192, window.Width);
                Assert.Equal(192, window.Height);
                Assert.Equal(0.8, window.Opacity);
                window.Left = 10;
                window.Top = 10;
                window.Show();
                window.UpdateWear(new TyreWearTelemetry(10, 20, 30, 40, 1));
                IntPtr handle = new WindowInteropHelper(window).Handle;
                long extendedStyles = TyreWearNativeMethods.GetWindowLongPtr(
                    handle, TyreWearNativeMethods.GwlExStyle).ToInt64();
                Assert.False(window.ShowActivated);
                Assert.False(window.ShowInTaskbar);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.AllowsTransparency);
                Assert.NotEqual(0, extendedStyles & TyreWearNativeMethods.WsExNoActivate);
                Assert.NotEqual(0, extendedStyles & TyreWearNativeMethods.WsExToolWindow);

                object surface = typeof(TyreWearWindow).GetField("_surface",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                object firstTimer = surface.GetType().GetField("_timer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
                window.Hide();
                window.Show();
                object secondTimer = surface.GetType().GetField("_timer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
                Assert.Same(firstTimer, secondTimer);
                window.ClearWear();
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
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA smoke thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
