using System;
using System.Drawing;
using System.IO;
using SkiaSharp;

namespace White2UpgradeEditor;

/// <summary>Frost's Gen 5 Editor 16x1 PNG palette strip interchange.</summary>
internal static class FrostPalette
{
    public static Color[] Import(Stream input)
    {
        using var image = SKBitmap.Decode(input)
            ?? throw new InvalidDataException("The image could not be decoded. Choose a PNG palette file.");
        if (image.Width != 16 || image.Height != 1)
            throw new InvalidDataException($"Expected a Frost 16×1 PNG palette (16 colors); got {image.Width}×{image.Height}.");

        var result = new Color[16];
        for (int i = 0; i < result.Length; i++)
        {
            SKColor color = image.GetPixel(i, 0);
            result[i] = Color.FromArgb(255, color.Red, color.Green, color.Blue);
        }
        return result;
    }

    public static void Export(Stream output, Color[] colors)
    {
        if (colors.Length != 16)
            throw new InvalidDataException($"Frost palette export requires exactly 16 colors; found {colors.Length}.");

        using var bitmap = new SKBitmap(16, 1, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (int i = 0; i < colors.Length; i++)
            bitmap.SetPixel(i, 0, new SKColor(colors[i].R, colors[i].G, colors[i].B, 255));

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Failed to encode the palette PNG.");
        png.SaveTo(output);
    }
}
