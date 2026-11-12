using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace White2UpgradeEditor;

/// <summary>Strict, read-only validation and opt-in repair for the verified White 2 Upgrade battle overlay.</summary>
internal static class TypeChartEngine
{
    internal static readonly string[] Types =
    [
        "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost",
        "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy"
    ];

    // Modern 18x18 attack/defend matrix, 0=immune, 2=half, 4=neutral, 8=double.
    private static readonly string[] ReferenceRows =
    [
        "444442402444444444", "842248208444428482", "484442842448244444",
        "444222420448444448", "440848248842844444", "428424842844448444",
        "422244422248484482", "044444484444484424", "444448442224248448",
        "444442848228448244", "444488444822444244", "442288242282444244",
        "448404444482244244", "484844442444424404", "448484442228442844",
        "444444442444444840", "424444484444484422", "484244442244444884"
    ];
    private static readonly byte[] NewChart = ReferenceRows.SelectMany(row => row.Select(c => checked((byte)(c - '0')))).ToArray();
    private const int LegacyBase = 0x02199900;
    private const int ExpandedBase = 0x02199780;
    private const int LegacySize = 0x41740;
    private const int Prefix = 0x180;
    private const int ExpandedSize = LegacySize + Prefix;
    private const int LegacyTableOffset = 0x3DC40;
    private const string LegacySha256 = "e94a3f0582384f1d6fc2dc5296de5ba410f1d1ab7461f0c2d43c5dab0de5064a";
    private const string StarterContextSha256 = "598121c741dc5461a0b5f6bcbe19cf4869c4f5110e8eabecc5759f93e4ee2c49";

    // Offsets are in the expanded overlay; preexisting bytes begin after the 0x180 prefix.
    private static readonly (int Offset, string Before, string After)[] Changes =
    [
        (0x0C312,"112a08d0","122a08da"), (0x11226,"112802d1","122802db"),
        (0x21884,"11","12"), (0x21896,"11","12"),
        (0x218A2,"112906d1112801d1","122906db122801db"),
        (0x218B4,"112800d1","122800db"), (0x218DA,"112c0ed0","122c0eda"),
        (0x23A20,"112801d0112901d1","122801da122901db"),
        (0x23A2C,"02018218","12224243"), (0x23A6C,"40751d02","80971902"),
        (0x23B98,"11","12"), (0x23BA4,"40751d02","80971902"),
        (0x260E2,"112e1ed0","122e1eda"), (0x306F0,"112c3cd0","122c3cda"),
        (0x306F6,"11","12")
    ];

