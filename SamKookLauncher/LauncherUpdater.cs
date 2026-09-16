using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

static class LauncherUpdater {
  [STAThread] static int Main(string[] args) {
    try {
      if(args.Length!=3) return 2;
      var zip=Path.GetFullPath(args[0]); int pid=int.Parse(args[1]); var target=Path.GetFullPath(args[2]);
      try { Process.GetProcessById(pid).WaitForExit(30000); } catch { }
      Thread.Sleep(500);
      var temp=Path.Combine(Path.GetTempPath(),"SamKookLauncher-extract-"+Guid.NewGuid().ToString("N"));
      ZipFile.ExtractToDirectory(zip,temp);
      var extractedLauncher=Path.Combine(temp,"SamKookLauncher.exe");
      if(!File.Exists(extractedLauncher)) throw new InvalidDataException("업데이트 ZIP에 SamKookLauncher.exe가 없습니다.");
      foreach(var file in Directory.GetFiles(temp,"*",SearchOption.AllDirectories)) {
        var rel=file.Substring(temp.Length).TrimStart(Path.DirectorySeparatorChar); var dest=Path.Combine(target,rel);
        // A running executable cannot replace itself on Windows. The bundled
        // updater remains in place; launcher and supporting files are updated.
        if(string.Equals(rel,"LauncherUpdater.exe",StringComparison.OrdinalIgnoreCase)) continue;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(file,dest,true);
      }
      try { File.Delete(zip); Directory.Delete(temp,true); } catch { }
      var launcher=Path.Combine(target,"SamKookLauncher.exe"); if(File.Exists(launcher)) Process.Start(launcher);
      return 0;
    } catch(Exception ex) { File.WriteAllText(Path.Combine(Path.GetTempPath(),"SamKookLauncher-update-error.txt"),ex.ToString()); return 1; }
  }
}
