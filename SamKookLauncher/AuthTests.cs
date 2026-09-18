using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
static class AuthTests {
 static Assembly asm;
 static byte[] Hash(byte[] b){return (byte[])asm.GetType("SamKookFreeNet.LegacyHash").GetMethod("Compute").Invoke(null,new object[]{b});}
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static int Main(){
  asm=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SamKookLauncher.exe"));
  Check(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accounts.db")),"Use an isolated directory");
  Check(BitConverter.ToString(Hash(Encoding.ASCII.GetBytes("example"))).Replace("-","").ToLowerInvariant()=="41f41ae22e0082028a1a389645164047108f94fa","Game hash vector mismatch");
  var v=new byte[28];for(int i=0;i<28;i++)v[i]=(byte)i;
  Check(BitConverter.ToString(Hash(v)).Replace("-","").ToLowerInvariant()=="bc2974a7df5aa62bf66e8b6e114daf4c2c0b5532","Game challenge vector mismatch");
  var type=asm.GetType("SamKookFreeNet.LobbyServer");
  var server=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{new Action<string>(Console.WriteLine),0},null);
  type.GetMethod("Start").Invoke(server,null);
  int port=(int)type.GetProperty("ListeningPort").GetValue(server,null);
  try{using(var c=new TcpClient("127.0.0.1",port))using(var s=c.GetStream()){
   s.ReadTimeout=4000;Read(s);s.WriteByte(0x31);Send(s,5,new byte[20]);var challenge=Read(s);Check(challenge[1]==0x28,"No challenge");
   var room=new byte[20+Encoding.ASCII.GetByteCount("Test room\0\0testuser Map01\0")];
   Encoding.ASCII.GetBytes("Test room\0\0testuser Map01\0").CopyTo(room,20);
   Send(s,8,room);Check(Read(s)[4]==0,"Unauthenticated room accepted");
   var pw=Hash(Encoding.ASCII.GetBytes("example"));var id=Encoding.ASCII.GetBytes("testuser\0");
   var create=new byte[20+id.Length];Buffer.BlockCopy(pw,0,create,0,20);Buffer.BlockCopy(id,0,create,20,id.Length);
   Send(s,0x2A,create);Check(Read(s)[4]==1,"Create status");Send(s,0x2A,create);Check(Read(s)[4]==1,"Retry status");
   create[0]^=1;Send(s,0x2A,create);Check(Read(s)[4]==0,"Conflict accepted");
   Send(s,0x36,new byte[45]);Check(Read(s)[4]==1,"Pre-auth status");
   var material=new byte[28];Buffer.BlockCopy(challenge,4,material,4,4);Buffer.BlockCopy(pw,0,material,8,20);
   var login=new byte[28+id.Length];Buffer.BlockCopy(material,0,login,0,8);Buffer.BlockCopy(Hash(material),0,login,8,20);Buffer.BlockCopy(id,0,login,28,id.Length);
   login[8]^=1;Send(s,0x29,login);Check(Read(s)[4]==0,"Wrong proof accepted");login[8]^=1;
   var frame=Frame(0x29,login);s.Write(frame,0,3);s.Write(frame,3,frame.Length-3);Check(Read(s)[4]==1,"Valid proof rejected");
   Send(s,0x0B,new byte[4]);var channels=Read(s);Check(channels[1]==0x0B && Encoding.ASCII.GetString(channels,4,channels.Length-4)=="FreeNet\0\0","No channels");
   Send(s,0x0C,Encoding.ASCII.GetBytes("\0\0\0\0FreeNet\0"));var joined=Read(s);
   Check(joined[1]==0x0F && BitConverter.ToInt32(joined,4)==7 && joined.Length==37 && Encoding.ASCII.GetString(joined,28,joined.Length-28)=="\0FreeNet\0","Channel event layout");
   Send(s,8,new byte[4]);Check(Read(s)[4]==0,"Malformed room accepted");
   Send(s,8,room);var created=Read(s);Check(created.Length==8 && created[1]==8 && created[4]==1,"Room create failed");
   Send(s,8,room);Check(Read(s)[4]==1,"Room retry failed");
   room[0]=12;Send(s,8,room);Check(Read(s)[4]==1,"Room refresh failed");
   using(var b=new TcpClient("127.0.0.1",port))using(var bs=b.GetStream()) {
    bs.ReadTimeout=4000;Read(bs);
    var query=new byte[17];query[12]=20;
    Send(bs,9,query);Check(BitConverter.ToInt32(Read(bs),4)==0,"Unauthenticated room listing");
    Login(bs,"guestuser");
    Send(s,0x10,new byte[0]);
    Send(bs,9,query);var listed=Read(bs);
    Check(BitConverter.ToInt32(listed,4)==1 && Encoding.ASCII.GetString(listed,40,10)=="Test room\0","Room not visible to B");
    Check(listed[20]==127 && listed[23]==1 && listed[32]==4,"Room IP/status layout");
    File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"room-response.bin"),listed);
    Send(s,2,new byte[0]);Send(s,9,query);Check(BitConverter.ToInt32(Read(s),4)==0,"Stop advertising did not remove room");
    Send(s,0x0C,Encoding.ASCII.GetBytes("\0\0\0\0FreeNet\0"));Read(s);
    Send(bs,0x0C,Encoding.ASCII.GetBytes("\0\0\0\0FreeNet\0"));Read(bs);
    var chat=Frame(0x0E,Encoding.GetEncoding(949).GetBytes("안녕하세요\0"));s.Write(chat,0,2);s.Write(chat,2,chat.Length-2);
    var own=Read(s);var other=Read(bs);
    Check(BitConverter.ToInt32(other,4)==5 && Encoding.GetEncoding(949).GetString(other,28,other.Length-28)=="testuser\0안녕하세요\0","Chat not delivered to B");
    Check(Convert.ToBase64String(own)==Convert.ToBase64String(other),"Chat self echo differs");
    File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"chat-response.bin"),other);
    Send(bs,0x0E,Encoding.GetEncoding(949).GetBytes("답장\0"));Read(bs);var reply=Read(s);
    Check(Encoding.GetEncoding(949).GetString(reply,28,reply.Length-28)=="guestuser\0답장\0","B-to-A chat failed");
    room[0]=0;Send(bs,8,room);Check(Read(bs)[4]==1,"B room create failed");
   }
   bool removed=false;
   for(int i=0;i<30;i++) { var q=new byte[17];q[12]=20;Send(s,9,q);if(BitConverter.ToInt32(Read(s),4)==0){removed=true;break;}System.Threading.Thread.Sleep(20); }
   Check(removed,"Disconnected host room retained");
  }Console.WriteLine("PASS: auth, two-client room discovery/removal, bidirectional Korean chat");return 0;}
  finally{type.GetMethod("Stop").Invoke(server,null);}
 }
 static void Login(NetworkStream s,string user) {
  s.WriteByte(0x31);Send(s,5,new byte[20]);var challenge=Read(s);
  var pw=Hash(Encoding.ASCII.GetBytes("example"));var id=Encoding.ASCII.GetBytes(user+"\0");
  var create=new byte[20+id.Length];pw.CopyTo(create,0);id.CopyTo(create,20);Send(s,0x2A,create);Check(Read(s)[4]==1,"Guest create");
  var material=new byte[28];Buffer.BlockCopy(challenge,4,material,4,4);pw.CopyTo(material,8);
  var login=new byte[28+id.Length];Buffer.BlockCopy(material,0,login,0,8);Hash(material).CopyTo(login,8);id.CopyTo(login,28);
  Send(s,0x29,login);Check(Read(s)[4]==1,"Guest login");
 }
 static byte[] Frame(byte cmd,byte[] body){var p=new byte[4+body.Length];p[0]=0xE1;p[1]=cmd;p[2]=(byte)p.Length;p[3]=(byte)(p.Length>>8);Buffer.BlockCopy(body,0,p,4,body.Length);return p;}
 static void Send(NetworkStream s,byte cmd,byte[] body){var p=Frame(cmd,body);s.Write(p,0,p.Length);}
 static byte[] Read(NetworkStream s){var h=new byte[4];ReadAll(s,h,0,4);int n=h[2]|h[3]<<8;Check(n>=4 && n<8192,"Bad length");var p=new byte[n];Buffer.BlockCopy(h,0,p,0,4);ReadAll(s,p,4,n-4);return p;}
 static void ReadAll(NetworkStream s,byte[] p,int o,int n){while(n>0){int r=s.Read(p,o,n);if(r==0)throw new EndOfStreamException();o+=r;n-=r;}}
}
