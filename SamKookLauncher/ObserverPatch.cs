using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SamKookFreeNet {
  // Experimental opt-in protocol. All peers must enable it before launch.
  // Exact original SHA256 is enforced upstream by QueuePatch.ApplyAddress.
  static class ObserverPatch {
    public static byte[] Apply(byte[] input) {
      int pe=BitConverter.ToInt32(input,0x3c),opt=pe+24;
      int header=opt+BitConverter.ToUInt16(input,pe+20)+(BitConverter.ToUInt16(input,pe+6)-1)*40;
      int size=BitConverter.ToInt32(input,header+8);
      int raw=BitConverter.ToInt32(input,header+20),rva=BitConverter.ToInt32(input,header+12);
      if(Encoding.ASCII.GetString(input,header,6)!=".fnfix" || (size!=512 && size!=65536) ||
         BitConverter.ToInt32(input,opt+28)+rva!=0x630000 || input.Length>raw+65536)
        throw new InvalidDataException("관전 기능 패치 순서 또는 게임 주소 검증 실패");
      var data=new byte[raw+65536];Buffer.BlockCopy(input,0,data,0,input.Length);
      for(int i=20480;i<32768;i++)if(data[raw+i]!=0)throw new InvalidDataException("관전 코드 공간 충돌");
      for(int i=45056;i<47104;i++)if(data[raw+i]!=0)throw new InvalidDataException("관전 상태 공간 충돌");
      Put(data,header+8,65536);Put(data,header+16,65536);
      int align=BitConverter.ToInt32(input,opt+32);
      Put(data,opt+56,((rva+65536+align-1)/align)*align);Put(data,opt+64,0);
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("observer.manifest")) {
        if(stream==null)throw new InvalidDataException("관전 기능 패치 목록 누락");
        using(var reader=new StreamReader(stream)) {
          string line;while((line=reader.ReadLine())!=null) {
            if(line.Length==0 || line[0]=='#')continue;
            var f=line.Split(' ');int offset=Hex(f[1]);byte[] expected,replacement;
            if(f[0]=="B") {
              replacement=Bytes(f[2]);
              bool code=offset>=20480 && offset+replacement.Length<=32768;
              bool state=offset>=45056 && offset+replacement.Length<=46080;
              if(!code && !state)throw new InvalidDataException("관전 기능 공간 범위 오류");
              offset+=raw;expected=new byte[replacement.Length];
            } else if(f[0]=="H") { expected=Bytes(f[2]);replacement=Bytes(f[3]); }
            else throw new InvalidDataException("관전 기능 목록 형식 오류");
            if(expected.Length!=replacement.Length || offset<0 || offset>data.Length-expected.Length)
              throw new InvalidDataException("관전 기능 명령어 길이 오류");
            for(int i=0;i<expected.Length;i++)if(data[offset+i]!=expected[i])
              throw new InvalidDataException("관전 기능 명령어 검증 실패: "+offset.ToString("X"));
            Buffer.BlockCopy(replacement,0,data,offset,replacement.Length);
          }
        }
      }
      return data;
    }
    static int Hex(string s){return int.Parse(s,NumberStyles.HexNumber,CultureInfo.InvariantCulture);}
    static byte[] Bytes(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=(byte)Hex(s.Substring(i*2,2));return b;}
    static void Put(byte[] b,int o,int v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,4);}
  }
}
