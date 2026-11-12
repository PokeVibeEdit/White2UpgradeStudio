using System.Buffers.Binary;
using System.Text;

namespace White2UpgradeEditor;

/// <summary>English B2/W2 trainer names (message member 382) and class names (383).
/// Both banks use Gen V encrypted strings; most trainer names additionally use
/// the 0xF100 packed, little-endian 9-bit character encoding.</summary>
public sealed class TrainerNames
{
    private readonly string[] _names;
    private readonly string[] _classes;
    private TrainerNames(string[] names, string[] classes) { _names = names; _classes = classes; }

    public string Name(int id) => id >= 0 && id < _names.Length && !string.IsNullOrWhiteSpace(_names[id])
        ? _names[id] : $"Trainer #{id:000}";
    public string Class(int id) => id >= 0 && id < _classes.Length && !string.IsNullOrWhiteSpace(_classes[id])
        ? _classes[id] : $"Class #{id}";
    public string Label(int trainerId, int classId) => $"#{trainerId:000}   {Class(classId)} {Name(trainerId)}";

    public static TrainerNames Load(RomWorkspace workspace, int trainerCount)
    {
        var archive = workspace.GetArchive("a/0/0/2");
        var names = DecodeBank(archive.GetMember(382));
        var classes = DecodeBank(archive.GetMember(383));
        if (names.Length != trainerCount || classes.Length < 200)
            throw new InvalidDataException($"Trainer text bank mismatch: {names.Length} names for {trainerCount} trainers; {classes.Length} classes.");
        // Check a recognizable, uncompressed entry and a compressed entry to catch wrong text-bank offsets.
        if (names.Length <= 161 || names[4] != "Bianca" || names[161] != "Rival")
            throw new InvalidDataException("Trainer name bank failed known White 2 Upgrade checks.");
        return new TrainerNames(names, classes);
    }

    private static string[] DecodeBank(byte[] bank)
    {
        if (bank.Length < 24 || Read16(bank, 0) != 1)
            throw new InvalidDataException("Unexpected Gen V message bank header.");
        int count = Read16(bank, 2);
        if (count == 0 || 20L + count * 8L > bank.Length)
            throw new InvalidDataException("Invalid Gen V message bank table.");
        var result = new string[count];
        for (int id = 0; id < count; id++)
        {
            int entry = 20 + id * 8;
            uint offset = Read32(bank, entry), length = Read32(bank, entry + 4);
            long begin = 16L + offset;
            if (length == 0 || length > 512 || begin + length * 2L > bank.Length)
                throw new InvalidDataException($"Invalid trainer text entry {id}.");
            var words = new ushort[(int)length];
            for (int i = 0; i < words.Length; i++) words[i] = Read16(bank, checked((int)(begin + i * 2L)));
            ushort key = (ushort)(words[^1] ^ 0xFFFF);
            for (int i = words.Length - 1; i >= 0; i--)
            {
                words[i] ^= key;
                key = (ushort)((key >> 3) | (key << 13));
            }
            if (words[^1] != 0xFFFF) throw new InvalidDataException($"Invalid trainer text terminator {id}.");
            result[id] = words[0] == 0xF100 ? DecodePacked(words) : DecodePlain(words);
        }
        return result;
    }

    private static string DecodePacked(ushort[] words)
    {
        // Bitstream starts after 0xF100 and ends before the 0xFFFF string terminator.
        // 9-bit codes are packed least-significant-bit first; 0x1FF ends the string.
        var sb = new StringBuilder();
        uint bits = 0; int bitCount = 0;
        for (int i = 1; i < words.Length - 1; i++)
        {
            bits |= (uint)words[i] << bitCount;
            bitCount += 16;
            while (bitCount >= 9)
            {
                int code = (int)(bits & 0x1FF);
                bits >>= 9; bitCount -= 9;
                if (code == 0x1FF) return sb.ToString();
                sb.Append(code >= 32 && code < 0xFFFE ? (char)code : '?');
            }
        }
        return sb.ToString();
    }

    private static string DecodePlain(ushort[] words)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < words.Length - 1; i++)
        {
            ushort code = words[i];
            if (code == 0xFFFF) break;
            if (code == 0xFFFE) { sb.Append(' '); continue; }
            sb.Append(code >= 32 && code < 0xF000 ? (char)code : '?');
        }
        return sb.ToString();
    }

    private static ushort Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
