using System;
using System.IO;
using System.Reflection;
static class RuntimeTests {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static int Main(string[] args){
  var root=AppDomain.CurrentDomain.BaseDirectory;
  var asm=Assembly.LoadFrom(Path.Combine(root,"SamKookLauncher.exe"));
  var patch=asm.GetType("SamKookFreeNet.QueuePatch");
  var original=File.ReadAllBytes(args[0]);
  var data=(byte[])patch.GetMethod("ApplyAddress").Invoke(null,new object[]{original,"26.157.67.215"});
  patch.GetMethod("ReduceSenderSpin").Invoke(null,new object[]{data});
  Check(data[0x4a1a0]==0xe9,"Spin loop not hooked");
  Check(original[0x4a1a0]==0xa1,"Source modified");
  File.WriteAllBytes(Path.Combine(root,"performance-test.exe"),data);
  var options=asm.GetType("SamKookFreeNet.GameOptions");
  foreach(int mode in new[]{1,2})foreach(bool wide in new[]{true,false}) {
   var config=(string)options.GetMethod("Config").Invoke(null,new object[]{mode,wide,1280,720});
   Check(config.Contains("aspect_ratio="+(wide?"16:9":"4:3")),"Aspect mismatch");
   Check(config.Contains("fullscreen="+(mode==2?"true":"false")),"Mode mismatch");
   Check(config.Contains("windowed=true\r\n") && config.Contains("toggle_borderless=true\r\n"),"Unsafe fullscreen toggle");
   Check(config.Contains("renderer=gdi\r\n") && config.Contains("minfps=-2\r\n") && config.Contains("fixchilds=2\r\n") && config.Contains("nonexclusive=true\r\n"),"Dialog rendering compatibility missing");
   Check(config.Contains("singlecpu=false") && config.Contains("maxgameticks=-1") && config.Contains("vsync=false"),"Latency settings wrong");
  }
  string runtime=Path.Combine(root,"display-test");Directory.CreateDirectory(runtime);
  options.GetMethod("Prepare").Invoke(null,new object[]{root,runtime,1,true,1280,720});
  Check(File.Exists(Path.Combine(runtime,"ddraw.dll")) && File.Exists(Path.Combine(runtime,"ddraw.ini")),"Graphics module missing");
  Console.WriteLine("PASS display modes, aspect options, graphics hash, source preservation and CPU patch generation");return 0;
 }
}
