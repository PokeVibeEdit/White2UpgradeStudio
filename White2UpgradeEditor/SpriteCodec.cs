using System.Drawing;
using SkiaSharp;

namespace White2UpgradeEditor;

public static class SpriteCodec
{
    private static readonly (int X, int Y, int W, int H)[] SingleCell96 =
    [
        (0, 0, 64, 64), (64, 0, 32, 64), (0, 64, 64, 32), (64, 64, 32, 32)
    ];

    public static byte[] Decompress(byte[] blob)
    {
        if (blob.Length == 0 || (blob[0] != 0x10 && blob[0] != 0x11)) return blob.ToArray();
        byte kind = blob[0];
        int size = blob[1] | blob[2] << 8 | blob[3] << 16;
        int cursor = 4;
        if (size == 0)
        {
            size = BitConverter.ToInt32(blob, cursor);
            cursor += 4;
        }
        var output = new List<byte>(size);
        while (output.Count < size)
        {
            if (cursor >= blob.Length) throw new InvalidDataException("Truncated Nitro LZ stream.");
            byte flags = blob[cursor++];
            for (int bit = 7; bit >= 0 && output.Count < size; bit--)
            {
                if ((flags & (1 << bit)) == 0)
                {
                    if (cursor >= blob.Length) throw new InvalidDataException("Truncated Nitro LZ literal.");
                    output.Add(blob[cursor++]);
                    continue;
                }
                if (cursor + 1 >= blob.Length) throw new InvalidDataException("Truncated Nitro LZ back-reference.");
                byte first = blob[cursor++], second = blob[cursor++];
                int length, distance;
                if (kind == 0x10)
                {
                    length = (first >> 4) + 3;
                    distance = ((first & 15) << 8 | second) + 1;
                }
                else if ((first >> 4) == 0)
                {
                    if (cursor >= blob.Length) throw new InvalidDataException("Truncated LZ11 reference.");
                    byte third = blob[cursor++];
                    length = ((first & 15) << 4 | second >> 4) + 0x11;
                    distance = ((second & 15) << 8 | third) + 1;
                }
                else if ((first >> 4) == 1)
                {
                    if (cursor + 1 >= blob.Length) throw new InvalidDataException("Truncated LZ11 reference.");
                    byte third = blob[cursor++], fourth = blob[cursor++];
                    length = ((first & 15) << 12 | second << 4 | third >> 4) + 0x111;
                    distance = ((third & 15) << 8 | fourth) + 1;
                }
                else
                {
                    length = (first >> 4) + 1;
                    distance = ((first & 15) << 8 | second) + 1;
                }
                if (distance > output.Count) throw new InvalidDataException("Invalid Nitro LZ back-reference.");
                int start = output.Count - distance;
                for (int j = 0; j < length && output.Count < size; j++)
                    output.Add(output[start + j]);
            }
        }
        return output.ToArray();
    }

    public static byte[] CompressLz11(byte[] raw)
    {
        if (raw.Length >= 1 << 24) throw new InvalidOperationException("LZ11 input is too large.");
        using var output = new MemoryStream();
        output.WriteByte(0x11);
        output.WriteByte((byte)raw.Length);
        output.WriteByte((byte)(raw.Length >> 8));
        output.WriteByte((byte)(raw.Length >> 16));

        int cursor = 0;
        while (cursor < raw.Length)
        {
            long flagPosition = output.Position;
            output.WriteByte(0);
            byte flags = 0;
            for (int bit = 7; bit >= 0 && cursor < raw.Length; bit--)
            {
                FindMatch(raw, cursor, out int bestLength, out int bestDistance);
                if (bestLength >= 3)
                {
                    flags |= (byte)(1 << bit);
                    int length = bestLength;
                    int distance = bestDistance - 1;
                    if (length >= 0x111)
                    {
                        int value = length - 0x111;
                        output.WriteByte((byte)(0x10 | (value >> 12)));
                        output.WriteByte((byte)(value >> 4));
                        output.WriteByte((byte)(((value & 15) << 4) | (distance >> 8)));
                        output.WriteByte((byte)distance);
                    }
                    else if (length >= 0x11)
                    {
                        int value = length - 0x11;
                        output.WriteByte((byte)(value >> 4));
                        output.WriteByte((byte)(((value & 15) << 4) | (distance >> 8)));
                        output.WriteByte((byte)distance);
                    }
                    else
                    {
                        output.WriteByte((byte)(((length - 1) << 4) | (distance >> 8)));
                        output.WriteByte((byte)distance);
                    }
                    cursor += bestLength;
                }
                else
                {
                    output.WriteByte(raw[cursor++]);
                }
            }
            long end = output.Position;
            output.Position = flagPosition;
            output.WriteByte(flags);
            output.Position = end;
        }
        return output.ToArray();
    }

