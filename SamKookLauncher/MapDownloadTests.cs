using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
static class MapDownloadTests {
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
  static int Main(string[] args) {
    var asm=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SamKookLauncher.exe"));
    var maps=asm.GetType("SamKookFreeNet.MapDownloads");var install=maps.GetMethod("InstallWithDownload");
    var bytes=File.ReadAllBytes(args[0]);int count=0;
    Func<Task<byte[]>> download=()=>{count++;return Task.FromResult(bytes);};
    string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"map-tests-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);string exe=Path.Combine(root,"SamKook.exe");File.WriteAllText(exe,"test fixture");
    Func<string,Func<Task<byte[]>>,Task<string>> run=(path,fetch)=>(Task<string>)install.Invoke(null,new object[]{path,fetch});
    run(exe,download).GetAwaiter().GetResult();
    string dest=Path.Combine(root,"Mission","(N4) 삼한의 영광.skm");
    Check(File.Exists(dest) && new FileInfo(dest).Length==bytes.Length,"Map missing from selected game Mission");
    run(exe,download).GetAwaiter().GetResult();Check(count==1,"Already installed map downloaded again");
    File.WriteAllText(dest,"my edited map");bool rejected=false;
    try{run(exe,download).GetAwaiter().GetResult();}catch(IOException){rejected=true;}
    Check(rejected && File.ReadAllText(dest)=="my edited map" && count==1,"Existing map overwritten");
    File.Move(dest,dest+".preserved");var bad=(byte[])bytes.Clone();bad[100]^=1;rejected=false;
    try{run(exe,()=>Task.FromResult(bad)).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}
    Check(rejected && !File.Exists(dest) && Directory.GetFiles(Path.GetDirectoryName(dest),"*.tmp").Length==0,"Corrupt download installed");
    rejected=false;try{run(exe,()=>Task.FromResult(new byte[0])).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}
    Check(rejected && !File.Exists(dest),"Truncated download installed");
    rejected=false;try{run(Path.Combine(root,"missing.exe"),download).GetAwaiter().GetResult();}catch(FileNotFoundException){rejected=true;}
    Check(rejected && count==1,"Missing game not rejected before network");
    Task.WaitAll(run(exe,download),run(exe,download));Check(count==2,"Concurrent installs downloaded twice");
    object[] render={dest,0,0};((IDisposable)asm.GetType("SamKookFreeNet.MapPreview").GetMethod("Render").Invoke(null,render)).Dispose();
    Check((int)render[1]==128 && (int)render[2]==128,"New map dimensions incorrect");
    Console.WriteLine("PASS map integrity, Mission install, offline repeat, collision protection, corrupt/truncated rejection, missing game, concurrent install and preview");return 0;
  }
}
