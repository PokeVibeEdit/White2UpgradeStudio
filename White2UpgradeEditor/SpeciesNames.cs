using System.Buffers.Binary;

namespace White2UpgradeEditor;

/// <summary>
/// Reads the English Gen V system-message species names from a/0/0/2, member 90.
/// The 16-byte message header precedes an eight-byte-per-string offset/length table.
/// String offsets are relative to the first text section (byte 16). The last encrypted
/// word is the terminator (0xFFFF XOR the final key); keys rotate right three bits
/// when traversing encrypted characters backwards.
/// </summary>
public sealed class SpeciesNames
{
    private readonly string[] _names;
    private SpeciesNames(string[] names) => _names = names;

    public string Get(int speciesId) => speciesId >= 1 && speciesId < _names.Length && !string.IsNullOrWhiteSpace(_names[speciesId])
        ? _names[speciesId] : $"Species #{speciesId:000}";

    public string Label(int speciesId) => $"#{speciesId:000}   {Get(speciesId)}";

    public static SpeciesNames Load(RomWorkspace workspace)
    {
        byte[] bank = workspace.GetArchive("a/0/0/2").GetMember(90);
        if (bank.Length < 32 || Read16(bank, 0) != 1)
            throw new InvalidDataException("Species-name message bank has an unexpected header.");
        int count = Read16(bank, 2);
        if (count < 722 || 20L + count * 8L > bank.Length)
            throw new InvalidDataException($"Expected at least 722 species-name messages, found {count}.");
        var names = new string[722];
        for (int id = 1; id <= 721; id++)
        {
            int entry = 20 + id * 8;
            uint offset = Read32(bank, entry);
            uint length = Read32(bank, entry + 4);
            long begin = 16L + offset;
            if (length < 2 || length > 64 || begin < 0 || begin + length * 2L > bank.Length)
                throw new InvalidDataException($"Invalid encrypted species-name entry #{id}.");
            ushort[] words = new ushort[checked((int)length)];
            for (int i = 0; i < words.Length; i++) words[i] = Read16(bank, checked((int)(begin + i * 2L)));
            ushort key = (ushort)(words[^1] ^ 0xFFFF);
            var chars = new char[words.Length - 1];
            for (int i = words.Length - 1; i >= 0; i--)
            {
                ushort decoded = (ushort)(words[i] ^ key);
                if (i == words.Length - 1 && decoded != 0xFFFF)
                    throw new InvalidDataException($"Invalid species-name terminator #{id}.");
                if (i < chars.Length)
                {
                    if (decoded < 0x20 || decoded >= 0xFFFE)
                        throw new InvalidDataException($"Unsupported character in species-name #{id}.");
                    chars[i] = (char)decoded;
                }
                key = (ushort)((key >> 3) | (key << 13));
            }
            names[id] = new string(chars);
        }
        if (names[1] != "Bulbasaur" || names[25] != "Pikachu" || names[495] != "Snivy" || names[498] != "Tepig" || names[501] != "Oshawott" || names[721] != "Volcanion")
            throw new InvalidDataException("Species-name bank failed the verified White 2 Upgrade ID checks.");
        return new SpeciesNames(names);
    }

    private static ushort Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
