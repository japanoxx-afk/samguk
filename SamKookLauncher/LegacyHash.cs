using System;
namespace SamKookFreeNet {
  // Exact algorithm at SamKook.exe 0x401c00. Not standard SHA-1.
  public static class LegacyHash {
    static uint Rol(uint x,int n) { n&=31; return (x<<n)|(x>>((32-n)&31)); }
    public static byte[] Compute(byte[] input) {
      uint[] h={0x67452301,0xefcdab89,0x98badcfe,0x10325476,0xc3d2e1f0};
      unchecked {
        for(int offset=0;offset<input.Length;offset+=64) {
          var w=new uint[80];
          for(int j=0;j<64 && offset+j<input.Length;j++) w[j/4]|=(uint)input[offset+j]<<((j%4)*8);
          for(int j=16;j<80;j++) w[j]=Rol(1,(int)(w[j-16]^w[j-14]^w[j-3]^w[j-8]));
          uint a=h[0],b=h[1],c=h[2],d=h[3],e=h[4];
          for(int j=0;j<80;j++) {
            uint f,k;
            if(j<20){f=(b&c)|(~b&d);k=0x5a827999;}
            else if(j<40){f=b^c^d;k=0x6ed9eba1;}
            else if(j<60){f=(b&c)|(b&d)|(c&d);k=0x8f1bbcdc;}
            else {f=b^c^d;k=0xca62c1d6;}
            uint t=Rol(a,5)+f+e+k+w[j]; e=d;d=c;c=Rol(b,30);b=a;a=t;
          }
          h[0]+=a;h[1]+=b;h[2]+=c;h[3]+=d;h[4]+=e;
        }
      }
      var output=new byte[20]; for(int i=0;i<5;i++)Buffer.BlockCopy(BitConverter.GetBytes(h[i]),0,output,i*4,4);
      return output;
    }
  }
}
