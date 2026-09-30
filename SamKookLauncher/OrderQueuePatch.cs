using System;
using System.IO;
using System.Reflection;
using System.Text;
namespace SamKookFreeNet {
 static class OrderQueuePatch {
  public static byte[] Apply(byte[] input) {
   int pe=BitConverter.ToInt32(input,0x3c),opt=pe+24;
   int h=opt+BitConverter.ToUInt16(input,pe+20)+(BitConverter.ToUInt16(input,pe+6)-1)*40;
   int raw=BitConverter.ToInt32(input,h+20),rva=BitConverter.ToInt32(input,h+12);
   if(Encoding.ASCII.GetString(input,h,6)!=".fnfix" || BitConverter.ToInt32(input,h+8)!=65536 || rva!=0x230000)throw new InvalidDataException("예약 이동 패치 순서 오류");
   var data=new byte[raw+262144];Buffer.BlockCopy(input,0,data,0,input.Length);
   Put(data,h+8,262144);Put(data,h+16,262144);Put(data,opt+56,rva+262144);Put(data,opt+64,0);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("orders.manifest")) {
    if(stream==null)throw new InvalidDataException("예약 이동 코드 누락");
    using(var reader=new StreamReader(stream)) {
     string line;while((line=reader.ReadLine())!=null){
      if(line.Length==0 || line[0]=='#')continue;
      var f=line.Split(' ');int at=Convert.ToInt32(f[1],16);byte[] before,after;
      if(f[0]=="B") {after=Bytes(f[2]);before=new byte[after.Length];if(!((at>=0x10000 && at+after.Length<=0x11000)||(at>=0x2d000 && at+after.Length<=0x30000)))throw new InvalidDataException("예약 코드 범위 오류");at+=raw;}
      else if(f[0]=="H"){before=Bytes(f[2]);after=Bytes(f[3]);}else throw new InvalidDataException("예약 코드 형식 오류");
      if(before.Length!=after.Length || at<0 || at+before.Length>data.Length)throw new InvalidDataException("예약 코드 길이 오류");
      for(int i=0;i<before.Length;i++)if(data[at+i]!=before[i])throw new InvalidDataException("예약 코드 검증 실패: "+at.ToString("X"));
      Buffer.BlockCopy(after,0,data,at,after.Length);
     }
    }
   }
   return data;
  }
  static byte[] Bytes(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(i*2,2),16);return b;}
  static void Put(byte[] b,int at,int value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
 }
}
