using System;
using System.IO;
using System.Reflection;
static class RuntimeTests {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static int Main(string[] args){
  var root=AppDomain.CurrentDomain.BaseDirectory;
  var asm=Assembly.LoadFrom(Path.Combine(root,"SamKookLauncher.exe"));
  var network=asm.GetType("SamKookFreeNet.NetworkSetup");
  foreach(string value in new[]{"", "bad IP", "::1", "26.157.67.215", "127.0.0.1"}) {
   object[] addressArgs={value,false};string address=(string)network.GetMethod("GameAddress").Invoke(null,addressArgs);
   bool valid=value=="26.157.67.215" || value=="127.0.0.1";
   Check((bool)addressArgs[1]==valid && address==(valid?value:"127.0.0.1"),"Offline launch address fallback failed");
  }
  var candidates=new[]{System.Net.IPAddress.Parse("26.157.67.215"),System.Net.IPAddress.Parse("192.168.0.3"),System.Net.IPAddress.Parse("26.157.67.215"),System.Net.IPAddress.Parse("25.1.2.3"),System.Net.IPAddress.IPv6Loopback,System.Net.IPAddress.Parse("26.1.2.3")};
  var filtered=(string[])network.GetMethod("FilterRadminAddresses").Invoke(null,new object[]{candidates});
  Check(filtered.Length==2 && filtered[0]=="26.1.2.3" && filtered[1]=="26.157.67.215","Radmin filtering/deduplication failed");
  string prompt=(string)network.GetMethod("ServerPrompt").Invoke(null,new object[]{filtered});
  Check(prompt.Contains("당신이 서버가 맞나요?") && prompt.Contains(filtered[0]) && prompt.Contains(filtered[1]),"Server confirmation missing addresses");
  prompt=(string)network.GetMethod("ServerPrompt").Invoke(null,new object[]{new string[0]});
  Check(prompt.Contains("찾지 못했습니다"),"Missing VPN not explained");
  Console.WriteLine("PASS offline address fallback, Radmin filtering, multiple/no-address prompts");
  var patch=asm.GetType("SamKookFreeNet.QueuePatch");
  var original=File.ReadAllBytes(args[0]);
  var data=(byte[])patch.GetMethod("ApplyAddress").Invoke(null,new object[]{original,"26.157.67.215"});
  patch.GetMethod("ReduceSenderSpin").Invoke(null,new object[]{data});
  patch.GetMethod("ReduceMultiplayerLatency").Invoke(null,new object[]{data});
  Check(data[0x4a1a0]==0xe9,"Spin loop not hooked");
  Check(original[0x4a1a0]==0xa1,"Source modified");
  Check(BitConverter.ToInt32(data,0x42f6c)==3 && BitConverter.ToInt32(data,0x4300c)==3,"Multiplayer cadence not reduced");
  Check(data[0x65802]==0 && data[0x65804]==1 && data[0x65806]==2,"Multiplayer buffers not reduced");
  Check(BitConverter.ToInt32(original,0x42f6c)==6 && original[0x65806]==6,"Original multiplayer logic modified");
  File.WriteAllBytes(Path.Combine(root,"performance-test.exe"),data);
  var lifecycle=asm.GetType("SamKookFreeNet.NetworkLifecyclePatch").GetMethod("Apply");
  var safeNetwork=(byte[])lifecycle.Invoke(null,new object[]{data});
  Check(data[0x499fb]==0xff && safeNetwork[0x499fb]==0xe8,"Lifecycle input preservation failed");
  File.WriteAllBytes(Path.Combine(root,"network-test.exe"),safeNetwork);
  var badNetwork=(byte[])data.Clone();badNetwork[0x49c7a]^=1;
  bool invalidNetwork=false;
  try{lifecycle.Invoke(null,new object[]{badNetwork});}catch(TargetInvocationException ex){invalidNetwork=ex.InnerException is InvalidDataException;}
  Check(invalidNetwork,"Unknown lifecycle instructions accepted");
  invalidNetwork=false;
  try{lifecycle.Invoke(null,new object[]{safeNetwork});}catch(TargetInvocationException ex){invalidNetwork=ex.InnerException is InvalidDataException;}
  Check(invalidNetwork,"Duplicate lifecycle patch accepted");
  var expanded=(byte[])asm.GetType("SamKookFreeNet.SelectionPatch").GetMethod("Apply").Invoke(null,new object[]{data});
  File.WriteAllBytes(Path.Combine(root,"selection36-test.exe"),expanded);
  Check(BitConverter.ToUInt16(expanded,0x44556)==36,"Drag selection bound not expanded");
  Check(BitConverter.ToUInt16(data,0x44556)==12,"Selection patch modified input");
  Check(expanded[0x1a24b]==12 && expanded[0x3425d]==12,"Portrait bounds must stay at twelve");
  bool rejected=false;var changed=(byte[])data.Clone();changed[0x44556]=13;
  try{asm.GetType("SamKookFreeNet.SelectionPatch").GetMethod("Apply").Invoke(null,new object[]{changed});}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}
  Check(rejected,"Unknown selection instructions accepted");
  var quality=asm.GetType("SamKookFreeNet.GameQualityPatch").GetMethod("Apply");
  foreach(bool selection in new[]{false,true})foreach(bool rally in new[]{false,true})foreach(bool timer in new[]{false,true}) {
   var source=selection?expanded:data;
   var result=(byte[])quality.Invoke(null,new object[]{source,rally,timer});
   Check(source[0x333d5]==0xe8 && source[0x42d1b]==0xa1,"Quality patch modified input");
   Check((result[0x42d1b]==0xe9)==timer,"Timer toggle incorrect");
   Check((BitConverter.ToInt32(result,0x333d6)!=0x1496)==rally,"Rally toggle incorrect");
   File.WriteAllBytes(Path.Combine(root,"quality-"+(selection?"36":"12")+"-"+(rally?"rally":"off")+"-"+(timer?"timer":"off")+".exe"),result);
   var rice=(byte[])asm.GetType("SamKookFreeNet.GameQualityPatch").GetMethod("ApplyRiceRally").Invoke(null,new object[]{result});
   Check(rice[0x12539]==0xe9 && result[0x12539]==0x8b,"Rice rally hook or preservation failed");
   if(rally && timer)File.WriteAllBytes(Path.Combine(root,"rice-"+(selection?"36":"12")+".exe"),rice);
   var observer=(byte[])asm.GetType("SamKookFreeNet.ObserverPatch").GetMethod("Apply").Invoke(null,new object[]{rice});
   Check(observer[0x3a210]==0xe9 && rice[0x3a210]==0x53,"Observer receive gate or source preservation failed");
   Check(observer[0x47ad5]==0xe9 && rice[0x47ad5]==0x0f,"Observer empty-army fallback hook missing or source modified");
   observer=(byte[])lifecycle.Invoke(null,new object[]{observer});
   if(rally && timer)File.WriteAllBytes(Path.Combine(root,"observer-"+(selection?"36":"12")+".exe"),observer);
  }
  changed=(byte[])data.Clone();changed[0x333d6]^=1;rejected=false;
  try{quality.Invoke(null,new object[]{changed,true,true});}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}
  Check(rejected,"Unknown rally instructions accepted");
  changed=(byte[])data.Clone();changed[0x12539]^=1;rejected=false;
  try{asm.GetType("SamKookFreeNet.GameQualityPatch").GetMethod("ApplyRiceRally").Invoke(null,new object[]{changed});}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}
  Check(rejected,"Unknown production instructions accepted");
  changed=(byte[])data.Clone();changed[0x3a210]^=1;rejected=false;
  try{asm.GetType("SamKookFreeNet.ObserverPatch").GetMethod("Apply").Invoke(null,new object[]{changed});}catch(TargetInvocationException ex){rejected=ex.InnerException is InvalidDataException;}
  Check(rejected,"Unknown observer instructions accepted");
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
  var preview=asm.GetType("SamKookFreeNet.MapPreview");int maps=0;
  string mission=Path.Combine(Path.GetDirectoryName(args[0]),"Mission");
  foreach(string file in Directory.GetFiles(mission,"*.skm")) {
   object[] call={file,0,0};var bitmap=(IDisposable)preview.GetMethod("Render").Invoke(null,call);
   Check((int)call[1]>=16 && (int)call[2]>=16,"Invalid map dimensions");bitmap.Dispose();maps++;
  }
  Check(maps>0,"No map previews tested");
  Console.WriteLine("PASS "+maps+" map previews, display modes, original preservation and multiplayer patches");return 0;
 }
}
