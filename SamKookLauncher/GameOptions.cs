using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace SamKookFreeNet {
 static class GameOptions {
  public const string DdrawHash="85E0F7D530DFDA134793A57CB3E76B0287DCC96892EE57162DD68F47283B03A9";
  public static string Config(int mode,bool wide,int width,int height) {
   if(mode<1 || mode>2 || width<320 || height<240)throw new ArgumentException("화면 설정 오류");
   return "[ddraw]\r\nwidth="+width+"\r\nheight="+height+"\r\nwindowed=true\r\nfullscreen="+(mode==2?"true":"false")+
    "\r\nmaintas=true\r\naspect_ratio="+(wide?"16:9":"4:3")+
    "\r\nboxing=false\r\nrenderer=gdi\r\nshader=\r\nd3d9_filter=1\r\nvsync=false\r\nmaxfps=60\r\nmaxgameticks=-1\r\nsinglecpu=false\r\nadjmouse=true\r\nborder=true\r\nresizable=false\r\nsavesettings=0\r\nminfps=-2\r\ntoggle_borderless=true\r\nnonexclusive=true\r\nfixchilds=2\r\n";
  }
  public static void Prepare(string root,string runtime,int mode,bool wide,int width,int height) {
   if(mode==0)return;
   string source=Path.Combine(root,"cnc-ddraw.dll");
   using(var sha=SHA256.Create())using(var f=File.OpenRead(source))
    if(BitConverter.ToString(sha.ComputeHash(f)).Replace("-","")!=DdrawHash)throw new InvalidDataException("화면 모듈 검증 실패. 런처 ZIP 전체를 다시 설치하세요.");
   File.Copy(source,Path.Combine(runtime,"ddraw.dll"),true);
   File.WriteAllText(Path.Combine(runtime,"ddraw.ini"),Config(mode,wide,width,height),Encoding.ASCII);
  }
 }
}
