using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// x86 helper: only the launched game is debugged. First-chance exceptions
// remain available to the game; capture only unhandled (second-chance) faults.
static class GameMonitor {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
  public int cb; public string reserved,desktop,title;
  public int x,y,cx,cy,charsX,charsY,fill,flags; public short show,reserved2;
  public IntPtr reservedPtr,input,output,error;
 }
 [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process,thread; public int pid,tid; }
 [StructLayout(LayoutKind.Explicit,Size=96)] struct DebugEvent {
  [FieldOffset(0)] public int kind;
  [FieldOffset(4)] public int pid;
  [FieldOffset(8)] public int tid;
  [FieldOffset(12)] public uint code;
  [FieldOffset(12)] public IntPtr file;
  [FieldOffset(24)] public IntPtr address;
  [FieldOffset(92)] public int firstChance;
 }
 [StructLayout(LayoutKind.Sequential)] struct ExceptionInfo { public int thread; public IntPtr pointers; public int clientPointers; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref Startup startup,out ProcessInfo info);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool WaitForDebugEvent(out DebugEvent e,uint ms);
 [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(int pid,int tid,uint status);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access,bool inherit,int tid);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetThreadContext(IntPtr thread,IntPtr context);
 [DllImport("dbghelp.dll",SetLastError=true)] static extern bool MiniDumpWriteDump(IntPtr process,int pid,IntPtr file,uint type,ref ExceptionInfo exception,IntPtr user,IntPtr callback);
 static string folder;
 static void Log(string s) { File.AppendAllText(Path.Combine(folder,"crash-monitor.log"),DateTime.Now.ToString("s")+" "+s+Environment.NewLine); }
 static int Main(string[] args) {
  if(args.Length!=3) return 2;
  folder=args[2]; Directory.CreateDirectory(folder);
  var startup=new Startup(); startup.cb=Marshal.SizeOf(startup); ProcessInfo p;
  if(!CreateProcess(args[0],new StringBuilder("\""+args[0]+"\""),IntPtr.Zero,IntPtr.Zero,false,2,IntPtr.Zero,args[1],ref startup,out p)) {
   Log("CreateProcess failed: "+Marshal.GetLastWin32Error()); return 3;
  }
  Log("Game PID="+p.pid);
  bool initialBreakpoint=true;
  try {
   while(true) {
    DebugEvent e;
    if(!WaitForDebugEvent(out e,0xffffffff)) { Log("Wait failed: "+Marshal.GetLastWin32Error()); return 4; }
    uint status=0x10002;
    if(e.kind==1) {
     status=0x80010001;
     if(e.code==0x80000003 && e.firstChance!=0 && initialBreakpoint) { status=0x10002; initialBreakpoint=false; }
     if(e.firstChance==0) {
      Log("Unhandled 0x"+e.code.ToString("X8")+" at 0x"+e.address.ToInt32().ToString("X8")+" thread="+e.tid);
      try { Capture(p,e); } catch(Exception ex) { Log("Dump failed: "+ex.Message); }
     }
    }
    if((e.kind==2 || e.kind==3 || e.kind==6) && e.file!=IntPtr.Zero && e.file.ToInt32()!=-1) CloseHandle(e.file);
    if(e.kind==5) { Log("Exit 0x"+e.code.ToString("X8")); ContinueDebugEvent(e.pid,e.tid,status); return unchecked((int)e.code); }
    ContinueDebugEvent(e.pid,e.tid,status);
   }
  } finally { CloseHandle(p.thread); CloseHandle(p.process); }
 }
 static void Capture(ProcessInfo p,DebugEvent e) {
  IntPtr context=Marshal.AllocHGlobal(716),record=Marshal.AllocHGlobal(96),pointers=Marshal.AllocHGlobal(8);
  IntPtr thread=OpenThread(8,false,e.tid);
  try {
   Marshal.Copy(new byte[716],0,context,716); Marshal.WriteInt32(context,0x1003f);
   if(!GetThreadContext(thread,context)) throw new System.ComponentModel.Win32Exception();
   Marshal.StructureToPtr(e,record,false);
   Marshal.WriteIntPtr(pointers,IntPtr.Add(record,12)); Marshal.WriteIntPtr(pointers,4,context);
   var info=new ExceptionInfo { thread=e.tid,pointers=pointers,clientPointers=0 };
   string path=Path.Combine(folder,"SamKook-"+p.pid+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".dmp");
   using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
    if(!MiniDumpWriteDump(p.process,p.pid,f.SafeFileHandle.DangerousGetHandle(),0,ref info,IntPtr.Zero,IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
   }
   Log("Dump saved: "+Path.GetFileName(path));
  } finally { if(thread!=IntPtr.Zero)CloseHandle(thread); Marshal.FreeHGlobal(context); Marshal.FreeHGlobal(record); Marshal.FreeHGlobal(pointers); }
 }
}
