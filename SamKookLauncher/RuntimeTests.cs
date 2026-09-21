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
  var sync=asm.GetType("SamKookFreeNet.SyncSafetyPatch");
  var syncApply=sync.GetMethod("Apply");
  var profiles=new System.Collections.Generic.HashSet<string>();
  for(int flags=0;flags<16;flags++) {
   var guarded=(byte[])syncApply.Invoke(null,new object[]{safeNetwork,Path.Combine(root,"sync-test.bin"),(flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0});
   Check(guarded[0x39770]==0xe9 && safeNetwork[0x39770]==0x83,"Sync protection or input preservation failed");
   profiles.Add(BitConverter.ToString(guarded,0x5f670,16));
   if(flags==0)File.WriteAllBytes(Path.Combine(root,"sync-base.exe"),guarded);
  }
  Check(profiles.Count==16,"Different simulation settings can connect to the same session");
  var pathOnly=(byte[])syncApply.Invoke(null,new object[]{safeNetwork,Path.Combine(root,"다른 진단 경로.bin"),false,false,false,false});
  Check(profiles.Contains(BitConverter.ToString(pathOnly,0x5f670,16)),"Log path incorrectly affects network compatibility");
  var fixture=new byte[256];
  Buffer.BlockCopy(BitConverter.GetBytes(0x314e5953),0,fixture,0,4);
  Buffer.BlockCopy(BitConverter.GetBytes(1),0,fixture,4,4);
  Buffer.BlockCopy(BitConverter.GetBytes(2),0,fixture,8,4);
  Buffer.BlockCopy(BitConverter.GetBytes(3000),0,fixture,12,4);
  string fixturePath=Path.Combine(root,"sync-report-test.bin");File.WriteAllBytes(fixturePath,fixture);
  string report=(string)sync.GetMethod("ReadReport").Invoke(null,new object[]{fixturePath});
  Check(report.Contains("frame=3000") && report.Contains("검사값 불일치") && report.Contains("slot=7"),"Sync report decoding failed");
  Buffer.BlockCopy(BitConverter.GetBytes(3),0,fixture,4,4);
  Buffer.BlockCopy(BitConverter.GetBytes(5),0,fixture,8,4);
  Buffer.BlockCopy(BitConverter.GetBytes(0x11111111),0,fixture,40,4);
  Buffer.BlockCopy(BitConverter.GetBytes(0x22222222),0,fixture,48,4);
  Buffer.BlockCopy(BitConverter.GetBytes(0),0,fixture,52,4);
  Buffer.BlockCopy(BitConverter.GetBytes(1),0,fixture,56,4);
  Buffer.BlockCopy(BitConverter.GetBytes(2),0,fixture,60,4);
  File.WriteAllBytes(fixturePath,fixture);
  report=(string)sync.GetMethod("ReadReport").Invoke(null,new object[]{fixturePath});
  Check(report.Contains("유닛·건물 핵심 상태") && report.Contains("slotA=0") && report.Contains("slotB=1") && report.Contains("11111111") && report.Contains("22222222"),"Sectional sync report decoding failed");
  File.WriteAllBytes(fixturePath,new byte[12]);bool rejectedReport=false;
  try{sync.GetMethod("ReadReport").Invoke(null,new object[]{fixturePath});}catch(TargetInvocationException ex){rejectedReport=ex.InnerException is InvalidDataException;}
  Check(rejectedReport,"Truncated sync report accepted");
  var badSync=(byte[])safeNetwork.Clone();badSync[0x39770]^=1;bool rejectedSync=false;
  try{syncApply.Invoke(null,new object[]{badSync,Path.Combine(root,"sync-test.bin"),false,false,false,false});}catch(TargetInvocationException ex){rejectedSync=ex.InnerException is InvalidDataException;}
  Check(rejectedSync,"Unknown sync instructions accepted");
  Console.WriteLine("PASS split-session protection and 16 distinct simulation compatibility profiles");
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
   Check(observer[0x2d8f6]==0x6a && observer[0x2d8f7]==1 && rice[0x2d8f7]==0,"Host observer control not enabled or source modified");
   foreach(int offset in new[]{0x1cfa3,0x1cfd9,0x1d00c,0x1d077,0x18a70})
    Check(observer[offset]==0xe9 && rice[offset]==0x0f,"Observer inspection hook missing or source modified");
   observer=(byte[])lifecycle.Invoke(null,new object[]{observer});
   if(rally && timer)File.WriteAllBytes(Path.Combine(root,"observer-"+(selection?"36":"12")+".exe"),observer);
   var guardedObserver=(byte[])syncApply.Invoke(null,new object[]{observer,Path.Combine(root,"sync-test.bin"),true,selection,true,true});
   if(rally && timer)File.WriteAllBytes(Path.Combine(root,"sync-"+(selection?"36":"12")+".exe"),guardedObserver);
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
