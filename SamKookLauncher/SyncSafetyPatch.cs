using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SamKookFreeNet {
 // Applied last to the SHA256-validated runtime copy. No new wire packets.
 static class SyncSafetyPatch {
  public static string Profile(bool latency,bool selection,bool rice,bool observer) {
   return "sync-safety-1;latency="+latency+";selection36="+selection+";rice="+rice+";observer2="+observer;
  }
  public static byte[] Apply(byte[] input,string logPath,bool latency,bool selection,bool rice,bool observer) {
   int pe=BitConverter.ToInt32(input,0x3c),opt=pe+24;
   int h=opt+BitConverter.ToUInt16(input,pe+20)+(BitConverter.ToUInt16(input,pe+6)-1)*40;
   int raw=BitConverter.ToInt32(input,h+20);
   if(Encoding.ASCII.GetString(input,h,6)!=".fnfix" || BitConverter.ToInt32(input,h+8)!=65536 ||
      BitConverter.ToInt32(input,opt+28)+BitConverter.ToInt32(input,h+12)!=0x630000 || input.Length!=raw+65536)
    throw new InvalidDataException("동기화 보호 패치 순서/주소 검증 실패");
   var data=(byte[])input.Clone();
   for(int i=0xc400;i<0xe000;i++)if(data[raw+i]!=0)throw new InvalidDataException("동기화 보호 공간 충돌");
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("sync.manifest")) {
    if(stream==null)throw new InvalidDataException("동기화 보호 목록 누락");
    using(var reader=new StreamReader(stream)) {
     string line;while((line=reader.ReadLine())!=null) {
      if(line.Length==0 || line[0]=='#')continue;
      var f=line.Split(' ');int offset=Hex(f[1]);byte[] before,after;
      if(f[0]=="B") {
       after=Bytes(f[2]);before=new byte[after.Length];
       if(offset<0xc400 || offset+after.Length>0xd800)throw new InvalidDataException("동기화 보호 공간 범위 오류");
       offset+=raw;
      }else if(f[0]=="H"){before=Bytes(f[2]);after=Bytes(f[3]);}
      else throw new InvalidDataException("동기화 보호 목록 형식 오류");
      if(before.Length!=after.Length || offset<0 || offset>data.Length-before.Length)throw new InvalidDataException("동기화 보호 길이 오류");
      for(int i=0;i<before.Length;i++)if(data[offset+i]!=before[i])throw new InvalidDataException("동기화 보호 명령어 검증 실패: "+offset.ToString("X"));
      Buffer.BlockCopy(after,0,data,offset,after.Length);
     }
    }
   }
   if(!Path.IsPathRooted(logPath))throw new ArgumentException("동기화 로그는 절대 경로가 필요합니다.");
   var path=Encoding.Unicode.GetBytes(Path.GetFullPath(logPath)+"\0");
   if(path.Length>2048)throw new ArgumentException("동기화 로그 경로가 너무 깁니다.");
   Buffer.BlockCopy(path,0,data,raw+0xd800,path.Length);
   // Cosmetic options/server address never affect compatibility. Simulation
   // options do: prevent mismatched rules from entering the same session.
   using(var sha=SHA256.Create()) {
    var digest=sha.ComputeHash(Encoding.UTF8.GetBytes(Profile(latency,selection,rice,observer)));
    Buffer.BlockCopy(digest,0,data,0x5f670,16);
   }
   return data;
  }
  public static string ReadReport(string path) {
   if(!File.Exists(path))return null;
   var b=File.ReadAllBytes(path);
   if(b.Length<256 || b.Length%256!=0)throw new InvalidDataException("동기화 기록이 불완전합니다: "+path);
   int o=b.Length-256;
   if(BitConverter.ToUInt32(b,o)!=0x314e5953 || BitConverter.ToUInt32(b,o+4)!=1)throw new InvalidDataException("동기화 기록 형식 오류");
   var s=new StringBuilder();uint reason=BitConverter.ToUInt32(b,o+8);
   s.AppendLine("분리 진행 보호로 대전을 중단했습니다. 자동 재동기화된 것은 아닙니다.");
   s.AppendLine("원인 경로: "+(reason==1?"응답 대기 후 상대 제외 요청":reason==2?"원본 동기화 검사값 불일치":reason==3?"상대의 강제 제외 통지 수신":"알 수 없음"));
   s.AppendLine("frame="+BitConverter.ToUInt32(b,o+12)+" localSlot="+BitConverter.ToUInt32(b,o+16)+" peerSlot="+BitConverter.ToInt32(b,o+20));
   s.AppendLine("rng="+BitConverter.ToUInt32(b,o+24).ToString("X8")+" rngCalls="+BitConverter.ToUInt32(b,o+28)+" requiredMask="+BitConverter.ToUInt32(b,o+32).ToString("X")+" readyMask="+BitConverter.ToUInt32(b,o+36).ToString("X"));
   for(int slot=0;slot<8;slot++) {
    s.Append("slot="+slot);
    string[] names={" state="," check="," seq="," read="," write="," pending="};
    for(int f=0;f<6;f++)s.Append(names[f]+BitConverter.ToInt32(b,o+64+slot*24+f*4));
    s.AppendLine();
   }
   return s.ToString();
  }
  static int Hex(string s){return int.Parse(s,NumberStyles.HexNumber,CultureInfo.InvariantCulture);}
  static byte[] Bytes(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=(byte)Hex(s.Substring(i*2,2));return b;}
 }
}
