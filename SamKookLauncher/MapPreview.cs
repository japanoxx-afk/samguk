using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SamKookFreeNet {
  static class MapPreview {
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

    public static void Show(IWin32Window owner,string gameExe) {
      string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gameExe)),"Mission");
      if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Mission 폴더를 찾을 수 없습니다: "+folder);
      string[] files=Directory.GetFiles(folder,"*.skm");Array.Sort(files,StringComparer.CurrentCultureIgnoreCase);
      if(files.Length==0)throw new FileNotFoundException("Mission 폴더에 SKM 맵이 없습니다.");
      using(var dialog=new Form { Text="맵 미니맵 프리뷰",ClientSize=new Size(820,570),MinimumSize=new Size(700,500),StartPosition=FormStartPosition.CenterParent,Font=new Font("맑은 고딕",10F) }) {
        var list=new ListBox { Dock=DockStyle.Left,Width=290 };
        var image=new PictureBox { Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,20,22),SizeMode=PictureBoxSizeMode.Zoom };
        var info=new Label { Dock=DockStyle.Bottom,Height=52,Padding=new Padding(8),ForeColor=Color.DimGray,Text="SKM 지형 타일을 축소한 구조 프리뷰입니다. 실제 게임 미니맵의 색상·오브젝트와는 다를 수 있습니다." };
        foreach(string file in files)list.Items.Add(new Item(file));
        list.SelectedIndexChanged+=(s,e)=>{
          var selected=list.SelectedItem as Item;if(selected==null)return;
          try { int w,h;var next=Render(selected.Path,out w,out h);var old=image.Image;image.Image=next;if(old!=null)old.Dispose();info.Text=Path.GetFileName(selected.Path)+"  /  "+w+"×"+h+"  /  SKM 지형 구조 프리뷰"; }
          catch(Exception ex){info.Text="프리뷰 실패: "+ex.Message;}
        };
        dialog.FormClosed+=(s,e)=>{if(image.Image!=null)image.Image.Dispose();};
        dialog.Controls.Add(image);dialog.Controls.Add(info);dialog.Controls.Add(list);list.SelectedIndex=0;
        dialog.ShowDialog(owner);
      }
    }
    sealed class Item { public readonly string Path;public Item(string path){Path=path;}public override string ToString(){return System.IO.Path.GetFileNameWithoutExtension(Path);} }
  }
}
