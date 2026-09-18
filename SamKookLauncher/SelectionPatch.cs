using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SamKookFreeNet {
  // Optional exact-build protocol extension. Every peer must use the same mode.
  static class SelectionPatch {
    public const int Limit=36;
    public const int DataOffset=32768;
    const int DataLength=12288;
    public static byte[] Apply(byte[] patched) {
      // QueuePatch.ApplyAddress has already required the exact original SHA256.
      int pe=BitConverter.ToInt32(patched,0x3c),opt=pe+24;
      int table=opt+BitConverter.ToUInt16(patched,pe+20);
      int header=table+(BitConverter.ToUInt16(patched,pe+6)-1)*40;
      if(Encoding.ASCII.GetString(patched,header,6)!=".fnfix" || BitConverter.ToInt32(patched,header+8)!=512)
        throw new InvalidDataException("선택 확장 패치 순서 또는 실행 파일 형식이 잘못되었습니다.");
      int raw=BitConverter.ToInt32(patched,header+20),rva=BitConverter.ToInt32(patched,header+12);
      int va=BitConverter.ToInt32(patched,opt+28)+rva,dataVA=va+DataOffset;
      int size=65536;
      var data=new byte[raw+size];Buffer.BlockCopy(patched,0,data,0,patched.Length);
      Put(data,header+8,size);Put(data,header+16,size);
      int align=BitConverter.ToInt32(data,opt+32);
      Put(data,opt+56,((rva+size+align-1)/align)*align);Put(data,opt+64,0);
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("selection36.manifest")) {
        if(stream==null)throw new InvalidDataException("선택 확장 패치 목록이 없습니다.");
        using(var reader=new StreamReader(stream)) {
          string line;while((line=reader.ReadLine())!=null) {
            if(line.Length==0 || line[0]=='#')continue;
            var fields=line.Split(' ');int offset=Hex(fields[1]);
            if(fields[0]=="P") {
              if(BitConverter.ToInt32(data,offset)!=Hex(fields[2]))throw new InvalidDataException("선택 배열 주소 검증 실패: "+fields[1]);
              Put(data,offset,dataVA+Hex(fields[3]));
            } else CheckWrite(data,offset,Bytes(fields[2]),Bytes(fields[3]));
          }
        }
      }
      // Fresh scenario: clear relocated arrays, then replay the displaced prologue.
      var reset=new Code(va+512);
      reset.Emit(0x9c,0x60);Clear(reset,dataVA);reset.Emit(0x61,0x9d,0x56,0x57,0xb9);reset.Int(11);
      reset.Branch(0xe9,0x447b47);Install(data,raw+512,reset);
      Hook(data,0x47b40,va+512,Bytes("5657b90b000000"),0xe9);

      // Keep old save files readable. The legacy format stores twelve IDs per
      // group: export/import those IDs, without changing any game/unit state.
      var import=new Code(va+1024);import.Emit(0x9c,0x60);Clear(import,dataVA);
      Transfer(import,0x49b302,dataVA+256,true);import.Emit(0x61,0x9d,0xc3);Install(data,raw+1024,import);
      var export=new Code(va+1536);export.Emit(0x9c,0x60);
      Transfer(export,dataVA+256,0x49b302,false);export.Emit(0x61,0x9d,0xc3);Install(data,raw+1536,export);
      var save=new Code(va+2048);save.Branch(0xe8,va+1536);save.Branch(0xe9,0x455412);Install(data,raw+2048,save);
      Hook(data,0x2b396,va+2048,Bytes("e877a00200"),0xe8);
      var read=new Code(va+2304);
      for(int i=0;i<4;i++)read.Emit(0xff,0x74,0x24,0x10);
      read.Branch(0xe8,0x454caa);read.Emit(0x83,0xc4,0x10);read.Branch(0xe8,va+1024);read.Emit(0xc3);Install(data,raw+2304,read);
      Hook(data,0x2ba24,va+2304,Bytes("e881920200"),0xe8);
      Hook(data,0x2bbfe,va+2304,Bytes("e8a7900200"),0xe8);
      return data;
    }
    static void Clear(Code c,int address) {
      c.Emit(0x33,0xc0,0xb9);c.Int(DataLength/4);c.Emit(0xbf);c.Int(address);c.Emit(0xfc,0xf3,0xab);
    }
    static void Transfer(Code c,int source,int dest,bool import) {
      c.Emit(0xfc,0xbe);c.Int(source);c.Emit(0xbf);c.Int(dest);c.Emit(0xba);c.Int(9);
      int row=c.Here;c.Emit(0xbd);c.Int(15);
      int group=c.Here;c.Emit(0x33,0xdb,0xb9);c.Int(12);
      int unit=c.Here;c.Emit(0x66,0xad,0x66,0xab,0x66,0x85,0xc0,0x74,0x01,0x43,0x49);
      c.Jcc(0x85,unit);
      if(import) {
        c.Emit(0x66,0x89,0x5f,0x30,0x83,0xc6,0x02,0x83,0xc7,0x32);
      } else {
        c.Emit(0x66,0x89,0x1f,0x83,0xc6,0x32,0x83,0xc7,0x02);
      }
      c.Emit(0x4d);c.Jcc(0x85,group);
      c.Emit(0x81,0xc6);c.Int(import?734:14);c.Emit(0x81,0xc7);c.Int(import?14:734);
      c.Emit(0x4a);c.Jcc(0x85,row);
    }
    static void Install(byte[] data,int offset,Code code) {
      if(code.Data.Count>256)throw new InvalidDataException("선택 확장 코드 공간 초과");
      Buffer.BlockCopy(code.Data.ToArray(),0,data,offset,code.Data.Count);
    }
    static void Hook(byte[] data,int offset,int target,byte[] expected,byte opcode) {
      var code=new byte[expected.Length];for(int i=0;i<code.Length;i++)code[i]=0x90;
      code[0]=opcode;Put(code,1,target-(0x400000+offset+5));CheckWrite(data,offset,expected,code);
    }
    static void CheckWrite(byte[] data,int offset,byte[] expected,byte[] replacement) {
      if(expected.Length!=replacement.Length)throw new InvalidDataException("패치 크기 오류");
      for(int i=0;i<expected.Length;i++)if(data[offset+i]!=expected[i])throw new InvalidDataException("선택 확장 명령어 검증 실패: "+offset.ToString("X"));
      Buffer.BlockCopy(replacement,0,data,offset,replacement.Length);
    }
    static int Hex(string s){return int.Parse(s,NumberStyles.HexNumber,CultureInfo.InvariantCulture);}
    static byte[] Bytes(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=(byte)Hex(s.Substring(i*2,2));return b;}
    static void Put(byte[] b,int offset,int value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,offset,4);}
    sealed class Code {
      public readonly List<byte> Data=new List<byte>();readonly int start;
      public Code(int address){start=address;}
      public int Here {get{return start+Data.Count;}}
      public void Emit(params byte[] bytes){Data.AddRange(bytes);}
      public void Int(int value){Data.AddRange(BitConverter.GetBytes(value));}
      public void Branch(byte op,int target){Emit(op);Int(target-(Here+4));}
      public void Jcc(byte condition,int target){Emit(0x0f,condition);Int(target-(Here+4));}
    }
  }
}
