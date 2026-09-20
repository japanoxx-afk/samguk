using System;
using System.IO;
using System.Text;

namespace SamKookFreeNet {
  // Applied last, after exact-build validation by QueuePatch. No wire changes.
  static class NetworkLifecyclePatch {
    public static byte[] Apply(byte[] input) {
      int pe=BitConverter.ToInt32(input,0x3c),opt=pe+24;
      int h=opt+BitConverter.ToUInt16(input,pe+20)+(BitConverter.ToUInt16(input,pe+6)-1)*40;
      int raw=BitConverter.ToInt32(input,h+20),rva=BitConverter.ToInt32(input,h+12),size=BitConverter.ToInt32(input,h+8);
      if(Encoding.ASCII.GetString(input,h,6)!=".fnfix" || (size!=512 && size!=65536) ||
         BitConverter.ToInt32(input,opt+28)+rva!=0x630000 || input.Length>raw+65536)
        throw new InvalidDataException("통신 수명 패치 공간 검증 실패");
      var data=new byte[raw+65536];Buffer.BlockCopy(input,0,data,0,input.Length);
      for(int i=49152;i<49668;i++)if(data[raw+i]!=0)throw new InvalidDataException("통신 수명 패치 공간 충돌");
      // CoInitialize: count S_OK and S_FALSE, not failed HRESULTs; retain HRESULT.
      Copy(data,raw+0xc000,"ff742404ff15d0f2450085c07806ff0500c16300c20400");
      // CoUninitialize only for initialization owned by this native game path.
      Copy(data,raw+0xc020,"833d00c1630000740cff0d00c16300ff25d8f24500c3");
      // Take-and-clear the lobby pointer BEFORE Release, including re-entry.
      Copy(data,raw+0xc050,"31c0870584306100c3");
      Hook(data,0x499fb,"ff15d0f24500",0x63c000);
      Hook(data,0x49c9b,"ff15d8f24500",0x63c020);
      Hook(data,0x49c7a,"a184306100",0x63c050);
      Put(data,h+8,65536);Put(data,h+16,65536);
      int align=BitConverter.ToInt32(input,opt+32);
      Put(data,opt+56,((rva+65536+align-1)/align)*align);Put(data,opt+64,0);
      return data;
    }
    static byte[] Bytes(string s){var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(i*2,2),16);return b;}
    static void Copy(byte[] b,int p,string s){var v=Bytes(s);Buffer.BlockCopy(v,0,b,p,v.Length);}
    static void Hook(byte[] b,int p,string s,int target){
      var expected=Bytes(s);for(int i=0;i<expected.Length;i++)if(b[p+i]!=expected[i])throw new InvalidDataException("통신 수명 명령어 검증 실패");
      b[p]=0xe8;Put(b,p+1,target-(0x400000+p+5));for(int i=5;i<expected.Length;i++)b[p+i]=0x90;
    }
    static void Put(byte[] b,int p,int v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,p,4);}
  }
}
