using System.Buffers.Binary;

namespace White2UpgradeEditor;

// White 2 zone entity records. Keep all unedited bytes, especially init scripts.
public sealed class ZoneEvents
{
    public static readonly string[] Kinds = ["Signs / proxies", "NPCs", "Warps", "Triggers"];
    public static readonly int[] Sizes = [20, 36, 20, 22];
    public byte[] Data { get; }
    public int[] Counts { get; } = new int[4];
    public int[][] Offsets { get; } = new int[4][];
    public int InitOffset { get; }
    public ZoneEvents(byte[] source)
    {
        Data = source.ToArray();
        if (Data.Length < 10) throw new InvalidDataException("Not a zone entity file.");
        int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(Data));
        int position = 8;
        for (int k = 0; k < 4; k++)
        {
            Counts[k] = Data[4 + k];
            Offsets[k] = new int[Counts[k]];
            for (int i = 0; i < Counts[k]; i++)
            {
                if (position > Data.Length - Sizes[k]) throw new InvalidDataException("Truncated zone entity records.");
                Offsets[k][i] = position;
                position += Sizes[k];
            }
        }
        if (position != offset + 4 || position + 2 > Data.Length)
            throw new InvalidDataException("Zone entity offsets are inconsistent.");
        InitOffset = position;
    }
    public byte[] Get(int kind, int index) => Data.AsSpan(Offsets[kind][index], Sizes[kind]).ToArray();
    public void Replace(int kind, int index, byte[] bytes)
    {
        if (bytes.Length != Sizes[kind]) throw new ArgumentException("Record size changed.");
        bytes.CopyTo(Data, Offsets[kind][index]);
    }
    public static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2));
    public static short I16(byte[] d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o, 2));
    public static int I32(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o, 4));
    public static void SetU16(byte[] d, int o, ushort n) => BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(o, 2), n);
    public static void SetI16(byte[] d, int o, short n) => BinaryPrimitives.WriteInt16LittleEndian(d.AsSpan(o, 2), n);
    public static void SetI32(byte[] d, int o, int n) => BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(o, 4), n);
    public static int RailOffset(int kind) => kind switch { 0 => 6, 1 => 24, 2 => 6, 3 => 8, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    public static bool IsRail(int kind, byte[] record) => U16(record, RailOffset(kind)) != 0;
    public static IReadOnlyList<(string Name, int Offset, string Type)> Fields(int kind, bool rail)
    {
        var f = new List<(string, int, string)>();
        void U(string name, int o) => f.Add((name, o, "u16"));
        void B(string name, int o) => f.Add((name, o, "u8"));
        void S(string name, int o) => f.Add((name, o, "i16"));
        void I(string name, int o) => f.Add((name, o, "i32"));
        if (kind == 0)
        {
            U("Script ID", 0); U("Condition", 2); U("Direction", 4);
            if (!rail) { I("X", 8); I("Y", 12); I("Z", 16); }
            else { U("Rail component", 8); U("Rail front", 10); S("Rail side", 12); }
        }
        else if (kind == 1)
        {
            string[] names = ["NPC ID", "Model ID", "Movement ID", "Event type", "Spawn flag", "Script ID", "Direction", "Parameter 1", "Parameter 2", "Parameter 3", "Area width", "Area height"];
            for (int i = 0; i < names.Length; i++) U(names[i], i * 2);
            if (!rail) { U("X", 28); U("Z", 30); I("Y", 32); }
            else { U("Rail component", 28); U("Rail front", 30); S("Rail side", 32); }
        }
        else if (kind == 2)
        {
            U("Destination zone", 0); U("Destination warp", 2); B("Unknown byte", 4); B("Transition", 5);
            if (!rail) { string[] names = ["X", "Y", "Z", "Width", "Height", "Unknown"]; for (int i=0;i<6;i++) U(names[i], 8+i*2); }
            else { U("Rail component", 8); U("Rail front", 10); S("Rail side", 12); U("Front span", 14); U("Side span", 16); U("Parameter", 18); }
        }
        else if (kind == 3)
        {
            U("Script ID", 0); U("Work value", 2); U("Variable ID", 4); U("Trigger type", 6);
            if (!rail) { U("X", 10); U("Z", 12); U("Width", 14); U("Height", 16); S("Y", 18); }
            else { U("Rail component", 10); U("Rail front", 12); S("Rail side", 14); U("Front span", 16); U("Side span", 18); }
            U("Unknown field", 20);
        }
        return f;
    }
    public static string ReadField(byte[] data, int offset, string type) => type switch
    {
        "u8" => data[offset].ToString(), "u16" => U16(data,offset).ToString(), "i16" => I16(data,offset).ToString(), "i32" => I32(data,offset).ToString(), _ => ""
    };
    public static void WriteField(byte[] data, int offset, string type, string input)
    {
        if (type == "u8") data[offset] = byte.Parse(input);
        else if (type == "u16") SetU16(data,offset,ushort.Parse(input));
        else if (type == "i16") SetI16(data,offset,short.Parse(input));
        else if (type == "i32") SetI32(data,offset,int.Parse(input));
    }
}
