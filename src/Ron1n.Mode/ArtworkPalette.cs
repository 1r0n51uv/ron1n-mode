using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

namespace Ron1n.Mode;

internal static class ArtworkPalette
{
    public static async Task<Color> ReadAsync(string? path, string? id)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var transform = new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24 };
                var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                var pixels = data.DetachPixelData();
                double red = 0, green = 0, blue = 0, weight = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var r = pixels[i + 2];
                    var g = pixels[i + 1];
                    var b = pixels[i];
                    var saturation = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    if (pixels[i + 3] < 128 || saturation < 24) continue;
                    weight += saturation;
                    red += saturation * r;
                    green += saturation * g;
                    blue += saturation * b;
                }
                if (weight > 0) return Color.FromArgb(255, Lift(red / weight), Lift(green / weight), Lift(blue / weight));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException
                or System.Runtime.InteropServices.COMException) { }
        }
        return (id?.Aggregate(0, (value, character) => (value + character) % 3) ?? 0) switch
        {
            1 => Color.FromArgb(255, 255, 171, 119),
            2 => Color.FromArgb(255, 199, 171, 255),
            _ => Color.FromArgb(255, 110, 230, 211)
        };

        static byte Lift(double value) => (byte)(100 + value * .60);
    }
}
