using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using F1TelemetryOverlay.Core;
using F1TelemetryOverlay.Wpf;
using Xunit;

namespace F1TelemetryOverlay.Wpf.Tests;

public sealed class TemperatureBitmapSmokeTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void RendersPhysicalWheelLayoutAtSupportedScale(double scale)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                const long now = 50_000;
                TemperatureSurface surface = new(() => now);
                surface.Initialize(AppSettings.Default.TemperatureOverlay);
                int width = (int)(TemperatureWindow.BaseWidth * scale);
                int height = (int)(TemperatureWindow.BaseHeight * scale);
                surface.Measure(new Size(width, height));
                surface.Arrange(new Rect(0, 0, width, height));
                surface.SetTelemetry(new TemperatureTelemetry(
                    new WheelTemperatures(199, 69, 999),
                    new WheelTemperatures(799, 99, 999),
                    new WheelTemperatures(999, 109, 999),
                    new WheelTemperatures(1200, 99, 999),
                    now));
                surface.UpdateLayout();

                RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);

                string artifact = Path.Combine(Path.GetTempPath(), scale == 2.0
                    ? "temperature-overlay-smoke.png"
                    : "temperature-overlay-smoke-0.5x.png");
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream output = File.Create(artifact)) encoder.Save(output);

                Assert.Equal(width, bitmap.PixelWidth);
                Assert.Equal(height, bitmap.PixelHeight);
                Assert.Equal(0, AlphaAt(bitmap, 0, 0));
                Assert.Equal(0, AlphaAt(bitmap, width / 2, height / 4));
                if (scale == 2.0)
                {
                    AssertCellHasColour(bitmap, 0, 0, width / 2, height / 2, 0xFF, 0xD8, 0x4A); // FL surface
                    AssertCellHasColour(bitmap, width / 2, 0, width, height / 2, 0x42, 0xE3, 0x7C); // FR surface
                    AssertCellHasColour(bitmap, width / 2, 0, width, height / 2, 0xFF, 0x42, 0x61); // FR brake
                    AssertCellHasColour(bitmap, 0, height / 2, width / 2, height, 0x48, 0xA9, 0xFF); // RL surface/brake
                    AssertCellHasColour(bitmap, width / 2, height / 2, width, height, 0x42, 0xE3, 0x7C); // RR surface/brake
                }

            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA temperature-bitmap thread did not finish.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void AssertCellHasColour(RenderTargetBitmap bitmap, int left, int top, int right, int bottom,
        byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                (byte red, byte green, byte blue, byte alpha) = PixelAt(bitmap, x, y);
                if (alpha < 180) continue;
                int unpremultipliedRed = red * 255 / alpha;
                int unpremultipliedGreen = green * 255 / alpha;
                int unpremultipliedBlue = blue * 255 / alpha;
                int colourDistance = Math.Abs(unpremultipliedRed - expectedRed)
                    + Math.Abs(unpremultipliedGreen - expectedGreen)
                    + Math.Abs(unpremultipliedBlue - expectedBlue);
                if (colourDistance <= 55) return;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected colour #{expectedRed:X2}{expectedGreen:X2}{expectedBlue:X2} in cell.");
    }

    private static byte AlphaAt(RenderTargetBitmap bitmap, int x, int y) => PixelAt(bitmap, x, y).alpha;

    private static (byte red, byte green, byte blue, byte alpha) PixelAt(RenderTargetBitmap bitmap, int x, int y)
    {
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }
}
