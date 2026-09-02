using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class TyreWearBitmapSmokeTests
{
    [Fact]
    public void RendersRepresentativeWearBandsToTransparentFourCircleBitmap()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                TyreWearSurface surface = new();
                surface.Initialize(AppSettings.Default.TyreWearOverlay);
                const int size = 256;
                surface.Measure(new Size(size, size));
                surface.Arrange(new Rect(0, 0, size, size));
                surface.SetTelemetry(new TyreWearTelemetry(62, 78, 12, 45, 1));
                surface.UpdateLayout();

                RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                string artifact = Path.Combine(Path.GetTempPath(), "f1-tyre-wear-bitmap-smoke.png");
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream output = File.Create(artifact)) encoder.Save(output);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "f1-tyre-wear-bitmap-smoke.path"), artifact);

                Assert.Equal(size, bitmap.PixelWidth);
                Assert.Equal(size, bitmap.PixelHeight);
                Assert.Equal(0, AlphaAt(bitmap, 0, 0));

                // The progress arc's midpoint is sampled at each expected
                // visual position: top FL/FR, bottom RL/RR.
                AssertBand(bitmap, 64, 64, 12, 0x42, 0xE3, 0x7C);
                AssertBand(bitmap, 192, 64, 45, 0xFF, 0xD8, 0x4A);
                AssertBand(bitmap, 64, 192, 62, 0xFF, 0x8A, 0x2A);
                AssertBand(bitmap, 192, 192, 78, 0xFF, 0x42, 0x61);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA bitmap render thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void AssertBand(RenderTargetBitmap bitmap, int centerX, int centerY,
        double percentage, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        double radians = (-90 + (percentage * 3.6 / 2)) * Math.PI / 180;
        int x = (int)Math.Round(centerX + Math.Cos(radians) * 57.5);
        int y = (int)Math.Round(centerY + Math.Sin(radians) * 57.5);
        for (int offsetY = -2; offsetY <= 2; offsetY++)
        {
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                (byte red, byte green, byte blue, byte alpha) = PixelAt(bitmap, x + offsetX, y + offsetY);
                if (alpha > 200 && red == expectedRed && green == expectedGreen && blue == expectedBlue) return;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected {expectedRed:X2}{expectedGreen:X2}{expectedBlue:X2} arc near ({x},{y}) for {percentage}%.");
    }

    private static byte AlphaAt(RenderTargetBitmap bitmap, int x, int y) => PixelAt(bitmap, x, y).alpha;

    private static (byte red, byte green, byte blue, byte alpha) PixelAt(RenderTargetBitmap bitmap, int x, int y)
    {
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }
}
