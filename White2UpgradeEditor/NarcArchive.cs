using System.Buffers.Binary;
using System.Text;

namespace White2UpgradeEditor;

public sealed class NarcArchive
{
    private readonly byte[] _fntBlock;
    private readonly List<byte[]> _members;

    public IReadOnlyList<byte[]> Members => _members;
    public int Count => _members.Count;
    public bool IsDirty { get; private set; }

    private NarcArchive(byte[] fntBlock, List<byte[]> members)
    {
        _fntBlock = fntBlock;
        _members = members;
    }

    public byte[] GetMember(int index)
    {
        if ((uint)index >= (uint)_members.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"NARC member {index} does not exist.");
        return _members[index];
    }

    public void MarkSaved() => IsDirty = false;

    public void SetMember(int index, byte[] data)
    {
        if ((uint)index >= (uint)_members.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"NARC member {index} does not exist.");
        _members[index] = data.ToArray();
        IsDirty = true;
    }

    public static NarcArchive Read(byte[] data)
    {
        if (data.Length < 0x20 || Encoding.ASCII.GetString(data, 0, 4) != "NARC")
            throw new InvalidDataException("The selected file is not a valid NARC archive.");

        int blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14, 2));
        int cursor = 16;
        byte[]? fat = null;
        byte[]? fntBlock = null;
        byte[]? gmif = null;

        for (int i = 0; i < blockCount; i++)
        {
            if (cursor + 8 > data.Length)
                throw new InvalidDataException("NARC block header is truncated.");

            string magic = Encoding.ASCII.GetString(data, cursor, 4);
            uint blockSizeU = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 4, 4));
            if (blockSizeU < 8 || blockSizeU > data.Length - cursor)
                throw new InvalidDataException($"Invalid {magic} block size.");

            int blockSize = checked((int)blockSizeU);
            if (magic == "BTAF")
                fat = data.AsSpan(cursor + 8, blockSize - 8).ToArray();
            else if (magic == "BTNF")
                fntBlock = data.AsSpan(cursor, blockSize).ToArray();
            else if (magic == "GMIF")
                gmif = data.AsSpan(cursor + 8, blockSize - 8).ToArray();

            cursor += blockSize;
        }

        if (fat is null || fntBlock is null || gmif is null || fat.Length < 4)
            throw new InvalidDataException("NARC is missing its FAT, FNT, or image-data block.");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(fat.AsSpan(0, 2));
        if (fat.Length < 4 + count * 8)
            throw new InvalidDataException("NARC FAT is truncated.");

        var members = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            uint startU = BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan(4 + i * 8, 4));
            uint endU = BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan(8 + i * 8, 4));
            if (startU > endU || endU > gmif.Length)
                throw new InvalidDataException($"Invalid NARC FAT range for member {i}.");
            int start = checked((int)startU);
            int length = checked((int)(endU - startU));
            members.Add(gmif.AsSpan(start, length).ToArray());
        }

        return new NarcArchive(fntBlock, members);
    }

    public byte[] Build()
    {
        using var fatData = new MemoryStream();
        using var imageData = new MemoryStream();
        using (var writer = new BinaryWriter(fatData, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)_members.Count);
            writer.Write((ushort)0);
            foreach (var member in _members)
            {
                while ((imageData.Position & 3) != 0)
                    imageData.WriteByte(0);
                uint start = checked((uint)imageData.Position);
                imageData.Write(member);
                uint end = checked((uint)imageData.Position);
                writer.Write(start);
                writer.Write(end);
            }
        }
        while ((imageData.Position & 3) != 0)
            imageData.WriteByte(0);

        byte[] fatBytes = fatData.ToArray();
        byte[] imageBytes = imageData.ToArray();
        byte[] fatBlock = MakeBlock("BTAF", fatBytes);
        byte[] gmifBlock = MakeBlock("GMIF", imageBytes);

        using var output = new MemoryStream();
        using var bw = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes("NARC"));
        bw.Write((ushort)0xFEFF);
        bw.Write((ushort)0x0100);
        bw.Write((uint)(16 + fatBlock.Length + _fntBlock.Length + gmifBlock.Length));
        bw.Write((ushort)16);
        bw.Write((ushort)3);
        bw.Write(fatBlock);
        bw.Write(_fntBlock);
        bw.Write(gmifBlock);
        return output.ToArray();
    }

    private static byte[] MakeBlock(string magic, byte[] payload)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes(magic));
        bw.Write((uint)(payload.Length + 8));
        bw.Write(payload);
        return ms.ToArray();
    }
}