    private static void FindMatch(byte[] data, int pos, out int bestLength, out int bestDistance)
    {
        bestLength = 0;
        bestDistance = 0;
        int maxDistance = Math.Min(0x1000, pos);
        int maxLength = Math.Min(0x10110, data.Length - pos);
        if (maxLength < 3) return;

        // Search recent candidates first; DS sprite data is repetitive and this greedy
        // matcher is sufficient for 96x96 4bpp character resources.
        int candidates = 0;
        for (int distance = 1; distance <= maxDistance && candidates < 512; distance++)
        {
            if (data[pos] != data[pos - distance] ||
                data[pos + 1] != data[pos - distance + 1] ||
                data[pos + 2] != data[pos - distance + 2]) continue;
            candidates++;
            int length = 3;
            while (length < maxLength && data[pos + length] == data[pos + length - distance])
                length++;
            if (length > bestLength)
            {
                bestLength = length;
                bestDistance = distance;
                if (length == maxLength) return;
            }
        }
    }

    public static Color[] ReadPalette(byte[] blob)
    {
        byte[] raw = Decompress(blob);
        if (raw.Length < 40 || !raw.AsSpan(0, 4).SequenceEqual("RLCN"u8) ||
            !raw.AsSpan(16, 4).SequenceEqual("TTLP"u8))
            throw new InvalidDataException("Sprite palette is not a supported Nitro RLCN/TTLP resource.");
        int size = BitConverter.ToInt32(raw, 32);
        if (size < 0 || size % 2 != 0 || 40 + size > raw.Length)
            throw new InvalidDataException("Invalid Nitro palette data size.");
        var colors = new Color[size / 2];
        for (int i = 0; i < colors.Length; i++)
        {
            ushort v = BitConverter.ToUInt16(raw, 40 + i * 2);
            int r = (v & 31) * 255 / 31;
            int g = ((v >> 5) & 31) * 255 / 31;
            int b = ((v >> 10) & 31) * 255 / 31;
            colors[i] = Color.FromArgb(255, r, g, b);
        }
        return colors;
    }

    public static byte[] PatchPalette(byte[] original, Color[] colors)
    {
        byte[] raw = Decompress(original).ToArray();
        Color[] old = ReadPalette(original);
        if (colors.Length != old.Length) throw new InvalidOperationException("Palette size cannot change.");
        for (int i = 0; i < colors.Length; i++)
        {
            ushort oldValue = BitConverter.ToUInt16(raw, 40 + i * 2);
            ushort value = (ushort)(oldValue & 0x8000);
            value |= (ushort)(To5(colors[i].R) | (To5(colors[i].G) << 5) | (To5(colors[i].B) << 10));
            BitConverter.GetBytes(value).CopyTo(raw, 40 + i * 2);
        }
        return RecompressLike(original, raw);
    }

