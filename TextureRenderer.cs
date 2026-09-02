using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperCare;

public static class TextureRenderer
{
    public static readonly string[] Names = { "细纹纸", "棉麻纸", "柔雾纸", "夜读纸" };

    private static readonly double[] TextureAlpha = { .28, .34, .22, .30 };

    // Premultiplied BGRA, shared by desktop overlays and the preview.
    public static byte[] Tile(Settings settings)
    {
        var bytes = new byte[256 * 256 * 4];
        var texture = Math.Clamp(settings.Texture, 0, Names.Length - 1);
        var intensity = Math.Clamp(settings.Intensity, 0, 100) / 100.0;
        var warmth = Math.Clamp(settings.Warmth, 0, 100) / 100.0 * .22;
        var dim = Math.Clamp(settings.Dim, 0, 50) / 50.0 * .42;

        // Avoid allocating and walking a tile when the requested effect has no opacity.
        if (intensity == 0 && warmth == 0 && dim == 0)
            return bytes;

        var random = new Random(7301 + texture);
        for (int y = 0; y < 256; y++)
        for (int x = 0; x < 256; x++)
        {
            double noise = random.NextDouble();
            double grain = texture switch
            {
                0 => .18 + noise * .82,
                1 => .38 + noise * .34 + ((x % 6 == 0 || y % 7 == 0) ? .24 : 0),
                2 => .54 + noise * .18 + (Math.Sin((x + y) * .08) + 1) * .04,
                _ => .28 + noise * .62
            };

            var textureOpacity = intensity * TextureAlpha[texture] * Math.Clamp(grain, 0, 1);
            var tone = texture == 3 ? 38 + noise * 22 : 224 + noise * 24;
            var textureRed = texture == 3 ? tone * .92 : tone;
            var textureGreen = texture == 3 ? tone * .98 : tone;
            var textureBlue = texture == 3 ? tone * 1.08 : tone;

            // Composite texture, warm tint, and dim tint in the same order for both
            // the WPF preview and the layered desktop window. Values written below
            // are premultiplied by the final alpha as required by PArgb.
            var alphaBeforeDim = warmth + textureOpacity * (1 - warmth);
            var alpha = alphaBeforeDim * (1 - dim) + dim;
            var redPremultiplied = (textureRed * textureOpacity * (1 - warmth) + 255 * warmth) * (1 - dim);
            var greenPremultiplied = (textureGreen * textureOpacity * (1 - warmth) + 178 * warmth) * (1 - dim);
            var bluePremultiplied = (textureBlue * textureOpacity * (1 - warmth) + 76 * warmth) * (1 - dim);

            int i = (y * 256 + x) * 4;
            var alphaByte = (byte)Math.Clamp((int)Math.Round(alpha * 255), 0, 255);
            bytes[i] = (byte)Math.Min(alphaByte, Math.Clamp((int)Math.Round(bluePremultiplied), 0, 255));
            bytes[i + 1] = (byte)Math.Min(alphaByte, Math.Clamp((int)Math.Round(greenPremultiplied), 0, 255));
            bytes[i + 2] = (byte)Math.Min(alphaByte, Math.Clamp((int)Math.Round(redPremultiplied), 0, 255));
            bytes[i + 3] = alphaByte;
        }
        return bytes;
    }

    public static bool IsFullyTransparent(byte[] tile)
    {
        if (tile is null || tile.Length != 256 * 256 * 4)
            return false;
        for (var i = 3; i < tile.Length; i += 4)
            if (tile[i] != 0) return false;
        return true;
    }

    public static Bitmap Bitmap(int width, int height, byte[] tile)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (tile is null || tile.Length != 256 * 256 * 4) throw new ArgumentException("纹理数据长度无效。", nameof(tile));
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x += 256)
                    Buffer.BlockCopy(tile, (y % 256) * 1024, row, x * 4, Math.Min(256, width - x) * 4);
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
    public static ImageBrush Brush(Settings settings)
    {
        var source = BitmapSource.Create(256, 256, 96, 96, PixelFormats.Pbgra32, null, Tile(settings), 1024);
        source.Freeze();
        var brush = new ImageBrush(source) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new System.Windows.Rect(0, 0, 256, 256), Stretch = Stretch.None };
        brush.Freeze();
        return brush;
    }
}
