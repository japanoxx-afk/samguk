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
    string hash=(string)maps.GetMethod("Digest").Invoke(null,new object[]{bytes});
    string row="samhan-glory.skm\t(N4) 삼한의 영광.skm\t"+hash+"\t"+bytes.Length;
    var catalog=maps.GetMethod("InstallCatalog");int fetched=0;
    Func<string,int,Task<byte[]>> catalogFetch=(file,size)=>{fetched++;return Task.FromResult(bytes);};
    Func<string,string> sync=text=>((Task<string>)catalog.Invoke(null,new object[]{exe,text,catalogFetch})).GetAwaiter().GetResult();
    sync(row);Check(fetched==0,"Catalog downloaded existing map");
    string added="second.skm\t새 맵.skm\t"+hash+"\t"+bytes.Length;
    sync(row+"\n"+added);Check(fetched==1 && File.Exists(Path.Combine(root,"Mission","새 맵.skm")),"New catalog map not installed");
    sync(row+"\n"+added);Check(fetched==1,"Catalog repeat downloaded again");
    foreach(string invalid in new[]{"../evil.skm\tx.skm\t"+hash+"\t"+bytes.Length,"x.skm\t../x.skm\t"+hash+"\t"+bytes.Length,"x.skm\tCON.skm\t"+hash+"\t"+bytes.Length,row+"\n"+row,row.Replace(hash,"bad"),""}) {
      rejected=false;try{sync(invalid);}catch(InvalidDataException){rejected=true;}
      Check(rejected,"Unsafe catalog accepted");
    }
    File.WriteAllText(Path.Combine(root,"Mission","새 맵.skm"),"custom map");
    string third="third.skm\t세 번째 맵.skm\t"+hash+"\t"+bytes.Length;
    string summary=sync(added+"\n"+third);
    Check(summary.Contains("보류 1개") && File.Exists(Path.Combine(root,"Mission","세 번째 맵.skm")),"One conflict prevented unrelated downloads");
    Console.WriteLine("PASS catalog discovery, future map addition without launcher update, repeat checks, unsafe names/duplicates and partial failure isolation");
    Console.WriteLine("PASS map integrity, Mission install, offline repeat, collision protection, corrupt/truncated rejection, missing game, concurrent install and preview");return 0;
  }
}
