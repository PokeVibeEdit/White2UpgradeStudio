#!/usr/bin/env python3
"""White 2 Upgrade starter screen sync companion. GPL-3.0-only.

Offline Python 3 backend for the main Avalonia editor. Requires a user-supplied
edited .nds and writes a different .nds; it never overwrites its input.
"""
from __future__ import annotations
import hashlib, os, re, shutil, struct, sys, tempfile
from pathlib import Path

SCRIPT = "a/0/5/6"
BATTLE = "a/0/0/4"
MENU = "a/2/0/2"
PERSONAL = "a/0/1/6"
TEXT = "a/0/0/3"
OVERLAY = 316
SPECIES_POS = 11284
CONTEXT_SHA = "598121c741dc5461a0b5f6bcbe19cf4869c4f5110e8eabecc5759f93e4ee2c49"
STARTER_POSITIONS = ((0x58B, 0x590, 0x595), (0x5C0, 0x5C5, 0x5CA), (0x5E2, 0x5E7, 0x5EC))
VANILLA = (495, 498, 501)
TYPES = ("Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost",
         "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy")

class PatchError(Exception): pass
def u16(b, p): return struct.unpack_from("<H", b, p)[0]
def u32(b, p): return struct.unpack_from("<I", b, p)[0]
def sha(b): return hashlib.sha256(b).hexdigest()

class NARC:
    def __init__(self, data):
        if data[:4] != b"NARC": raise PatchError("Not a NARC archive.")
        self.data = data
        self.files = []
        fat, base, end = None, None, None
        cursor = 16
        while cursor + 8 <= len(data):
            tag = data[cursor:cursor+4]
            size = u32(data, cursor+4)
            if size < 8 or cursor+size > len(data): raise PatchError("Invalid NARC block.")
            if tag == b"BTAF":
                count = u16(data, cursor+8)
                fat = [struct.unpack_from("<II", data, cursor+12+i*8) for i in range(count)]
            if tag == b"GMIF": base, end = cursor+8, cursor+size
            cursor += size
        if fat is None or base is None: raise PatchError("Incomplete NARC.")
        self.files = [data[base+a:base+b] for a,b in fat]
        if any(a>b or base+b>end for a,b in fat): raise PatchError("NARC FAT extends beyond archive.")
        self.ranges = [(base+a, base+b) for a,b in fat]

    def get(self, i): return self.files[i]
    def replace(self, updates):
        if all(len(value) == len(self.files[i]) for i, value in updates.items()):
            result = bytearray(self.data)
            for i, value in updates.items():
                a,b=self.ranges[i]
                result[a:b]=value
            return bytes(result)
        files = [updates.get(i, self.files[i]) for i in range(len(self.files))]
        image=bytearray(); positions=[]
        for data in files:
            a=len(image)
            image+=data
            positions.append((a,len(image)))
            image+=b"\xff"*((-len(image))%4)
        fat=struct.pack("<4sIHH",b"BTAF",12+8*len(files),len(files),0)
        fat+=b"".join(struct.pack("<II",a,b) for a,b in positions)
        fnt=struct.pack("<4sIIHH",b"BTNF",16,4,0,1)
        gmif=struct.pack("<4sI",b"GMIF",len(image)+8)+image
        return struct.pack("<4sHHIHH",b"NARC",0xFFFE,0x100,16+len(fat)+len(fnt)+len(gmif),16,3)+fat+fnt+gmif

