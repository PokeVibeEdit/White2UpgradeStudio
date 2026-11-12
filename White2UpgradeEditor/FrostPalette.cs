using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace White2UpgradeEditor;

/// <summary>Frost's Gen 5 Editor 16x1 PNG palette strip interchange.</summary>
internal static class FrostPalette
{
    public static Color[] Import(Stream input)
    {
        using var image = new Bitmap(input);
        if (image.Width != 16 || image.Height != 1)
            throw new InvalidDataException($"Expected a Frost 16×1 PNG palette (16 colors); got {image.Width}×{image.Height}.");
        var result = new Color[16];
        for (int i = 0; i < 16; i++)
        {
            var color = image.GetPixel(i, 0);
            result[i] = Color.FromArgb(255, color.R, color.G, color.B);
        }
        return result;
    }

    public static void Export(Stream output, Color[] colors)
    {
        if (colors.Length != 16)
            throw new InvalidDataException($"Frost palette export requires exactly 16 colors; found {colors.Length}.");
        using var image = new Bitmap(16, 1, PixelFormat.Format32bppArgb);
        for (int i = 0; i < 16; i++)
            image.SetPixel(i, 0, Color.FromArgb(255, colors[i].R, colors[i].G, colors[i].B));
        image.Save(output, ImageFormat.Png);
    }
}
