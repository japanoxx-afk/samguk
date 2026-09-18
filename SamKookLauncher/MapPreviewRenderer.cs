using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Windows.Forms;

static class MapPreviewRenderer {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetDllDirectory(string path);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
  [DllImport("MapEditor.dll",EntryPoint="_initMapEditor@0",CallingConvention=CallingConvention.StdCall,ExactSpelling=true)] static extern void Init();
  [DllImport("MapEditor.dll",EntryPoint="_freeMapEditor@0",CallingConvention=CallingConvention.StdCall,ExactSpelling=true)] static extern void FreeEditor();
  [DllImport("MapEditor.dll",EntryPoint="_resetMapEditor@8",CallingConvention=CallingConvention.StdCall,ExactSpelling=true)] static extern void Reset(int width,int height);
  [DllImport("MapEditor.dll",EntryPoint="_loadNewMap@4",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Ansi,ExactSpelling=true)] static extern void LoadMap([MarshalAs(UnmanagedType.LPStr)] string path);
  [DllImport("MapEditor.dll",EntryPoint="_freeNewMap@0",CallingConvention=CallingConvention.StdCall,ExactSpelling=true)] static extern void FreeMap();
  [DllImport("MapEditor.dll",EntryPoint="_refreshSmallMap@0",CallingConvention=CallingConvention.StdCall,ExactSpelling=true)] static extern void Refresh();
  [DllImport("MapEditor.dll",EntryPoint="_drawSmallMap@4",CallingConvention=CallingConvention.StdCall,ExactSpelling=true)] static extern void Draw(IntPtr hwnd);

  sealed class RenderWindow : NativeWindow,IDisposable {
    public RenderWindow(){CreateHandle(new CreateParams { Caption="SamKook private preview surface",Width=136,Height=136,Style=unchecked((int)0x80000000) });}
    public void Dispose(){DestroyHandle();}
  }

  [STAThread] static int Main(string[] args) {
    bool initialized=false,loaded=false;
    try {
      if(args.Length!=3)throw new ArgumentException("usage: MapPreviewRenderer.exe <game-dir> <map.skm> <output.png>");
      string game=Path.GetFullPath(args[0]),map=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
      string editor=Path.Combine(game,"MapEditor.dll");
      if(!File.Exists(editor))throw new FileNotFoundException("맵 에디터 DLL이 없습니다.");
      using(var sha=SHA256.Create())using(var input=File.OpenRead(editor))
        if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","")!="00E6CB1E2C7882D56E24C89402970963D8264C7D77F89795DFFCF35F982851C7")
          throw new InvalidDataException("이 MapEditor.dll 버전은 프리뷰가 지원하지 않습니다.");
      if(!File.Exists(map))throw new FileNotFoundException("맵 파일이 없습니다.",map);
      byte[] header=File.ReadAllBytes(map);
      if(header.Length<9 || Encoding.ASCII.GetString(header,0,4)!="P32M")throw new InvalidDataException("P32M 맵이 아닙니다.");
      int width=BitConverter.ToUInt16(header,5),height=BitConverter.ToUInt16(header,7);
      if(width<16 || height<16 || width>232 || height>232 || header.Length<9+width*height*2)throw new InvalidDataException("에디터 지원 범위를 벗어난 맵입니다.");
      if(Encoding.Default.GetString(Encoding.Default.GetBytes(map))!=map)throw new InvalidDataException("에디터가 표현할 수 없는 맵 경로입니다.");
      SetDllDirectory(game);Directory.SetCurrentDirectory(game);
      Init();initialized=true;
      IntPtr module=GetModuleHandle("MapEditor.dll");
      byte[] root=Encoding.Default.GetBytes(game+"\0");
      if(root.Length>99)throw new PathTooLongException("게임 폴더 경로가 에디터 제한을 넘었습니다.");
      Marshal.Copy(root,0,IntPtr.Add(module,0x234d8),root.Length);Marshal.Copy(root,0,IntPtr.Add(module,0x32494),root.Length);
      LoadMap(map);loaded=true;
      // Loading the season fills its RGB6 palette. Reset must run AFTER it:
      // reset converts RGB6 -> RGBQUAD and builds the DIB color table.
      Reset(640,480);Refresh();
      IntPtr mapState=Marshal.ReadIntPtr(IntPtr.Add(module,0x5075c));
      if(mapState==IntPtr.Zero)throw new InvalidDataException("에디터가 SKM 맵을 열지 못했습니다.");
      using(var bitmap=new Bitmap(136,136,PixelFormat.Format24bppRgb)) {
        // The editor API expects an HWND, not an HDC. Never draw on the desktop.
        using(var window=new RenderWindow())Draw(window.Handle);
        var raw=new byte[136*136];Marshal.Copy(IntPtr.Add(module,0x54fc8),raw,0,raw.Length);
        bool any=false;for(int i=0;i<raw.Length;i++)if(raw[i]!=0){any=true;break;}
        if(!any)throw new InvalidDataException("에디터가 빈 미니맵을 반환했습니다.");
        IntPtr palette=Marshal.ReadIntPtr(IntPtr.Add(module,0x5980c));
        if(palette==IntPtr.Zero)throw new InvalidDataException("에디터 팔레트가 없습니다.");
        {
          for(int y=0;y<136;y++)for(int x=0;x<136;x++) {
            int color=raw[y*136+x],offset=color*4;
            int b=Marshal.ReadByte(palette,offset),g=Marshal.ReadByte(palette,offset+1),r=Marshal.ReadByte(palette,offset+2);
            bitmap.SetPixel(x,y,Color.FromArgb(r,g,b));
          }
        }
        bitmap.Save(output,ImageFormat.Png);
      }
      return 0;
    } catch(Exception ex) { try { Console.Error.WriteLine(ex.ToString()); } catch {} return 2; }
    finally { if(loaded)try{FreeMap();}catch{} if(initialized)try{FreeEditor();}catch{} }
  }
}
