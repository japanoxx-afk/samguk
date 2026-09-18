using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Windows.Forms;

static class LauncherUpdater {
 static readonly string[] Files={"SamKookLauncher.exe","LauncherUpdater.exe","GameMonitor.exe","cnc-ddraw.dll","cnc-ddraw-LICENSE.txt","README.md"};
 [STAThread] static int Main(string[] args) {
  string target=null,work=null; Process parent=null; bool exited=false;
  var replaced=new List<string>(); var backups=new Dictionary<string,string>();
  try {
   if(args.Length<4) throw new ArgumentException("업데이트 인수 오류");
   string zip=Path.GetFullPath(args[0]); target=Path.GetFullPath(args[2]);
   string ready=Path.GetFullPath(args[3]);
   parent=Process.GetProcessById(int.Parse(args[1]));
   work=Path.Combine(Path.GetTempPath(),"SamKook-stage-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
   using(var archive=ZipFile.OpenRead(zip)) {
    foreach(string name in Files) {
     var entry=archive.GetEntry(name);
     if(entry==null || entry.Length==0) throw new InvalidDataException("업데이트 필수 파일 누락: "+name);
     entry.ExtractToFile(Path.Combine(work,name));
     if(name.EndsWith(".exe")) using(var stream=File.OpenRead(Path.Combine(work,name)))
       if(stream.ReadByte()!=77 || stream.ReadByte()!=90) throw new InvalidDataException("실행 파일 형식 오류: "+name);
    }
   }
   string probe=Path.Combine(target,".update-probe-"+Guid.NewGuid().ToString("N"));
   using(var f=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)) { }
   foreach(string name in Files) {
    string dest=Path.Combine(target,name);
    if(!File.Exists(dest)) continue;
    if(name=="GameMonitor.exe" || name=="LauncherUpdater.exe" || name.EndsWith(".dll")) using(var f=new FileStream(dest,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
    string backup=Path.Combine(work,name+".bak"); File.Copy(dest,backup); backups[name]=backup;
   }
   File.WriteAllText(ready,"READY");
   if(!parent.WaitForExit(30000)) throw new IOException("기존 런처가 종료되지 않아 업데이트를 취소했습니다.");
   exited=true;
   foreach(string name in Files) {
    replaced.Add(name); File.Copy(Path.Combine(work,name),Path.Combine(target,name),true);
   }
   Process.Start(new ProcessStartInfo { FileName=Path.Combine(target,"SamKookLauncher.exe"),WorkingDirectory=target,UseShellExecute=true });
   File.AppendAllText(Path.Combine(target,"update.log"),DateTime.Now.ToString("s")+" 업데이트 교체 및 재시작 요청 성공"+Environment.NewLine);
   return 0;
  } catch(Exception ex) {
   string detail=ex.ToString();
   for(int i=replaced.Count-1;i>=0;i--) try {
    string name=replaced[i],backup;
    if(backups.TryGetValue(name,out backup)) File.Copy(backup,Path.Combine(target,name),true);
    else File.Delete(Path.Combine(target,name));
   } catch(Exception rollback) { detail+="\n복구 실패: "+rollback.Message; }
   try { File.AppendAllText(Path.Combine(target??Path.GetTempPath(),"update.log"),DateTime.Now.ToString("s")+" "+detail+Environment.NewLine); } catch { }
   if(exited) try { Process.Start(new ProcessStartInfo { FileName=Path.Combine(target,"SamKookLauncher.exe"),WorkingDirectory=target,UseShellExecute=true }); } catch { }
   if(args.Length<5 || args[4]!="--silent") MessageBox.Show("업데이트 실패. 기존 파일 복구를 시도했습니다.\n"+ex.Message+"\n자세한 내용: update.log","런처 업데이트",MessageBoxButtons.OK,MessageBoxIcon.Error);
   return 1;
  } finally { if(parent!=null)parent.Dispose(); }
 }
}
