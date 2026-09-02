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
            try
            {
                app = new App();
                SetField(app, "_settings", AppSettings.Default with
                {
                    PedalsOverlay = AppSettings.Default.PedalsOverlay with { Locked = true },
                    TyreWearOverlay = AppSettings.Default.TyreWearOverlay with { Enabled = true, Locked = true },
                });
                pedals = new MainWindow(app);
                tyres = new TyreWearWindow(app);
                SetField(app, "_overlay", pedals);
                SetField(app, "_tyreOverlay", tyres);
                SetField(app, "_overlaysVisible", false);

                Assert.True(app.BeginArrangeOverlays());
                Assert.True(app.IsArranging);
                Assert.True((bool)GetField(app, "_overlaysVisible")!);
                Assert.True(pedals.IsVisible);
                Assert.False((bool)GetField(pedals, "_widgetLocked")!);
                Assert.False((bool)GetField(tyres, "_locked")!);

                app.EndArrangeOverlays();
                Assert.False(app.IsArranging);
                Assert.False((bool)GetField(app, "_overlaysVisible")!);
                Assert.False(pedals.IsVisible);
                Assert.True((bool)GetField(pedals, "_widgetLocked")!);
                Assert.True((bool)GetField(tyres, "_locked")!);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                pedals?.Close();
                tyres?.Close();
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