class Rom:
    def __init__(self, path):
        self.path=Path(path).resolve()
        b=self.path.read_bytes()
        if len(b)<0x200: raise PatchError("ROM header is too short.")
        self.raw=b
        fnt=u32(b,0x40); fnt_size=u32(b,0x44)
        self.fat_start=u32(b,0x48); fat_size=u32(b,0x4c)
        ovt=u32(b,0x50); ovt_size=u32(b,0x54)
        if fnt+fnt_size>len(b) or self.fat_start+fat_size>len(b) or ovt+ovt_size>len(b):
            raise PatchError("Invalid ROM filesystem tables.")
        self.fat=[struct.unpack_from("<II",b,self.fat_start+i*8) for i in range(fat_size//8)]
        dirs=[(u32(b,fnt+i*8),u16(b,fnt+i*8+4)) for i in range(u16(b,fnt+6))]
        self.paths={}
        def walk(d,prefix):
            pos, fid=dirs[d]
            while pos<fnt_size:
                marker=b[fnt+pos];pos+=1
                if not marker: break
                name=b[fnt+pos:fnt+pos+(marker&127)].decode("ascii","replace")
                pos+=marker&127
                if marker&128:
                    child=u16(b,fnt+pos)-0xf000;pos+=2
                    walk(child,prefix+name+"/")
                else:
                    self.paths[prefix+name]=fid;fid+=1
        walk(0,"")
        self.overlays={}
        for i in range(ovt,ovt+ovt_size,32):
            self.overlays[u32(b,i)]=u32(b,i+24)
    def read_id(self,fid):
        a,b=self.fat[fid]
        if a>b or b>len(self.raw): raise PatchError("Invalid ROM FAT range.")
        return self.raw[a:b]
    def fid(self,name):
        for p in (name,"data/"+name):
            if p in self.paths: return self.paths[p]
        raise PatchError("ROM resource is missing: "+name)
    def read(self,name): return self.read_id(self.fid(name))
    def overlay(self,i): return self.read_id(self.overlays[i])

def nitro_decompress(data):
    if not data or data[0] not in (0x10,0x11): return data
    kind=data[0]; length=int.from_bytes(data[1:4],"little");cur=4
    if length==0: length=u32(data,4);cur=8
    if length>16_000_000: raise PatchError("Unsupported compressed sprite size.")
    out=bytearray()
    while len(out)<length:
        if cur>=len(data):raise PatchError("Truncated Nitro LZ.")
        flags=data[cur];cur+=1
        for bit in range(7,-1,-1):
            if len(out)>=length: break
            if flags&(1<<bit):
                a=data[cur];b=data[cur+1];cur+=2
                if kind==0x10:
                    n=(a>>4)+3;dist=(((a&15)<<8)|b)+1
                elif a>>4==0:
                    c=data[cur];cur+=1;n=((a&15)<<4|b>>4)+0x11;dist=((b&15)<<8|c)+1
                elif a>>4==1:
                    c=data[cur];d=data[cur+1];cur+=2
                    n=((a&15)<<12|b<<4|c>>4)+0x111;dist=((c&15)<<8|d)+1
                else:
                    n=(a>>4)+1;dist=(((a&15)<<8)|b)+1
                if not 0<dist<=len(out):raise PatchError("Invalid LZ backreference.")
                for _ in range(min(n,length-len(out))): out.append(out[-dist])
            else:
                out.append(data[cur]);cur+=1
    return bytes(out)

ORDER=[]
for xx,yy,ww,hh in ((0,0,64,64),(64,0,32,64),(0,64,64,32),(64,64,32,32)):
    for y in range(yy,yy+hh,8):
        for x in range(xx,xx+ww,8): ORDER.append(y//8*12+x//8)

def untile(raw):
    if len(raw)!=4608: raise PatchError("Unexpected 96x96 tile length.")
    pixel=bytearray(96*96)
    for stored, tiled in enumerate(ORDER):
        x=tiled%12*8;y=tiled//12*8
        for row in range(8):
            for col in range(4):
                c=raw[stored*32+row*4+col]
                idx=(y+row)*96+x+2*col
                pixel[idx]=c&15;pixel[idx+1]=c>>4
    return pixel

def retile(pixel):
    raw=bytearray(4608)
    for stored,tiled in enumerate(ORDER):
        x=tiled%12*8;y=tiled//12*8
        for row in range(8):
            for col in range(4):
                idx=(y+row)*96+x+col*2
                raw[stored*32+row*4+col]=pixel[idx]|(pixel[idx+1]<<4)
    return bytes(raw)

def enlarge(pixel):
    points=[(i%96,i//96) for i,c in enumerate(pixel) if c]
    if not points: raise PatchError("Battle sprite is blank.")
    x1=min(x for x,y in points);x2=max(x for x,y in points)
    y1=min(y for x,y in points);y2=max(y for x,y in points)
    w=x2-x1+1;h=y2-y1+1
    scale=min(min(88,2*w)/w,min(84,2*h)/h)
    dw=min(96,max(1,round(w*scale)));dh=min(96,max(1,round(h*scale)))
    x0=(96-dw)//2;y0=max(0,86-dh)
    out=bytearray(96*96)
    for y in range(dh):
        sy=y1+min(h-1,int(y*h/dh))
        for x in range(dw):
            sx=x1+min(w-1,int(x*w/dw))
            out[(y0+y)*96+x0+x]=pixel[sy*96+sx]
    return out

def dim(v):
    r,g,b=(v&31),(v>>5)&31,(v>>10)&31
    f=lambda x:min(4,(x+4)//8)
    return (v&0x8000)|f(r)|(f(g)<<5)|(f(b)<<10)

def edit_palette(raw, colors):
    if len(raw)!=552 or raw[:4]!=b"RLCN" or raw[16:20]!=b"TTLP":
        raise PatchError("Unsupported menu palette format.")
    out=bytearray(raw)
    for i in range(1,16):struct.pack_into("<H",out,40+i*2,colors[i])
    return bytes(out)

def decrypt(words,index):
    key=(0x7c89+0x2983*index)&65535
    out=[]
    for v in words:
        out.append(v^key)
        key=((key<<3)|(key>>13))&65535
    return out

def read_bank(data):
    if u16(data,0)!=1: raise PatchError("Unsupported message language count.")
    count=u16(data,2); section=u32(data,12)
    messages=[]
    for i in range(count):
        offset,length,attribute=struct.unpack_from("<IHH",data,section+4+i*8)
        if attribute:raise PatchError("Unsupported message attribute.")
        words=struct.unpack_from("<%dH"%length,data,section+offset)
        clear=decrypt(words,i)
        if clear[-1]!=0xffff: raise PatchError("Message bank decryption failed.")
        messages.append(clear[:-1])
    end=4+count*8+2*sum(u16(data,section+8+i*8) for i in range(count))
    pad=data[section+end] if section+end<len(data) else 0
    return messages,pad

def write_bank(messages,pad):
    table=bytearray();body=bytearray();offset=4+8*len(messages)
    for i,raw in enumerate(messages):
        words=decrypt(raw+[0xffff],i)
        table+=struct.pack("<IHH",offset,len(words),0)
        body+=struct.pack("<%dH"%len(words),*words)
        offset+=len(words)*2
    size=(4+len(table)+len(body)+3)&~3
    section=struct.pack("<I",size)+table+body
    section+=bytes([pad])*(size-len(section))
    return struct.pack("<HHIII",1,len(messages),size,0,16)+section

def expected_type_message(type_name):
    # Preserve the exact game's color markup and species-name placeholder.
    prefix=[0xF000,0xBD02,0]
    text="The "+type_name+"-type Pokémon"
    middle=[0xFFFE,0xF000,0xBD02,0,0xF000,0x0101,1,1]
    return prefix+list(map(ord,text))+middle

def patch_starter(src):
    gift=NARC(src.read(SCRIPT)).get(854)
    chosen=[]
    for trio in STARTER_POSITIONS:
        values=[u16(gift,pos) for pos in trio]
        if len(set(values))!=1 or not 1<=values[0]<=721:
            raise PatchError("Starter script member 854 does not match supported White 2 Upgrade.")
        chosen.append(values[0])
    overlay=src.overlay(OVERLAY)
    context=overlay[SPECIES_POS-16:SPECIES_POS]+overlay[SPECIES_POS+6:SPECIES_POS+22]
    if sha(context)!=CONTEXT_SHA:raise PatchError("Unsupported starter selection overlay 316.")
    previous=struct.unpack_from("<HHH",overlay,SPECIES_POS)
    updated_overlay=bytearray(overlay)
    struct.pack_into("<HHH",updated_overlay,SPECIES_POS,*chosen)

    personal=NARC(src.read(PERSONAL))
    types=[]
    for species in chosen:
        ty=personal.get(species)[6]
        if ty>=len(TYPES): raise PatchError("Unsupported Pokémon type ID: "+str(ty))
        types.append(TYPES[ty])
    text=NARC(src.read(TEXT))
    bank,pad=read_bank(text.get(169))
    for index, name in zip((37,36,35),types):
        if not bank[index][:3]==[0xF000,0xBD02,0]:
            raise PatchError("Unsupported Aspertia City text header.")
        # Keep old content only if known starter type message format.
        if 0xFFFE not in bank[index]:
            raise PatchError("Unexpected starter description message format.")
    for index,name in zip((37,36,35),types):
        bank[index]=expected_type_message(name)
    updated_text=text.replace({169:write_bank(bank,pad)})

    battle=NARC(src.read(BATTLE))
    menu=NARC(src.read(MENU))
    updated={}
    for slot,species in enumerate(chosen):
        if species==VANILLA[slot] and previous[slot]==VANILLA[slot]: continue
        image=nitro_decompress(battle.get(species*20))
        if len(image)!=4656 or image[:4]!=b"RGCN":raise PatchError("Unsupported battle sprite.")
        front=untile(image[48:])
        colors=nitro_decompress(battle.get(species*20+18))
        if colors[:4]!=b"RLCN" or len(colors)<72:raise PatchError("Unsupported battle palette.")
        words=[u16(colors,40+i*2) for i in range(16)]
        existing=menu.get(12+slot)
        if len(existing)!=4656 or existing[:4]!=b"RGCN":raise PatchError("Unexpected menu NCGR.")
        updated[12+slot]=existing[:48]+retile(enlarge(front))
        updated[slot*2]=edit_palette(menu.get(slot*2),words)
        updated[slot*2+1]=edit_palette(menu.get(slot*2+1),[dim(v) for v in words])
    return {src.overlays[OVERLAY]:bytes(updated_overlay),src.fid(TEXT):updated_text,
            src.fid(MENU):menu.replace(updated)},tuple(chosen),tuple(types)

def crc16(buf):
    crc=0xFFFF
    for value in buf:
        crc ^= value
        for _ in range(8): crc=(crc>>1)^ (0xA001 if crc&1 else 0)
    return crc&0xffff

def export(input_name,output_name):
    source=Path(input_name).resolve();target=Path(output_name).resolve()
    if source==target or target.exists() or not target.parent.is_dir():
        raise PatchError("Output must be a NEW .nds in an existing folder, not the source.")
    src=Rom(source)
    changed,chosen,types=patch_starter(src)
    fd,temp=tempfile.mkstemp(prefix=".starter-sync-",suffix=".nds",dir=target.parent)
    os.close(fd)
    try:
        shutil.copyfile(source,temp)
        with open(temp,"r+b") as f:
            f.seek(0,2);end=f.tell()
            for fid,blob in changed.items():
                a,b=src.fat[fid]
                if len(blob)==b-a:
                    f.seek(a);f.write(blob)
                else:
                    align=(-end)%4
                    f.seek(end);f.write(b"\xff"*align)
                    start=end+align;f.write(blob);end=start+len(blob)
                    f.seek(src.fat_start+fid*8);f.write(struct.pack("<II",start,end))
            f.seek(0)
            header=bytearray(f.read(0x200))
            if u32(header,0x80)<end:struct.pack_into("<I",header,0x80,end)
            cap=header[0x14]
            while (128*1024<<cap)<end and cap<16:cap+=1
            header[0x14]=cap
            struct.pack_into("<H",header,0x15e,crc16(header[:0x15e]))
            f.seek(0);f.write(header);f.flush();os.fsync(f.fileno())
        verify=Rom(temp)
        for fid,blob in changed.items():
            if verify.read_id(fid)!=blob:raise PatchError("Patch read-back failed for file ID "+str(fid))
        os.rename(temp,target)
    finally:
        if os.path.exists(temp):os.unlink(temp)
    return chosen,types

def self_test():
    p=bytearray(96*96)
    for y in range(5,30):
        for x in range(4,31):p[y*96+x]=(x+y)%16
    assert untile(retile(p))==p
    bank=[[ord("H"),ord("i")],expected_type_message("Normal"),expected_type_message("Fairy")]
    packed=write_bank(bank,0)
    messages,_=read_bank(packed)
    assert bank==messages
    print("Starter sync sprite + message codec self-tests passed.")

def main():
    if len(sys.argv)==2 and sys.argv[1]=="--self-test":self_test();return
    if len(sys.argv)!=4 or sys.argv[1]!="--export":
        raise PatchError("Usage: starter_menu_sync.py --export input.nds output.nds")
    chosen,types=export(sys.argv[2],sys.argv[3])
    print("Starter menu patched:",chosen,"types:",types)

if __name__=="__main__":
    try:main()
    except (OSError,IndexError,struct.error,PatchError) as ex:
        print("Starter menu sync failed:",ex,file=sys.stderr);sys.exit(1)
