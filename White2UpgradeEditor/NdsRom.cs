using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;

namespace White2UpgradeEditor;

public sealed record RomFile(int Id, string Path, int Start, int End)
{
    public int Length => End - Start;
}

public sealed class NdsRom
{
    private byte[] _data = [];
    private byte[] _sourceSha256 = [];
    public string FilePath { get; private set; } = "";
    public string Title => _data.Length >= 12 ? Encoding.ASCII.GetString(_data, 0, 12).TrimEnd('\0', ' ') : "";
    public IReadOnlyList<RomFile> Files { get; private set; } = [];

    public void Open(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 0x200)
            throw new InvalidDataException("This file is too small to be a Nintendo DS ROM.");

        uint fntOffset = U32(data, 0x40), fntSize = U32(data, 0x44);
        uint fatOffset = U32(data, 0x48), fatSize = U32(data, 0x4C);
        if (!RangeOk(data, fntOffset, fntSize) || !RangeOk(data, fatOffset, fatSize) ||
            fatSize < 8 || fatSize % 8 != 0)
            throw new InvalidDataException("Invalid NitroFS/FAT offsets in the ROM header.");

        int count = checked((int)(fatSize / 8));
        var ranges = new (int start, int end)[count];
        for (int i = 0; i < count; i++)
        {
            int p = checked((int)fatOffset + i * 8);
            uint start = U32(data, p), end = U32(data, p + 4);
            if (start > end || end > data.Length)
                throw new InvalidDataException($"Invalid FAT range for file ID {i}.");
            ranges[i] = ((int)start, (int)end);
        }

        var names = ParseNames(data, checked((int)fntOffset), checked((int)fntSize), count);
        var list = new List<RomFile>(count);
        for (int i = 0; i < count; i++)
            list.Add(new RomFile(i, names.TryGetValue(i, out var name) ? name : $"File_{i:D4}.bin",
                ranges[i].start, ranges[i].end));

        _data = data;
        _sourceSha256 = SHA256.HashData(data);
        FilePath = Path.GetFullPath(path);
        Files = list;
    }

    public byte[] ReadFile(RomFile file) => _data.AsSpan(file.Start, file.Length).ToArray();

    public RomFile FindFile(string path)
    {
        string clean = path.TrimStart('/').Replace('\\', '/');
        return Files.FirstOrDefault(f =>
                   string.Equals(f.Path.TrimStart('/'), clean, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(f.Path.TrimStart('/'), "data/" + clean, StringComparison.OrdinalIgnoreCase))
               ?? throw new FileNotFoundException($"ROM NitroFS file '{clean}' was not found.");
    }

    public void ReplaceSameSize(RomFile file, byte[] replacement)
    {
        if (replacement.Length != file.Length)
            throw new InvalidOperationException(
                $"The rebuilt archive is {replacement.Length:N0} bytes, but its ROM entry is {file.Length:N0} bytes. " +
                "This ROM-mode build only accepts same-size archive replacements. Use an extracted-dump patch for size-changing edits.");
        replacement.CopyTo(_data, file.Start);
    }

    public void SaveAs(string path) => File.WriteAllBytes(path, _data);

    /// <summary>
    /// Reject xdelta export if the original .nds has changed on disk since it was opened.
    /// The editor's working image may have changed in memory; that is expected.
    /// </summary>
    public bool OriginalFileMatchesDisk()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath)) return false;
        using var source = File.OpenRead(FilePath);
        return source.Length == _data.LongLength
               && SHA256.HashData(source).AsSpan().SequenceEqual(_sourceSha256);
    }

    private static Dictionary<int, string> ParseNames(byte[] data, int fnt, int fntSize, int fileCount)
    {
        var result = new Dictionary<int, string>();
        if (fntSize < 8) return result;
        int dirCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fnt + 6, 2));
        if (dirCount < 1 || dirCount > fntSize / 8) return result;
        var dirs = new (int sub, int firstFile)[dirCount];
        for (int i = 0; i < dirCount; i++)
        {
            int p = fnt + i * 8;
            dirs[i] = (checked((int)U32(data, p)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 4, 2)));
        }

        var active = new HashSet<int>();
        void Walk(int index, string prefix, int depth)
        {
            if (depth > 32 || index < 0 || index >= dirs.Length || !active.Add(index)) return;
            int cursor = fnt + dirs[index].sub;
            int fileId = dirs[index].firstFile;
            int limit = fnt + fntSize;
            while (cursor < limit)
            {
                byte marker = data[cursor++];
                if (marker == 0) break;
                bool directory = (marker & 0x80) != 0;
                int length = marker & 0x7F;
                if (length == 0 || cursor + length > limit) break;
                string entry = Encoding.ASCII.GetString(data, cursor, length);
                cursor += length;
                if (directory)
                {
                    if (cursor + 2 > limit) break;
                    int child = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor, 2)) - 0xF000;
                    cursor += 2;
                    Walk(child, prefix + entry + "/", depth + 1);
                }
                else
                {
                    if (fileId < fileCount) result[fileId] = prefix + entry;
                    fileId++;
                }
            }
            active.Remove(index);
        }
        Walk(0, "", 0);
        return result;
    }

    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
    private static bool RangeOk(byte[] b, uint p, uint size) => p <= b.Length && size <= b.Length - p;
}
