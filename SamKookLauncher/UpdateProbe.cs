using System;
using System.IO;
using System.Threading;
static class UpdateProbe {
 static void Main(string[] args) {
  if(args.Length>0 && args[0]=="hold") { for(int i=0;i<300 && !File.Exists(args[1]);i++) Thread.Sleep(100); return; }
  if(args.Length>0 && args[0]=="args") { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"args.txt"),args[1]); return; }
  #if OLD
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"restarted.txt"),"old");
  #else
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"restarted.txt"),"new");
  #endif
 }
}
