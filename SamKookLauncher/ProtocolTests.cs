using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

static class ProtocolTests {
  static int Main() {
    AppDomain.CurrentDomain.UnhandledException += delegate(object sender,UnhandledExceptionEventArgs e) {
      var ex=e.ExceptionObject as Exception; Console.Error.WriteLine("UNHANDLED "+(ex==null?e.ExceptionObject.GetType().FullName:ex.GetType().FullName+": "+ex.Message));
    };
    var accountFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accounts.db");
    if(File.Exists(accountFile)) throw new InvalidOperationException("Refusing to run protocol tests while a real accounts.db exists.");
    var assembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SamKookLauncher.exe"));
    var type=assembly.GetType("SamKookFreeNet.LobbyServer",true);
    var server=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{new Action<string>(Console.WriteLine)},null);
    type.GetMethod("Start").Invoke(server,null);
    try {
      using(var client=new TcpClient("127.0.0.1",7104)) using(var stream=client.GetStream()) {
        var bootstrap=new byte[4]; ReadAll(stream,bootstrap);
        if(bootstrap[0]!=0xE1||bootstrap[1]!=0x05||bootstrap[2]!=4||bootstrap[3]!=0) throw new Exception("Bad bootstrap");
        var create=Build(0x2A,false); stream.Write(create,0,create.Length); var response=new byte[8]; ReadAll(stream,response);
        if(response[1]!=0x2A || response[4]!=0) throw new Exception("Create failed: "+BitConverter.ToString(response));
        var login=Build(0x36,true); stream.Write(login,0,login.Length); ReadAll(stream,response);
        if(response[1]!=0x36 || response[4]!=1) throw new Exception("Login failed: "+BitConverter.ToString(response));
      }
      Console.WriteLine("PASS: account creation and login protocol"); return 0;
    } finally { type.GetMethod("Stop").Invoke(server,null); if(File.Exists(accountFile)) File.Delete(accountFile); }
  }
  static byte[] Build(byte command,bool login) {
    if(!login) {
      var p=new byte[33]; p[0]=0xE1;p[1]=command;p[2]=(byte)p.Length;
      for(int i=4;i<20;i++)p[i]=(byte)(i+1); p[20]=1;p[21]=2;p[22]=3;p[23]=4;
      var id=System.Text.Encoding.ASCII.GetBytes("testuser"); Buffer.BlockCopy(id,0,p,24,id.Length); return p;
    } else {
      var p=new byte[40]; p[0]=0xE1;p[1]=command;p[2]=(byte)p.Length;
      p[20]=1;p[21]=2;p[22]=3;p[23]=4; var id=System.Text.Encoding.ASCII.GetBytes("testuser"); Buffer.BlockCopy(id,0,p,24,id.Length); return p;
    }
  }
  static void ReadAll(NetworkStream stream,byte[] data) { int o=0; while(o<data.Length){int n=stream.Read(data,o,data.Length-o);if(n==0)throw new EndOfStreamException();o+=n;} }
}
