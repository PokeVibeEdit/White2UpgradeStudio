using System.Buffers.Binary;

namespace White2UpgradeEditor;

/// <summary>
/// Read-only, conservative Nintendo DS GX display-list geometry preview.
/// Gen V map archive a/0/0/8 wraps BMD0 models in WB/GC containers.
/// This decoder handles vertex/primitive commands but not SBC transforms,
/// skeletal skinning, materials, or textures. Never writes model data.
/// </summary>
public static class NsbmdMapModel
{
    public readonly record struct Vertex(double X, double Y, double Z);
    public readonly record struct Triangle(Vertex A, Vertex B, Vertex C);
    public sealed record Result(IReadOnlyList<Triangle> Triangles, int DisplayLists, string Note);

    private static readonly Dictionary<int, int> ParameterWords = new()
    {
        [0x00]=0,[0x10]=1,[0x11]=0,[0x12]=1,[0x13]=1,[0x14]=1,[0x15]=0,
        [0x16]=16,[0x17]=12,[0x18]=16,[0x19]=12,[0x1A]=9,[0x1B]=3,[0x1C]=3,
        [0x20]=1,[0x21]=1,[0x22]=1,[0x23]=2,[0x24]=1,[0x25]=1,[0x26]=1,
        [0x27]=1,[0x28]=1,[0x29]=1,[0x2A]=1,[0x2B]=1,[0x30]=1,[0x31]=1,
        [0x32]=1,[0x33]=1,[0x34]=32,[0x40]=1,[0x41]=0,[0x50]=1,[0x60]=1,
        [0x70]=3,[0x71]=2,[0x72]=1
    };
    private static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o,2));
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o,4));
    private static double Fixed(short v) => v / 4096.0;

    public static Result Decode(byte[] container)
    {
        int bmd = Find(container, "BMD0", 0);
        if (bmd < 0 || bmd + 16 > container.Length)
            throw new InvalidDataException("No BMD0 model found in this map member.");
        int length = (int)U32(container, bmd + 8);
        if (length < 32 || length > container.Length - bmd)
            throw new InvalidDataException("Invalid BMD0 section size.");
        int mdl = Find(container, "MDL0", bmd + 16);
        if (mdl < 0 || mdl >= bmd + length)
            throw new InvalidDataException("BMD0 does not contain an MDL0 section.");
        int end = Math.Min(bmd + length, container.Length);
        // GX display lists use four packed command bytes followed by their parameter words.
        // Candidate sequences must contain a primitive BEGIN/END and >=3 vertices.
        // This is a geometric preview, not a complete NSBMD polygon/model parser.
        var best = new List<Triangle>();
        var found = new List<(int Start,int End,List<Triangle> Faces)>();
        for (int offset = mdl + 8; offset + 32 < end; offset += 4)
        {
            if (!TryReadList(container, offset, end, out var faces, out int stop)) continue;
            if (faces.Count < 2 || stop - offset < 32) continue;
            if (found.Any(x => offset >= x.Start && offset < x.End)) continue;
            found.Add((offset,stop,faces));
            if (found.Count >= 128) break;
        }
        foreach (var group in found)
        {
            foreach (var t in group.Faces)
            {
                best.Add(t);
                if (best.Count >= 25000) break;
            }
            if (best.Count >= 25000) break;
        }
        return new Result(best, found.Count,
            best.Count == 0 ? "No supported GX polygon lists were detected; model is preserved but not rendered." :
            "Untextured GX vertex preview. Model transforms/materials and map placement are not decoded; coordinates may not align with events.");
    }
    private static int Find(byte[] data, string magic, int from)
    {
        var pattern = System.Text.Encoding.ASCII.GetBytes(magic);
        for (int i = from; i + 4 <= data.Length; i++)
            if (data[i] == pattern[0] && data[i+1] == pattern[1] && data[i+2] == pattern[2] && data[i+3] == pattern[3]) return i;
        return -1;
    }
    private static bool TryReadList(byte[] data, int start, int end, out List<Triangle> faces, out int stop)
    {
        faces = new(); stop = start;
        var vertices = new List<Vertex>();
        double x=0,y=0,z=0;
        int primitive=-1, begins=0, vertexCount=0, commands=0;
        int p=start;
        while (p + 4 <= end && p - start < 65536 && commands < 8000)
        {
            uint packed=U32(data,p); p+=4;
            for (int slot=0;slot<4;slot++)
            {
                int op=(int)((packed>>(slot*8))&255);
                if (!ParameterWords.TryGetValue(op,out int words) || p + words*4 > end) return false;
                commands++;
                if (op==0x40)
                {
                    if (primitive>=0) return false;
                    primitive=(int)(U32(data,p)&3); begins++; vertices.Clear();
                }
                else if (op==0x41)
                {
                    if (primitive<0) return false;
                    primitive=-1;
                    if (begins>0 && vertexCount>=3 && faces.Count>=2) {stop=p;return true;}
                }
                else if (op>=0x23 && op<=0x28)
                {
                    if (primitive<0) return false;
                    switch(op)
                    {
                        case 0x23: x=Fixed(I16(data,p));y=Fixed(I16(data,p+2));z=Fixed(I16(data,p+4));break;
                        case 0x24: x=Fixed(I16(data,p));y=Fixed(I16(data,p+2));break;
                        case 0x25: x=Fixed(I16(data,p));z=Fixed(I16(data,p+2));break;
                        case 0x26: y=Fixed(I16(data,p));z=Fixed(I16(data,p+2));break;
                        case 0x27:
                        {
                            uint v=U32(data,p);
                            static int S10(uint n) { int t=(int)(n&1023);return t>=512?t-1024:t; }
                            x=S10(v)/64.0;y=S10(v>>10)/64.0;z=S10(v>>20)/64.0;break;
                        }
                        case 0x28:
                        {
                            uint v=U32(data,p);
                            static int S10(uint n) { int t=(int)(n&1023);return t>=512?t-1024:t; }
                            x+=S10(v)/4096.0;y+=S10(v>>10)/4096.0;z+=S10(v>>20)/4096.0;break;
                        }
                    }
                    if (!double.IsFinite(x+y+z) || Math.Max(Math.Abs(x),Math.Max(Math.Abs(y),Math.Abs(z)))>10000) return false;
                    var vtx=new Vertex(x,y,z);vertices.Add(vtx);vertexCount++;
                    int n=vertices.Count;
                    if (primitive==0 && n%3==0) faces.Add(new(vertices[n-3],vertices[n-2],vertices[n-1]));
                    if (primitive==1 && n%4==0) {faces.Add(new(vertices[n-4],vertices[n-3],vertices[n-2]));faces.Add(new(vertices[n-4],vertices[n-2],vertices[n-1]));}
                    if (primitive==2 && n>=3) faces.Add(n%2==1?new(vertices[n-3],vertices[n-2],vertices[n-1]):new(vertices[n-2],vertices[n-3],vertices[n-1]));
                    if (primitive==3 && n>=4 && n%2==0) {faces.Add(new(vertices[n-4],vertices[n-3],vertices[n-2]));faces.Add(new(vertices[n-3],vertices[n-1],vertices[n-2]));}
                    if (faces.Count>5000) return false;
                }
                p+=words*4;
            }
        }
        return false;
    }
}
