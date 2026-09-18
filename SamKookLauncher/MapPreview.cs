using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace SamKookFreeNet {
  static class MapPreview {
    sealed class CrispPictureBox : PictureBox {
      protected override void OnPaint(PaintEventArgs e) {
        if(Image==null){base.OnPaint(e);return;}
        e.Graphics.Clear(BackColor);e.Graphics.InterpolationMode=InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode=PixelOffsetMode.Half;e.Graphics.SmoothingMode=SmoothingMode.None;
        float scale=Math.Min((float)ClientSize.Width/Image.Width,(float)ClientSize.Height/Image.Height);
        int w=Math.Max(1,(int)(Image.Width*scale)),h=Math.Max(1,(int)(Image.Height*scale));
        e.Graphics.DrawImage(Image,new Rectangle((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h),0,0,Image.Width,Image.Height,GraphicsUnit.Pixel);
      }
    }
    static readonly Color[] Terrain = {
      Color.FromArgb(62,91,48), Color.FromArgb(78,108,55),
      Color.FromArgb(105,116,62), Color.FromArgb(125,105,66),
      Color.FromArgb(91,76,54), Color.FromArgb(55,91,112),
      Color.FromArgb(47,105,123), Color.FromArgb(137,126,82),
      Color.FromArgb(82,111,76), Color.FromArgb(112,91,65),
      Color.FromArgb(74,84,52), Color.FromArgb(126,119,91)
    };

    public static Bitmap Render(string path,out int width,out int height) {
      byte[] data=File.ReadAllBytes(path);
      if(data.Length<9 || data[0]!='P' || data[1]!='3' || data[2]!='2' || data[3]!='M')
        throw new InvalidDataException("P32M 맵 파일이 아닙니다.");
      width=BitConverter.ToUInt16(data,5);height=BitConverter.ToUInt16(data,7);
      if(width<16 || height<16 || width>512 || height>512 || data.Length<9+width*height*2)
        throw new InvalidDataException("맵 크기 또는 지형 데이터가 손상되었습니다.");
      var image=new Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
        int tile=BitConverter.ToUInt16(data,9+(y*width+x)*2);
        Color baseColor=Terrain[(tile/32)%Terrain.Length];
        int delta=(tile%17)-8;
        image.SetPixel(x,y,Color.FromArgb(Clamp(baseColor.R+delta),Clamp(baseColor.G+delta),Clamp(baseColor.B+delta)));
      }
      return image;
    }
    static int Clamp(int value){return Math.Max(0,Math.Min(255,value));}

    static Bitmap RenderEditor(string game,string map,out string status) {
      string renderer=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MapPreviewRenderer.exe");
      if(!File.Exists(renderer) || !File.Exists(Path.Combine(game,"MapEditor.dll")))throw new FileNotFoundException("에디터 프리뷰 구성 파일이 없습니다.");
      string output=Path.Combine(Path.GetTempPath(),"SamKook-map-"+Guid.NewGuid().ToString("N")+".png");
      try {
        var start=new ProcessStartInfo { FileName=renderer,Arguments=Quote(game)+" "+Quote(map)+" "+Quote(output),WorkingDirectory=game,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true };
        using(var process=Process.Start(start)) {
          var errorRead=process.StandardError.ReadToEndAsync();
          if(!process.WaitForExit(10000)){try{process.Kill();}catch{}throw new TimeoutException("에디터 프리뷰 시간 초과");}
          string error=errorRead.GetAwaiter().GetResult();if(process.ExitCode!=0 || !File.Exists(output))throw new InvalidDataException(string.IsNullOrWhiteSpace(error)?"에디터 프리뷰 실패":error.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)[0]);
        }
        using(var loaded=Image.FromFile(output)){status="Map.exe 에디터 렌더링 136×136 / 최근린 확대";return new Bitmap(loaded);}
      } finally {try{if(File.Exists(output))File.Delete(output);}catch{}}
    }
    static string Quote(string value){if(value.IndexOf('\"')>=0)throw new ArgumentException("경로에 따옴표를 사용할 수 없습니다.");return "\""+value+"\"";}

    public static void Show(IWin32Window owner,string gameExe) {
      string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe)),"Mission");
      string game=Path.GetDirectoryName(Path.GetFullPath(gameExe));
      if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Mission 폴더를 찾을 수 없습니다: "+folder);
      string[] files=Directory.GetFiles(folder,"*.skm");Array.Sort(files,StringComparer.CurrentCultureIgnoreCase);
      if(files.Length==0)throw new FileNotFoundException("Mission 폴더에 SKM 맵이 없습니다.");
      using(var dialog=new Form { Text="맵 미니맵 프리뷰",ClientSize=new Size(820,570),MinimumSize=new Size(700,500),StartPosition=FormStartPosition.CenterParent,Font=new Font("맑은 고딕",10F) }) {
        var list=new ListBox { Dock=DockStyle.Left,Width=290 };
        var image=new CrispPictureBox { Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,20,22) };
        var info=new Label { Dock=DockStyle.Bottom,Height=72,Padding=new Padding(8),ForeColor=Color.DimGray,Text="에디터 미니맵을 불러옵니다. 원본 미니맵은 136×136이며 픽셀을 유지해 확대합니다." };
        foreach(string file in files)list.Items.Add(new Item(file));
        int request=0;
        list.SelectedIndexChanged+=async (s,e)=>{
          var selected=list.SelectedItem as Item;if(selected==null)return;
          int current=++request;list.Enabled=false;info.Text="미니맵 불러오는 중…";
          try {
            var result=await Task.Run(()=>{
              int w,h;Render(selected.Path,out w,out h).Dispose();string status;Bitmap next;
              try {next=RenderEditor(game,selected.Path,out status);} catch(Exception editorError){next=Render(selected.Path,out w,out h);status="구조 프리뷰(에디터 사용 불가: "+editorError.Message+")";}
              return new PreviewResult { Image=next,Text=Path.GetFileName(selected.Path)+"  /  "+w+"×"+h+"  /  "+status };
            });
            if(dialog.IsDisposed || current!=request){result.Image.Dispose();return;}
            var old=image.Image;image.Image=result.Image;if(old!=null)old.Dispose();info.Text=result.Text;
          }
          catch(Exception ex){if(!dialog.IsDisposed)info.Text="프리뷰 실패: "+ex.Message;}
          finally {if(!dialog.IsDisposed && current==request)list.Enabled=true;}
        };
        dialog.FormClosed+=(s,e)=>{if(image.Image!=null)image.Image.Dispose();};
        dialog.Controls.Add(image);dialog.Controls.Add(info);dialog.Controls.Add(list);list.SelectedIndex=0;
        dialog.ShowDialog(owner);
      }
    }
    sealed class PreviewResult {public Bitmap Image;public string Text;}
    sealed class Item { public readonly string Path;public Item(string path){Path=path;}public override string ToString(){return System.IO.Path.GetFileNameWithoutExtension(Path);} }
  }
}
