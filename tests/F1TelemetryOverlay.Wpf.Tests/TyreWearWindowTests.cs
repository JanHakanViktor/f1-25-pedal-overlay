using System.Windows;
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
                window.ClearWear();
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
