using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SamKookFreeNet {
  // Exact-build patch: serialize enqueue and drain, including allocation and
  // freeing. The original enqueue publishes a node before initializing it.
  static class QueuePatch {
    public static void ReduceSenderSpin(byte[] data) {
      int pe=BitConverter.ToInt32(data,0x3c), opt=pe+24;
      int table=opt+BitConverter.ToUInt16(data,pe+20);
      int header=table+(BitConverter.ToUInt16(data,pe+6)-1)*40;
      int raw=BitConverter.ToInt32(data,header+20), va=BitConverter.ToInt32(data,opt+28)+BitConverter.ToInt32(data,header+12);
      byte[] expected={0xa1,0xac,0x82,0x47,0x00};
      for(int i=0;i<5;i++)if(data[0x4a1a0+i]!=expected[i])throw new InvalidDataException("송신 루프 검증 실패");
      Buffer.BlockCopy(Encoding.ASCII.GetBytes("Sleep\0kernel32.dll\0"),0,data,raw+340,19);
      var b=new List<byte>(); b.AddRange(new byte[]{0x9c,0x60,0xa1}); I32(b,va+336);
      b.AddRange(new byte[]{0x85,0xc0,0x75,0}); int cached=b.Count-1;
      b.Add(0x68);I32(b,va+346);b.AddRange(new byte[]{0xff,0x15});I32(b,0x45f124);
      b.Add(0x68);I32(b,va+340);b.Add(0x50);b.AddRange(new byte[]{0xff,0x15});I32(b,0x45f098);
      b.Add(0xa3);I32(b,va+336);b.AddRange(new byte[]{0x85,0xc0,0x74,0});int missing=b.Count-1;
      // Sleep(0) yields the current time slice without imposing a timer-sized
      // delay on every pass through the multiplayer sender loop.
      int invoke=b.Count; b.AddRange(new byte[]{0x6a,0x00,0xff,0xd0});int finish=b.Count;
      b[cached]=(byte)(invoke-cached-1);b[missing]=(byte)(finish-missing-1);
      b.AddRange(new byte[]{0x61,0x9d});b.AddRange(expected);b.Add(0xe9);I32(b,0x44a1a5-(va+368+b.Count+4));
      if(368+b.Count>512)throw new InvalidDataException("패치 공간 부족");
      Buffer.BlockCopy(b.ToArray(),0,data,raw+368,b.Count);
      data[0x4a1a0]=0xe9;Put(data,0x4a1a1,va+368-0x44a1a5);
    }
    public static void ReduceMultiplayerLatency(byte[] data) {
      // Exact-build safeguards. These are the four command-buffer thresholds
      // and two "once per six simulation frames" divisors in SamKook.exe.
      byte[] table={0,0,2,0,4,0,6,0};
      for(int i=0;i<table.Length;i++)if(data[0x65800+i]!=table[i])
        throw new InvalidDataException("멀티 저지연 패치가 지원하지 않는 게임 버전입니다.");
      if(data[0x42f6b]!=0xb9 || BitConverter.ToInt32(data,0x42f6c)!=6 ||
         data[0x4300b]!=0xb9 || BitConverter.ToInt32(data,0x4300c)!=6)
        throw new InvalidDataException("멀티 동기화 주기 검증 실패");
      // Preserve a small jitter buffer for slower peers while reducing the
      // original 0/2/4/6-frame buffering and six-frame polling cadence.
      byte[] reduced={0,0,0,0,1,0,2,0};
      Buffer.BlockCopy(reduced,0,data,0x65800,reduced.Length);
      Put(data,0x42f6c,3);Put(data,0x4300c,3);
    }
    public static byte[] ApplyAddress(byte[] original,string address) {
      System.Net.IPAddress ip;
      if(!System.Net.IPAddress.TryParse(address,out ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)
        throw new ArgumentException("IPv4 주소가 필요합니다.");
      var result=Apply(original,original);
      int pe=BitConverter.ToInt32(result,0x3c), opt=pe+24;
      int table=opt+BitConverter.ToUInt16(result,pe+20);
      int header=table+(BitConverter.ToUInt16(result,pe+6)-1)*40;
      int raw=BitConverter.ToInt32(result,header+20), rva=BitConverter.ToInt32(result,header+12);
      var bytes=Encoding.ASCII.GetBytes(ip.ToString()+"\0");
      Buffer.BlockCopy(bytes,0,result,raw+320,bytes.Length);
      // Verified PUSH operands, not a global replacement of arbitrary bytes.
      if(result[0x281ff]!=0x68 || BitConverter.ToInt32(result,0x28200)!=0x4642f0
        || result[0x28225]!=0x68 || BitConverter.ToInt32(result,0x28226)!=0x4642e0)
        throw new InvalidDataException("접속 주소 명령어 검증 실패");
      int target=BitConverter.ToInt32(result,opt+28)+rva+320;
      Put(result,0x28200,target); Put(result,0x28226,target);
      return result;
    }
    public static byte[] Apply(byte[] original, byte[] addressed) {
      using(var sha=SHA256.Create()) {
        if(BitConverter.ToString(sha.ComputeHash(original)).Replace("-","") !=
          "39A11E76F5328A66A4FE8DCB1318ECE6362843D8192CAA8C7E15F0FC08ABDC62")
          throw new InvalidDataException("송신 큐 패치가 지원하지 않는 게임 버전입니다.");
      }
      int pe=BitConverter.ToInt32(original,0x3c), opt=pe+24;
      int count=BitConverter.ToUInt16(original,pe+6);
      int table=opt+BitConverter.ToUInt16(original,pe+20);
      int last=table+(count-1)*40, header=table+count*40;
      if(header+40>BitConverter.ToInt32(original,opt+60)) throw new InvalidDataException("PE 헤더 공간 부족");
      int align=BitConverter.ToInt32(original,opt+32), fileAlign=BitConverter.ToInt32(original,opt+36);
      int rva=Align(BitConverter.ToInt32(original,last+12)+Math.Max(BitConverter.ToInt32(original,last+8),BitConverter.ToInt32(original,last+16)),align);
      int raw=Align(original.Length,fileAlign), size=Align(512,fileAlign);
      var result=new byte[raw+size]; Buffer.BlockCopy(addressed,0,result,0,addressed.Length);
      Buffer.BlockCopy(Encoding.ASCII.GetBytes(".fnfix"),0,result,header,6);
      Put(result,header+8,512); Put(result,header+12,rva); Put(result,header+16,size); Put(result,header+20,raw);
      Put(result,header+36,unchecked((int)0xE0000060)); // executable code plus lock storage
      result[pe+6]=(byte)(count+1); result[pe+7]=0;
      Put(result,opt+56,Align(rva+512,align)); Put(result,opt+64,0);
      int imageBase=BitConverter.ToInt32(original,opt+28), section=imageBase+rva;
      // First dword is the shared, initially zero lock. No application-global
      // scratch registers or queue fields are changed by the wrappers.
      Install(result,raw+16,section+16,section,0x3cca0,2,new byte[]{0x56,0x8b,0x74,0x24,0x08});
      Install(result,raw+160,section+160,section,0x3cd80,3,new byte[]{0x56,0x57,0x8b,0x7c,0x24,0x0c});
      return result;
    }
    static void Install(byte[] data,int raw,int va,int mutex,int originalOffset,int argc,byte[] prologue) {
      for(int i=0;i<prologue.Length;i++) if(data[originalOffset+i]!=prologue[i]) throw new InvalidDataException("게임 명령어 검증 실패");
      var b=new List<byte>();
      b.AddRange(new byte[]{0xb8,1,0,0,0,0x87,0x05}); I32(b,mutex); // xchg is atomic with memory
      b.AddRange(new byte[]{0x85,0xc0,0x74,0x04,0xf3,0x90,0xeb,0xed}); // wait with PAUSE, retry
      for(int i=0;i<argc;i++) b.AddRange(new byte[]{0xff,0x74,0x24,(byte)(argc*4)});
      int call=b.Count; b.Add(0xe8); I32(b,0);
      b.AddRange(new byte[]{0x83,0xc4,(byte)(argc*4),0xc7,0x05}); I32(b,mutex); I32(b,0); b.Add(0xc3);
      int trampoline=b.Count;
      b.AddRange(prologue); b.Add(0xe9); I32(b,0x400000+originalOffset+prologue.Length-(va+b.Count+4));
      var code=b.ToArray(); Put(code,call+1,trampoline-(call+5));
      Buffer.BlockCopy(code,0,data,raw,code.Length);
      data[originalOffset]=0xe9; Put(data,originalOffset+1,va-(0x400000+originalOffset+5));
      for(int i=5;i<prologue.Length;i++)data[originalOffset+i]=0x90;
    }
    static int Align(int n,int a) { return (n+a-1)/a*a; }
    static void I32(List<byte> b,int n) { b.AddRange(BitConverter.GetBytes(n)); }
    static void Put(byte[] b,int offset,int n) { Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,offset,4); }
  }
}
