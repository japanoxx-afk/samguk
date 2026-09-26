using System;
using System.IO;
using System.Drawing;
using SamKookFreeNet;
static class MapStartsTests {
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
  static void Put(byte[] b,int at,short value){Array.Copy(BitConverter.GetBytes(value),0,b,at,2);}
  static int Main(string[] args) {
    var bytes=File.ReadAllBytes(args[0]);var starts=MapStarts.Read(bytes);
    Check(starts.Length==4,"Fortress start count");
    int[] x={7,45,117,75},y={79,5,49,118};
    for(int i=0;i<4;i++)Check(starts[i].Slot==i && starts[i].X==x[i] && starts[i].Y==y[i],"Fortress coordinates");
    int table=9+2*128*128*(1+bytes[4])+56;
    Array.Clear(bytes,table,60000);Check(MapStarts.Read(bytes).Length==0,"No starts");
    Put(bytes,table,3);Put(bytes,table+2,1);Put(bytes,table+4,7);Put(bytes,table+12,127);Put(bytes,table+14,0);
    Check(MapStarts.Read(bytes).Length==1,"Edge coordinates and slot 8");
    Put(bytes,table+12,128);Check(MapStarts.Read(bytes).Length==0,"Out of range");
    Put(bytes,table+12,7);Put(bytes,table+2,0);Check(MapStarts.Read(bytes).Length==0,"Inactive object");
    bool rejected=false;try{MapStarts.Read(new byte[9]);}catch(InvalidDataException){rejected=true;}Check(rejected,"Bad header");
    rejected=false;try{MapStarts.Read(File.ReadAllBytes(args[0]).SubArray(100));}catch(InvalidDataException){rejected=true;}Check(rejected,"Truncation");
    using(var terrain=new Bitmap(128,64))using(var result=MapStarts.Overlay(terrain,starts,s=>new PointF(s.X,s.Y)))Check(result.Width==544 && result.Height==272,"Aspect ratio");
    int count=0;foreach(var file in Directory.GetFiles(args[1],"*.skm")){MapStarts.Read(File.ReadAllBytes(file));count++;}
    Console.WriteLine("PASS start coordinates, bounds, disabled records, corrupt files, aspect ratio; "+count+" real maps");return 0;
  }
  static byte[] SubArray(this byte[] data,int size){var b=new byte[size];Array.Copy(data,b,size);return b;}
}