    public static SKBitmap DecodeSingleCellSprite(byte[] blob, Color[] palette)
    {
        byte[] raw = Decompress(blob);
        if (raw.Length < 48 || !raw.AsSpan(0, 4).SequenceEqual("RGCN"u8) ||
            !raw.AsSpan(16, 4).SequenceEqual("RAHC"u8))
            throw new InvalidDataException("Sprite is not a supported Nitro RGCN/RAHC character resource.");
        int heightTiles = BitConverter.ToUInt16(raw, 24);
        int widthTiles = BitConverter.ToUInt16(raw, 26);
        int width = widthTiles == 0xFFFF ? 96 : widthTiles * 8;
        int height = heightTiles == 0xFFFF ? 96 : heightTiles * 8;
        int size = BitConverter.ToInt32(raw, 40);
        if (width != 96 || height != 96 || size != 0x1200 || 48 + size > raw.Length)
            throw new InvalidDataException($"Expected a 96x96 4bpp sprite, got {width}x{height} ({size} bytes).");

        byte[] chars = raw.AsSpan(48, size).ToArray();
        byte[] tiled = ReorderSingleCell(chars, reverse: false);
        var bmp = new SKBitmap(96, 96, SKColorType.Bgra8888, SKAlphaType.Premul);
        int cursor = 0;
        for (int ty = 0; ty < 96; ty += 8)
        for (int tx = 0; tx < 96; tx += 8)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x += 2)
        {
            byte b = tiled[cursor++];
            SetPixel(bmp, tx + x, ty + y, b & 15, palette);
            SetPixel(bmp, tx + x + 1, ty + y, b >> 4, palette);
        }
        return bmp;
    }

    public static byte[] PatchSingleCellSprite(byte[] original, SKBitmap bitmap, Color[] palette)
    {
        if (bitmap.Width != 96 || bitmap.Height != 96)
            throw new InvalidOperationException("Pokémon single-cell sprites must be exactly 96x96 pixels.");
        byte[] raw = Decompress(original).ToArray();
        if (raw.Length < 48 || !raw.AsSpan(0, 4).SequenceEqual("RGCN"u8) ||
            !raw.AsSpan(16, 4).SequenceEqual("RAHC"u8))
            throw new InvalidDataException("Sprite is not a supported Nitro RGCN/RAHC resource.");
        int size = BitConverter.ToInt32(raw, 40);
        if (size != 0x1200 || 48 + size > raw.Length)
            throw new InvalidDataException("Sprite character data is not 96x96 4bpp.");

        byte[] tiled = new byte[0x1200];
        int cursor = 0;
        for (int ty = 0; ty < 96; ty += 8)
        for (int tx = 0; tx < 96; tx += 8)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x += 2)
        {
            int p0 = NearestPaletteIndex(bitmap.GetPixel(tx + x, ty + y), palette);
            int p1 = NearestPaletteIndex(bitmap.GetPixel(tx + x + 1, ty + y), palette);
            tiled[cursor++] = (byte)(p0 | (p1 << 4));
        }
        byte[] stored = ReorderSingleCell(tiled, reverse: true);
        stored.CopyTo(raw, 48);
        return RecompressLike(original, raw);
    }

    private static int NearestPaletteIndex(SKColor color, Color[] palette)
    {
        if (color.Alpha < 128) return 0;
        long best = long.MaxValue;
        int bestIndex = 1;
        for (int i = 1; i < palette.Length; i++)
        {
            long dr = color.Red - palette[i].R, dg = color.Green - palette[i].G, db = color.Blue - palette[i].B;
            long distance = dr * dr + dg * dg + db * db;
            if (distance < best) { best = distance; bestIndex = i; }
        }
        return bestIndex;
    }

    private static void SetPixel(SKBitmap bmp, int x, int y, int index, Color[] palette)
    {
        if (index == 0) bmp.SetPixel(x, y, SKColors.Transparent);
        else
        {
            Color color = palette[Math.Min(index, palette.Length - 1)];
            bmp.SetPixel(x, y, new SKColor(color.R, color.G, color.B, color.A));
        }
    }

    private static byte[] ReorderSingleCell(byte[] chars, bool reverse)
    {
        const int width = 96;
        var tiles = new List<byte[]>();
        for (int i = 0; i < chars.Length; i += 32) tiles.Add(chars.AsSpan(i, 32).ToArray());
        var order = new List<int>();
        foreach (var obj in SingleCell96)
            for (int y = obj.Y; y < obj.Y + obj.H; y += 8)
            for (int x = obj.X; x < obj.X + obj.W; x += 8)
                order.Add((y / 8) * (width / 8) + x / 8);
        if (order.Count != tiles.Count) throw new InvalidDataException("Invalid single-cell sprite tile layout.");
        var result = new byte[chars.Length];
        for (int stored = 0; stored < order.Count; stored++)
        {
            int tiled = order[stored];
            byte[] source = reverse ? tiles[tiled] : tiles[stored];
            int target = reverse ? stored : tiled;
            source.CopyTo(result, target * 32);
        }
        return result;
    }

    private static byte[] RecompressLike(byte[] original, byte[] raw)
    {
        if (original.Length == 0 || (original[0] != 0x10 && original[0] != 0x11))
            return raw;
        return original[0] == 0x11 ? CompressLz11(raw) : CompressLz10(raw);
    }

    private static byte[] CompressLz10(byte[] raw)
    {
        // Reuse the LZ11 encoder: the output format differs, so use literal-only LZ10
        // when a legacy resource is encountered. This may fail the same-size guard.
        using var ms = new MemoryStream();
        ms.WriteByte(0x10);
        ms.WriteByte((byte)raw.Length);
        ms.WriteByte((byte)(raw.Length >> 8));
        ms.WriteByte((byte)(raw.Length >> 16));
        for (int i = 0; i < raw.Length; i += 8)
        {
            ms.WriteByte(0);
            ms.Write(raw, i, Math.Min(8, raw.Length - i));
        }
        return ms.ToArray();
    }

    private static int To5(int value) => (int)Math.Round(value * 31.0 / 255.0);
}
