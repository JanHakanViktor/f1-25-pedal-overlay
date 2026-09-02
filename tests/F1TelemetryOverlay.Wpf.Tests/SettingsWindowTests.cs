using System.Reflection;
using System.Windows;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class SettingsWindowTests
{
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
}
