using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SamKookFreeNet {
  // Exact original build only, after QueuePatch and optional SelectionPatch.
  // Reuses native input/command serialization and native indexed-color text.
  static class GameQualityPatch {
    public static byte[] Apply(byte[] input,bool rally,bool timer) {
      if(!rally && !timer)return input;
      int raw,va;var data=Expand(input,out raw,out va);
      return ApplyDisplayAndInput(data,raw,va,rally,timer);
    }
    static byte[] Expand(byte[] input,out int raw,out int va) {
      int pe=BitConverter.ToInt32(input,0x3c),opt=pe+24;
      int header=opt+BitConverter.ToUInt16(input,pe+20)+(BitConverter.ToUInt16(input,pe+6)-1)*40;
      int oldSize=BitConverter.ToInt32(input,header+8);
      if(Encoding.ASCII.GetString(input,header,6)!=".fnfix" || (oldSize!=512 && oldSize!=65536))
        throw new InvalidDataException("게임 편의 기능 패치 순서 또는 실행 파일 형식 오류");
      raw=BitConverter.ToInt32(input,header+20);int rva=BitConverter.ToInt32(input,header+12);
      va=BitConverter.ToInt32(input,opt+28)+rva;
      var data=new byte[raw+65536];Buffer.BlockCopy(input,0,data,0,input.Length);
      Put(data,header+8,65536);Put(data,header+16,65536);
      int align=BitConverter.ToInt32(input,opt+32);
      Put(data,opt+56,((rva+65536+align-1)/align)*align);Put(data,opt+64,0);
      return data;
    }
    static byte[] ApplyDisplayAndInput(byte[] data,int raw,int va,bool rally,bool timer) {
      if(rally) {
        Check(data,0x62b34,new byte[]{0x15,0,0,0,0,0,0,0,0x20,0x02,0x41,0,0x52,0,0,0});
        var c=new Code(va+16384);c.Emit(0x9c,0x60);
        // Only idle, normal gameplay input on the main terrain viewport.
        c.Emit(0x66,0x83,0x3d);c.Int(0x611e10);c.Emit(0);c.Jcc(0x85,"fallback");
        c.Emit(0x66,0x83,0x3d);c.Int(0x477fa8);c.Emit(0);c.Jcc(0x85,"fallback");
        c.Emit(0x80,0x3d);c.Int(0x476a18);c.Emit(0);c.Jcc(0x85,"fallback");
        c.Emit(0x80,0x3d);c.Int(0x47836b);c.Emit(1);c.Jcc(0x85,"fallback");
        c.Emit(0x83,0x3d);c.Int(0x498698);c.Emit(0);c.Jcc(0x84,"fallback");
        c.Emit(0x0f,0xb7,0x05);c.Int(0x611e12);
        c.Emit(0x85,0xc0);c.Jcc(0x84,"fallback");
        c.Emit(0x3d);c.Int(1699);c.Jcc(0x87,"fallback");
        c.Emit(0x69,0xc0);c.Int(292);
        c.Emit(0x80,0xb8);c.Int(0x49e0be);c.Emit(1);c.Jcc(0x85,"fallback");
        c.Emit(0x66,0x83,0xb8);c.Int(0x49e0c0);c.Emit(0);c.Jcc(0x8e,"fallback");
        c.Emit(0x8a,0x15);c.Int(0x59ee52);
        c.Emit(0x3a,0x90);c.Int(0x49e0bd);c.Jcc(0x85,"fallback");
        // Require the enabled native rally button, not merely a building type.
        c.Emit(0xb9);c.Int(1);c.Label("slot");
        c.Emit(0x66,0x83,0x3c,0x4d);c.Int(0x49869e);c.Emit(21);c.Jcc(0x85,"next");
        c.Emit(0x66,0x83,0x3c,0x4d);c.Int(0x4986be);c.Emit(0);c.Jcc(0x85,"next");
        c.Emit(0x66,0x83,0x3c,0x4d);c.Int(0x4986de);c.Emit(0);c.Jcc(0x84,"rally");
        c.Label("next");c.Emit(0x41,0x83,0xf9,0x10);c.Jcc(0x82,"slot");
        c.Label("fallback");c.Emit(0x61,0x9d);c.Branch(0xe9,0x434870);
        c.Label("rally");
        c.Emit(0x6a,21);c.Branch(0xe8,0x410220);c.Emit(0x83,0xc4,4);
        c.Branch(0xe8,0x434380);c.Emit(0x61,0x9d,0xc3);
        Install(data,raw+16384,c);
        Hook(data,0x333d5,va+16384,new byte[]{0xe8,0x96,0x14,0,0},0xe8);
      }
      if(timer) {
        byte[] format=Encoding.ASCII.GetBytes("%02u:%02u:%02u\0");
        for(int i=0;i<format.Length;i++)if(data[raw+60000+i]!=0)throw new InvalidDataException("타이머 문자열 공간 충돌");
        Buffer.BlockCopy(format,0,data,raw+60000,format.Length);
        var c=new Code(va+17408);c.Emit(0x9c,0x60);
        c.Emit(0x8b,0x3d);c.Int(0x613bd4);c.Emit(0x85,0xff);c.Jcc(0x84,"done");
        c.Emit(0x0f,0xb7,0x35);c.Int(0x613104);
        c.Emit(0x81,0xfe);c.Int(320);c.Jcc(0x82,"done");
        // Native mission countdown uses this same 30 simulation ticks/second.
        // Counter is saved/loaded by the original save-game implementation.
        c.Emit(0xa1);c.Int(0x5173d0);c.Emit(0x33,0xd2,0xb9);c.Int(30);c.Emit(0xf7,0xf1);
        c.Emit(0x33,0xd2,0xb9);c.Int(3600);c.Emit(0xf7,0xf1,0x89,0xc3,0x89,0xd0);
        c.Emit(0x33,0xd2,0xb9);c.Int(60);c.Emit(0xf7,0xf1);
        c.Emit(0x52,0x50,0x53,0x68);c.Int(va+60000);
        c.Emit(0x68);c.Int(150);c.Emit(0x6a,20,0x83,0xee,88,0x56,0x57);
        c.Branch(0xe8,0x453d20);c.Emit(0x83,0xc4,32);
        c.Label("done");c.Emit(0x61,0x9d,0xa1);c.Int(0x6124c0);c.Branch(0xe9,0x442d20);
        Install(data,raw+17408,c);
        Hook(data,0x42d1b,va+17408,new byte[]{0xa1,0xc0,0x24,0x61,0},0xe9);
      }
      return data;
    }
    public static byte[] ApplyRiceRally(byte[] input) {
      int raw,va;var data=Expand(input,out raw,out va);
      var c=new Code(va+18432);
      // Land-unit production branch, after native rally movement initialization.
      // ESI=producer, BX=new unit ID, EAX=new unit byte offset. Replay both writes.
      c.Emit(0x8b,0x4e,0x18,0x89,0x88);c.Int(0x49e0d0);c.Emit(0x9c,0x60);
      c.Emit(0x80,0xb8);c.Int(0x49e0be);c.Emit(0);c.Jcc(0x85,"done");
      c.Emit(0x0f,0xb6,0x90);c.Int(0x49e0bc);
      // Same three worker types recognized by native right-click handler 4386E4.
      c.Emit(0x83,0xfa,1);c.Jcc(0x84,"worker");
      c.Emit(0x83,0xfa,11);c.Jcc(0x84,"worker");
      c.Emit(0x83,0xfa,23);c.Jcc(0x85,"done");
      c.Label("worker");
      c.Emit(0x0f,0xb7,0x4e,0x18,0x81,0xf9);c.Int(232);c.Jcc(0x83,"done");
      c.Emit(0x0f,0xb7,0x56,0x1a,0x81,0xfa);c.Int(232);c.Jcc(0x83,"done");
      c.Emit(0x69,0xd2);c.Int(232);c.Emit(0x03,0xca);
      // Native context resolver 4388F0 chooses harvest (2009) for tile class 3.
      c.Emit(0x80,0xb9);c.Int(0x5da07c);c.Emit(3);c.Jcc(0x85,"done");
      // Save command scratch globals; do not alter any player's selection lists.
      foreach(int address in new[]{0x4868c8,0x4868cc,0x4868d0,0x4868d4}){c.Emit(0xff,0x35);c.Int(address);}
      c.Emit(0x83,0xec,12,0x33,0xc9,0x89,0x0c,0x24,0x89,0x4c,0x24,8);
      c.Emit(0x8b,0x4e,0x18,0x89,0x4c,0x24,4);
      c.Emit(0xc7,0x05);c.Int(0x4868c8);c.Int(0x20090000);
      c.Emit(0x66,0x89,0x1d);c.Int(0x4868d6);
      c.Emit(0x8d,0x0c,0x24,0x51);c.Branch(0xe8,0x437ff0);c.Emit(0x83,0xc4,16);
      foreach(int address in new[]{0x4868d4,0x4868d0,0x4868cc,0x4868c8}){c.Emit(0x8f,0x05);c.Int(address);}
      c.Label("done");c.Emit(0x61,0x9d);c.Branch(0xe9,0x412542);
      Install(data,raw+18432,c);
      Hook(data,0x12539,va+18432,new byte[]{0x8b,0x4e,0x18,0x89,0x88,0xd0,0xe0,0x49,0},0xe9);
      return data;
    }
    static void Put(byte[] b,int o,int v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,4);}
    static void Check(byte[] b,int o,byte[] expected){for(int i=0;i<expected.Length;i++)if(b[o+i]!=expected[i])throw new InvalidDataException("편의 기능 명령어 검증 실패: "+o.ToString("X"));}
    static void Hook(byte[] b,int o,int target,byte[] expected,byte opcode){Check(b,o,expected);b[o]=opcode;Put(b,o+1,target-(0x400000+o+5));for(int i=5;i<expected.Length;i++)b[o+i]=0x90;}
    static void Install(byte[] b,int o,Code c){var bytes=c.Finish();if(bytes.Length>1024)throw new InvalidDataException("편의 기능 코드 공간 초과");for(int i=0;i<bytes.Length;i++)if(b[o+i]!=0)throw new InvalidDataException("편의 기능 코드 공간 충돌");Buffer.BlockCopy(bytes,0,b,o,bytes.Length);}
    sealed class Code {
      readonly int start;readonly List<byte> bytes=new List<byte>();
      readonly Dictionary<string,int> labels=new Dictionary<string,int>();readonly Dictionary<int,string> jumps=new Dictionary<int,string>();
      public Code(int address){start=address;}
      public void Emit(params byte[] b){bytes.AddRange(b);}public void Int(int v){bytes.AddRange(BitConverter.GetBytes(v));}
      public void Label(string name){labels.Add(name,bytes.Count);}
      public void Jcc(byte condition,string label){Emit(0x0f,condition);jumps.Add(bytes.Count,label);Int(0);}
      public void Branch(byte opcode,int target){Emit(opcode);Int(target-(start+bytes.Count+4));}
      public byte[] Finish(){var b=bytes.ToArray();foreach(var j in jumps)Put(b,j.Key,labels[j.Value]-j.Key-4);return b;}
    }
  }
}
