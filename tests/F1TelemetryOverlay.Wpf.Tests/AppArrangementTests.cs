using System.Reflection;
using System.Windows;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class AppArrangementTests
{
    [Fact]
    public void ArrangementRestoresPreArrangeGlobalVisibilityOnSta()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            App? app = null;
            MainWindow? pedals = null;
            TyreWearWindow? tyres = null;
            TemperatureWindow? temperatures = null;
            try
            {
                app = new App();
                SetField(app, "_settings", AppSettings.Default with
                {
                    PedalsOverlay = AppSettings.Default.PedalsOverlay with { Locked = true },
                    TyreWearOverlay = AppSettings.Default.TyreWearOverlay with { Enabled = true, Locked = true },
                    TemperatureOverlay = AppSettings.Default.TemperatureOverlay with { Enabled = true, Locked = true },
                });
                pedals = new MainWindow(app);
                tyres = new TyreWearWindow(app);
                temperatures = new TemperatureWindow(app);
                SetField(app, "_overlay", pedals);
                SetField(app, "_tyreOverlay", tyres);
                SetField(app, "_temperatureOverlay", temperatures);
                SetField(app, "_overlaysVisible", false);

                Assert.True(app.BeginArrangeOverlays());
                Assert.True(app.IsArranging);
                Assert.True((bool)GetField(app, "_overlaysVisible")!);
                Assert.True(pedals.IsVisible);
                Assert.True(temperatures.IsVisible);
                Assert.False((bool)GetField(pedals, "_widgetLocked")!);
                Assert.False((bool)GetField(tyres, "_locked")!);

                app.EndArrangeOverlays();
                Assert.False(app.IsArranging);
                Assert.False((bool)GetField(app, "_overlaysVisible")!);
                Assert.False(pedals.IsVisible);
                Assert.False(temperatures.IsVisible);
                Assert.True((bool)GetField(pedals, "_widgetLocked")!);
                Assert.True((bool)GetField(tyres, "_locked")!);
                // A temperature-only layout must still participate in global
                // visibility and lock shortcuts.
                SetField(app, "_settings", app.Settings with
                {
                    PedalsOverlay = app.Settings.PedalsOverlay with { Enabled = false },
                    TyreWearOverlay = app.Settings.TyreWearOverlay with { Enabled = false },
                });
                app.ShowOverlay();
                Assert.True(app.IsOverlayVisible);
                Assert.False(pedals.IsVisible);
                Assert.False(tyres.IsVisible);
                Assert.True(temperatures.IsVisible);
                Assert.True(app.IsLocked);
                app.SetLocked(false);
                Assert.False(app.Settings.TemperatureOverlay.Locked);
                app.HideOverlay();
                Assert.False(app.IsOverlayVisible);
                TemperatureTelemetry reading = new(
                    new(700, 91, 95), new(710, 92, 96), new(720, 93, 97), new(730, 94, 98),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                object surface = GetField(temperatures, "_surface")!;
                typeof(App).GetMethod("ReceiveTemperatures", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, [reading]);
                Assert.Equal(reading, GetField(surface, "_telemetry"));
                typeof(App).GetMethod("ReceiveStatus", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, [new OverlayStatus(ConnectionState.Listening, "Waiting", 20777)]);
                Assert.Null(GetField(surface, "_telemetry"));
                app.SetDemoEnabled(true);
                typeof(App).GetMethod("ReceiveTemperatures", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(app, [reading]);
                Assert.Null(GetField(surface, "_telemetry"));
                app.SetDemoEnabled(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                pedals?.Close();
                tyres?.Close();
                temperatures?.Close();
                app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA arrangement visibility thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);

    private static void SetField(object instance, string name, object? value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(instance, value);
}
