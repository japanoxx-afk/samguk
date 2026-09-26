using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace SamKookFreeNet {
  static class MapStarts {
    internal sealed class Start { public int Slot,X,Y; }
    internal static Start[] Read(byte[] data) {
      if(data.Length<9 || data[0]!='P' || data[1]!='3' || data[2]!='2' || data[3]!='M')throw new InvalidDataException("잘못된 맵 헤더");
      int w=BitConverter.ToUInt16(data,5),h=BitConverter.ToUInt16(data,7);
      long table=9L+2L*w*h*(1+data[4])+56;
      if(w<16 || h<16 || w>512 || h>512 || table+60000>data.Length)throw new InvalidDataException("시작 위치 데이터가 손상되었습니다.");
      var starts=new List<Start>();
      for(int i=0;i<3000;i++) {
        int at=(int)table+i*20;
        if(BitConverter.ToInt16(data,at)!=3 || BitConverter.ToInt16(data,at+2)==0)continue;
        int slot=BitConverter.ToInt16(data,at+4),x=BitConverter.ToInt16(data,at+12),y=BitConverter.ToInt16(data,at+14);
        if(slot<0 || slot>7 || x<0 || x>=w || y<0 || y>=h)continue;
        starts.Add(new Start { Slot=slot,X=x,Y=y });
      }
      return starts.ToArray();
    }
    // Draw labels at display resolution, retaining the native terrain pixels.
    internal static Bitmap Overlay(Bitmap terrain,Start[] starts,Func<Start,PointF> position) {
      float scale=544f/Math.Max(terrain.Width,terrain.Height);
      var result=new Bitmap((int)(terrain.Width*scale),(int)(terrain.Height*scale));
      using(var g=Graphics.FromImage(result))using(var font=new Font("Arial",12,FontStyle.Bold))
      using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center }) {
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        g.DrawImage(terrain,new Rectangle(0,0,result.Width,result.Height));g.SmoothingMode=SmoothingMode.AntiAlias;
        foreach(var start in starts) {
          PointF p=position(start);float x=p.X*result.Width/terrain.Width,y=p.Y*result.Height/terrain.Height;
          // Keep the label visible at edges; the cross marks the precise position.
          g.DrawLine(Pens.Yellow,x-7,y,x+7,y);g.DrawLine(Pens.Yellow,x,y-7,x,y+7);
          float cx=Math.Max(13,Math.Min(result.Width-13,x)),cy=Math.Max(13,Math.Min(result.Height-13,y));
          var rect=new RectangleF(cx-12,cy-12,24,24);
          g.FillEllipse(Brushes.Black,rect);g.DrawEllipse(Pens.Yellow,rect);
          g.DrawString((start.Slot+1).ToString(),font,Brushes.Yellow,rect,format);
        }
      }
      return result;
    }
  }
}