    internal enum Health { LegacyNeedsRepair, ExpandedHealthy, Unknown }
    internal sealed record Inspection(Health State, string Detail, int Dimensions, byte[]? Matrix,
        bool White2UpgradeVerified, string SourcePath)
    {
        public bool CanRepair => White2UpgradeVerified && State == Health.LegacyNeedsRepair;
    }
    private sealed record Image(byte[] Overlay167, byte[] Overlay316, byte[] PersonalNarc,
        byte[] OverlayTable, int TableOffset, int OverlayFileId, int FatOffset, int RomTableOffset,
        byte[] RawRom);

    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
    private static void W32(byte[] b, int p, uint n) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p, 4), n);
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static int FindRecord(byte[] table, int overlayId)
    {
        if (table.Length % 32 != 0) throw new InvalidDataException("Malformed ARM9 overlay table.");
        for (int i = 0; i + 32 <= table.Length; i += 32)
            if (U32(table, i) == overlayId) return i;
        throw new InvalidDataException($"Overlay {overlayId} is missing.");
    }

    private static Image Load(string source, bool extracted)
    {
        if (extracted)
        {
            var root = Path.GetFullPath(source);
            byte[] file(string rel) => File.ReadAllBytes(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            var overlay167 = file("overlay/ov_167.bin");
            var overlay316 = file("overlay/ov_316.bin");
            string personal = File.Exists(Path.Combine(root, "data/a/0/1/6")) ? "data/a/0/1/6" : "a/0/1/6";
            var personalNarc = file(personal);
            var ovt = file("y9.bin");
            int record = FindRecord(ovt, 167);
            return new Image(overlay167, overlay316, personalNarc, ovt, record, -1, -1, -1, []);
        }
        var rom = new NdsRom();
        rom.Open(source);
        byte[] raw = File.ReadAllBytes(source);
        int offset = checked((int)U32(raw, 0x50));
        int length = checked((int)U32(raw, 0x54));
        if (length <= 0 || offset < 0 || (long)offset + length > raw.Length)
            throw new InvalidDataException("The ROM's ARM9 overlay table is invalid.");
        var table = raw.AsSpan(offset, length).ToArray();
        int record167 = FindRecord(table, 167);
        int record316 = FindRecord(table, 316);
        int fid167 = checked((int)U32(table, record167 + 24));
        int fid316 = checked((int)U32(table, record316 + 24));
        int fatOffset = checked((int)U32(raw, 0x48));
        byte[] overlay(int fid)
        {
            if ((long)fatOffset + (fid + 1L) * 8 > raw.Length)
                throw new InvalidDataException("Overlay FAT entry is outside the ROM.");
            int a = checked((int)U32(raw, fatOffset + fid * 8));
            int b = checked((int)U32(raw, fatOffset + fid * 8 + 4));
            if (a < 0 || b < a || b > raw.Length) throw new InvalidDataException("Overlay FAT range is invalid.");
            return raw.AsSpan(a, b - a).ToArray();
        }
        var personalFile = rom.FindFile("a/0/1/6");
        return new Image(overlay(fid167), overlay(fid316), rom.ReadFile(personalFile),
            table, record167, fid167, fatOffset, offset, raw);
    }

    private static bool IsVerifiedUpgrade(Image image)
    {
        // Reject ordinary White 2 ROMs even when their battle overlay matches vanilla:
        // the verified starter overlay layout AND Fairy-tagged personal records are required.
        var ov = image.Overlay316;
        const int speciesAt = 11284;
        if (ov.Length < speciesAt + 22) return false;
        var check = ov.AsSpan(speciesAt - 16, 16).ToArray().Concat(ov.AsSpan(speciesAt + 6, 16).ToArray()).ToArray();
        if (!string.Equals(Sha(check), StarterContextSha256, StringComparison.Ordinal)) return false;
        try
        {
            var personal = NarcArchive.Read(image.PersonalNarc);
            if (personal.Count <= 721) return false;
            int fairyCount = 0;
            foreach (int species in new[] { 35, 39, 173, 175, 183, 280, 282, 468, 669, 700 })
            {
                byte[] p = personal.GetMember(species);
                if (p.Length == 76 && (p[6] == 17 || p[7] == 17)) fairyCount++;
            }
            return fairyCount >= 2;
        }
        catch { return false; }
    }

    private static bool MatchesChanges(byte[] overlay, bool expanded)
    {
        foreach (var (at, before, after) in Changes)
        {
            byte[] expected = Convert.FromHexString(expanded ? after : before);
            int pos = expanded ? at : at - Prefix;
            if (pos < 0 || pos + expected.Length > overlay.Length
                || !overlay.AsSpan(pos, expected.Length).SequenceEqual(expected)) return false;
        }
        return true;
    }

    private static bool MatchesExpanded(byte[] ov)
    {
        if (ov.Length != ExpandedSize || !ov.AsSpan(0, 324).SequenceEqual(NewChart)
            || ov.AsSpan(324, Prefix - 324).ToArray().Any(b => b != 0xFF)
            || !MatchesChanges(ov, true)) return false;
        return U32(ov, 0x23A6C) == ExpandedBase && U32(ov, 0x23BA4) == ExpandedBase;
    }

    internal static Inspection Inspect(string source, bool extracted = false)
    {
        var img = Load(source, extracted);
        bool verified = IsVerifiedUpgrade(img);
        if (!verified)
            return new Inspection(Health.Unknown,
                "This ROM has not passed the verified White 2 Upgrade signatures (starter overlay and Fairy personal data). Only White 2 Upgrade is supported.",
                0, null, false, source);

        var row = img.OverlayTable;
        int i = img.TableOffset;
        uint baseAddress = U32(row, i + 4);
        uint size = U32(row, i + 8);
        uint flags = U32(row, i + 28) >> 24;
        byte[] ov = img.Overlay167;
        if (baseAddress == LegacyBase && size == LegacySize && flags == 0 &&
            ov.Length == LegacySize && Sha(ov) == LegacySha256 && MatchesChanges(ov, false))
        {
            var legacy = ov.AsSpan(LegacyTableOffset, 289).ToArray();
            return new Inspection(Health.LegacyNeedsRepair,
                "Verified 17×17 table detected. Fairy is not represented in the chart; Normal → Fairy can read the next row's 2× value when indexed as 18 columns.",
                17, legacy, true, source);
        }
        if (baseAddress == ExpandedBase && size == ExpandedSize && flags == 0 && MatchesExpanded(ov))
            return new Inspection(Health.ExpandedHealthy,
                "Verified 18×18 chart and battle lookup patches are present. Normal → Fairy = 1×.",
                18, ov.AsSpan(0, 324).ToArray(), true, source);
        return new Inspection(Health.Unknown,
            "This is White 2 Upgrade, but overlay 167 has an unrecognized or compressed layout. Automatic repair is disabled to avoid damaging another revision.",
            0, null, true, source);
    }

    private static byte[] Expand(byte[] old)
    {
        if (old.Length != LegacySize || Sha(old) != LegacySha256 || !MatchesChanges(old, false))
            throw new InvalidDataException("Overlay 167 failed the exact known-layout verification.");
        var next = new byte[ExpandedSize];
        NewChart.CopyTo(next, 0);
        Array.Fill(next, (byte)0xFF, 324, Prefix - 324);
        old.CopyTo(next, Prefix);
        foreach (var (at, _, after) in Changes)
        {
            byte[] patched = Convert.FromHexString(after);
            patched.CopyTo(next, at);
        }
        if (!MatchesExpanded(next)) throw new InvalidDataException("Expanded overlay verification failed.");
        return next;
    }

    private static byte[] ExpandedOverlayMetadata(Image image)
    {
        var table = image.OverlayTable.ToArray();
        int r = image.TableOffset;
        if (U32(table, r + 4) != LegacyBase || U32(table, r + 8) != LegacySize
            || U32(table, r + 28) >> 24 != 0)
            throw new InvalidDataException("Overlay 167 loader metadata differs from verified White 2 Upgrade.");
        W32(table, r + 4, ExpandedBase);
        W32(table, r + 8, ExpandedSize);
        W32(table, r + 28, ExpandedSize);
        return table;
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte v in data)
        {
            crc ^= v;
            for (int i = 0; i < 8; i++)
                crc = (ushort)((crc >> 1) ^ ((crc & 1) != 0 ? 0xA001 : 0));
        }
        return crc;
    }

    internal static void RepairToNewCopy(string source, string output, bool extracted = false)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Never overwrite the source ROM/dump.");
        if (File.Exists(output) || Directory.Exists(output))
            throw new IOException("Output already exists. Choose a new destination.");
        var before = Inspect(source, extracted);
        if (!before.CanRepair) throw new InvalidOperationException("Only an exact verified 17×17 White 2 Upgrade layout can be repaired.");
        var img = Load(source, extracted);
        byte[] next = Expand(img.Overlay167);
        byte[] updatedTable = ExpandedOverlayMetadata(img);
        if (extracted)
        {
            string root = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.Combine(root, "overlay"));
            try
            {
                File.WriteAllBytes(Path.Combine(root, "overlay", "ov_167.bin"), next);
                File.WriteAllBytes(Path.Combine(root, "y9.bin"), updatedTable);
                File.WriteAllText(Path.Combine(root, "PATCH-NOTES.txt"),
                    "White 2 Upgrade 18×18 type chart repair. Reinsert BOTH overlay/ov_167.bin and y9.bin into a copy of the matching extracted ROM.");
            }
            catch { Directory.Delete(root, true); throw; }
            return;
        }

        string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,
            ".w2u-typechart-" + Guid.NewGuid().ToString("N") + ".nds");
        try
        {
            File.Copy(source, temp, overwrite: false);
            using (var file = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long start = (file.Length + 511) & ~511L;
                file.SetLength(start);
                file.Position = start;
                file.Write(next);
                long end = start + next.Length;
                if (end > uint.MaxValue) throw new InvalidDataException("Patched ROM exceeds supported FAT address space.");
                var fat = new byte[8];
                W32(fat, 0, checked((uint)start));
                W32(fat, 4, checked((uint)end));
                file.Position = img.FatOffset + img.OverlayFileId * 8L;
                file.Write(fat);
                file.Position = img.RomTableOffset;
                file.Write(updatedTable);
                byte[] header = img.RawRom.AsSpan(0, 0x200).ToArray();
                if (U32(header, 0x80) < end) W32(header, 0x80, checked((uint)end));
                byte exponent = header[0x14];
                while ((128L * 1024 << exponent) < end && exponent < 16) exponent++;
                header[0x14] = exponent;
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x15E, 2), Crc16(header.AsSpan(0, 0x15E)));
                file.Position = 0;
                file.Write(header);
                file.Flush(flushToDisk: true);
            }
            var after = Inspect(temp);
            if (after.State != Health.ExpandedHealthy)
                throw new InvalidDataException("Patched ROM did not pass strict read-back verification: " + after.Detail);
            File.Move(temp, output);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static string Multiplier(byte code) => code switch { 0 => "0×", 2 => "½×", 4 => "1×", 8 => "2×", _ => "?" };
}
