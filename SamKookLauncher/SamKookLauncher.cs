using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("SamKook FreeNet Launcher")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace SamKookFreeNet {
  static class Program {
    [STAThread] static void Main() {
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new LauncherForm());
    }
  }

  sealed class LauncherForm : Form {
    const string LauncherVersion = "1.2.1";
    const string DefaultGame = @"C:\Users\seo\Downloads\DGGL\Games\SamKook_Win\SamKook.exe";
    readonly TextBox gamePath = new TextBox();
    readonly TextBox serverAddress = new TextBox();
    readonly TextBox log = new TextBox();
    readonly Button play = new Button();
    readonly Button serverButton = new Button();
    readonly Button update = new Button();
    readonly LobbyServer server;
    readonly object logLock = new object();

    public LauncherForm() {
      Text = "삼국통일 FreeNet 런처";
      ClientSize = new Size(720, 500);
      MinimumSize = new Size(650, 460);
      Font = new Font("맑은 고딕", 10F);
      StartPosition = FormStartPosition.CenterScreen;

      var title = new Label { Text = "삼국통일 FreeNet", Font = new Font("맑은 고딕", 20F, FontStyle.Bold), AutoSize = true, Location = new Point(20, 18) };
      var version = new Label { Text = "v" + LauncherVersion, Font = new Font("맑은 고딕", 9F), AutoSize = true, ForeColor = Color.SteelBlue, Location = new Point(275, 35) };
      var hint = new Label { Text = "함께하기 → 인터넷 플레이 전용 로컬 로비", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(24, 58) };
      var pathLabel = new Label { Text = "게임 실행 파일", AutoSize = true, Location = new Point(20, 96) };
      gamePath.Text = LoadPath(); gamePath.Location = new Point(20, 120); gamePath.Width = 585;
      var browse = new Button { Text = "찾기", Location = new Point(615, 117), Size = new Size(80, 31) };
      browse.Click += Browse;

      var serverLabel = new Label { Text = "접속 서버 IP", AutoSize = true, Location = new Point(20, 162) };
      serverAddress.Text = LoadSetting("server-address.txt","127.0.0.1"); serverAddress.Location = new Point(125,157); serverAddress.Size = new Size(180,27);
      var serverHint = new Label { Text = "A PC: 127.0.0.1  /  B PC: A PC의 LAN·VPN IPv4", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(315,162) };
      var hosts = new Button { Text = "hosts 파일 열기", Location = new Point(545,190), Size = new Size(150,36) };
      hosts.Click += OpenHostsFile;

      serverButton.Text = "로컬 서버 시작"; serverButton.Location = new Point(20, 194); serverButton.Size = new Size(150, 42);
      play.Text = "게임 실행"; play.Location = new Point(180, 194); play.Size = new Size(150, 42); play.Font = new Font(Font, FontStyle.Bold);
      update.Text = "런처 업데이트"; update.Location = new Point(340, 194); update.Size = new Size(150, 42);
      serverButton.Click += ToggleServer;
      play.Click += StartGame;
      update.Click += async (s,e) => await CheckUpdate();

      log.Location = new Point(20, 252); log.Size = new Size(675, 222); log.Multiline = true; log.ReadOnly = true;
      log.ScrollBars = ScrollBars.Vertical; log.BackColor = Color.FromArgb(25,25,28); log.ForeColor = Color.Gainsboro;
      log.Font = new Font("Consolas", 9F);
      Controls.AddRange(new Control[] { title, version, hint, pathLabel, gamePath, browse, serverLabel, serverAddress, serverHint, hosts, serverButton, play, update, log });
      server = new LobbyServer(WriteLog);
      FormClosing += (s,e) => server.Stop();
      WriteLog("런처 v"+LauncherVersion+" 준비됨. 로컬 서버 포트: TCP 7104");
    }

    string LoadPath() {
      var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game-path.txt");
      try { return File.Exists(p) ? File.ReadAllText(p).Trim() : DefaultGame; } catch { return DefaultGame; }
    }
    string LoadSetting(string name,string fallback) { try { var p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name); return File.Exists(p)?File.ReadAllText(p).Trim():fallback; } catch { return fallback; } }
    void SaveSetting(string name,string value) { try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name),value); } catch { } }
    void SavePath() { try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game-path.txt"), gamePath.Text.Trim()); } catch { } }
    void Browse(object sender, EventArgs e) {
      using (var d = new OpenFileDialog { Filter = "SamKook.exe|SamKook.exe|실행 파일|*.exe", FileName = gamePath.Text })
        if (d.ShowDialog(this) == DialogResult.OK) { gamePath.Text = d.FileName; SavePath(); }
    }
    void ToggleServer(object sender, EventArgs e) {
      try {
        if (server.IsRunning) server.Stop(); else server.Start();
        serverButton.Text = server.IsRunning ? "로컬 서버 중지" : "로컬 서버 시작";
      } catch (Exception ex) { MessageBox.Show(this, ex.Message, "서버 오류"); }
    }
    void OpenHostsFile(object sender,EventArgs e) {
      try {
        var hosts=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"drivers\etc\hosts");
        Process.Start(new ProcessStartInfo { FileName="notepad.exe", Arguments=Quote(hosts), UseShellExecute=true });
        WriteLog("hosts 파일을 메모장으로 열었습니다: "+hosts);
      } catch(Exception ex) { MessageBox.Show(this,ex.Message,"hosts 파일"); }
    }
    void StartGame(object sender, EventArgs e) {
      try {
        var source = Path.GetFullPath(gamePath.Text.Trim());
        if (!File.Exists(source)) throw new FileNotFoundException("SamKook.exe를 찾을 수 없습니다.", source);
        IPAddress targetAddress;
        var targetText=serverAddress.Text.Trim();
        if(!IPAddress.TryParse(targetText,out targetAddress) || targetAddress.AddressFamily!=AddressFamily.InterNetwork) throw new ArgumentException("접속 서버 IP에 올바른 IPv4 주소를 입력하세요.");
        if(targetText.Length>13) throw new ArgumentException("이 게임의 주소 저장 공간 제한으로 서버 IP는 13자 이하여야 합니다. LAN·Radmin·Hamachi IPv4를 사용하세요.");
        SavePath();
        SaveSetting("server-address.txt",targetText);
        if (IPAddress.IsLoopback(targetAddress) && !server.IsRunning) { server.Start(); serverButton.Text = "로컬 서버 중지"; }
        var runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
        Directory.CreateDirectory(runtime);
        var patched = Path.Combine(runtime, "SamKook.FreeNet.exe");
        PatchServerAddresses(source, patched, targetText);
        Process.Start(new ProcessStartInfo { FileName = patched, WorkingDirectory = Path.GetDirectoryName(source), UseShellExecute = true });
        WriteLog("게임 실행: 인터넷 플레이 접속을 "+targetText+":7104로 연결합니다.");
      } catch (Exception ex) { MessageBox.Show(this, ex.ToString(), "실행 오류"); }
    }
    static void PatchServerAddresses(string source, string destination, string targetAddress) {
      var data = File.ReadAllBytes(source);
      int changed = 0;
      foreach (var oldAddress in new[] { "210.109.148.16", "211.44.13.187" }) {
        var oldBytes = Encoding.ASCII.GetBytes(oldAddress); var replacement = new byte[oldBytes.Length];
        var target = Encoding.ASCII.GetBytes(targetAddress); Buffer.BlockCopy(target, 0, replacement, 0, target.Length);
        for (int i=0; i<=data.Length-oldBytes.Length; i++) {
          bool match=true; for(int j=0;j<oldBytes.Length;j++) if(data[i+j]!=oldBytes[j]) { match=false; break; }
          if(match) { Buffer.BlockCopy(replacement,0,data,i,replacement.Length); changed++; i += oldBytes.Length-1; }
        }
      }
      if (changed != 2) throw new InvalidDataException("지원하는 SamKook.exe가 아닙니다. 서버 주소 2개를 찾지 못했습니다.");
      File.WriteAllBytes(destination, data);
    }
    async Task CheckUpdate() {
      update.Enabled = false;
      try {
        WriteLog("업데이트 확인 중... 현재 버전 v"+LauncherVersion);
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        using (var http = new HttpClient()) {
          http.Timeout = TimeSpan.FromSeconds(20);
          http.DefaultRequestHeaders.UserAgent.ParseAdd("SamKookFreeNet/"+LauncherVersion);
          var info = await FindUpdate(http);
          Version remoteVersion, currentVersion;
          if(!Version.TryParse(info.Version.TrimStart('v','V'),out remoteVersion)) throw new InvalidDataException("업데이트 버전 형식이 잘못되었습니다: "+info.Version);
          currentVersion=new Version(LauncherVersion);
          WriteLog("최신 버전 v"+remoteVersion+" 확인 완료");
          if(remoteVersion<=currentVersion) {
            MessageBox.Show(this,"현재 최신 버전입니다.\n\n설치됨: v"+currentVersion+"\n최신: v"+remoteVersion,"런처 업데이트",MessageBoxButtons.OK,MessageBoxIcon.Information);
            update.Enabled=true; return;
          }
          var zip = Path.Combine(Path.GetTempPath(), "SamKookLauncher-update-" + Guid.NewGuid().ToString("N") + ".zip");
          WriteLog("v"+remoteVersion+" 다운로드 중...");
          File.WriteAllBytes(zip, await http.GetByteArrayAsync(info.Url));
          var updater = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LauncherUpdater.exe");
          if (!File.Exists(updater)) throw new FileNotFoundException("LauncherUpdater.exe가 없습니다.");
          Process.Start(new ProcessStartInfo { FileName=updater, Arguments=Quote(zip)+" "+Process.GetCurrentProcess().Id+" "+Quote(AppDomain.CurrentDomain.BaseDirectory), UseShellExecute=false });
          Application.Exit();
        }
      } catch (Exception ex) {
        var detail=ex.InnerException!=null ? ex.Message+"\n"+ex.InnerException.Message : ex.Message;
        WriteLog("업데이트 실패: " + detail.Replace("\r"," ").Replace("\n"," / "));
        MessageBox.Show(this,"업데이트 확인에 실패했습니다.\n\n"+detail,"런처 업데이트",MessageBoxButtons.OK,MessageBoxIcon.Error); update.Enabled=true;
      }
    }
    sealed class UpdateInfo { public string Version; public string Url; }
    static async Task<UpdateInfo> FindUpdate(HttpClient http) {
      const string releases="https://api.github.com/repos/japanoxx-afk/samguk/releases/latest";
      using(var response=await http.GetAsync(releases)) {
        if(response.IsSuccessStatusCode) {
          var json=await response.Content.ReadAsStringAsync();
          var tag=Regex.Match(json,"\\\"tag_name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
          var asset=Regex.Match(json,"\\\"browser_download_url\\\"\\s*:\\s*\\\"([^\\\"]*SamKookLauncher[^\\\"]*\\.zip)\\\"",RegexOptions.IgnoreCase);
          if(tag.Success && asset.Success) return new UpdateInfo { Version=tag.Groups[1].Value, Url=asset.Groups[1].Value.Replace("\\/","/") };
        } else if((int)response.StatusCode!=404) {
          throw new HttpRequestException("GitHub API 응답: "+(int)response.StatusCode+" "+response.ReasonPhrase);
        }
      }
      const string manifest="https://raw.githubusercontent.com/japanoxx-afk/samguk/main/SamKookLauncher/update.json";
      using(var response=await http.GetAsync(manifest)) {
        if(!response.IsSuccessStatusCode) throw new HttpRequestException("업데이트 정보가 아직 게시되지 않았습니다. HTTP "+(int)response.StatusCode);
        var json=await response.Content.ReadAsStringAsync();
        var version=Regex.Match(json,"\\\"version\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
        var url=Regex.Match(json,"\\\"url\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
        if(!version.Success || !url.Success) throw new InvalidDataException("update.json 형식이 잘못되었습니다.");
        return new UpdateInfo { Version=version.Groups[1].Value, Url=url.Groups[1].Value.Replace("\\/","/") };
      }
    }
    static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    void WriteLog(string text) {
      var line="["+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+"] "+text+Environment.NewLine;
      lock(logLock) { try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"freenet.log"),line,Encoding.UTF8); } catch { } }
      if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), line); return; }
      AppendLog(line);
    }
    void AppendLog(string line) { log.AppendText(line); }
  }

  sealed class LobbyServer {
    readonly Action<string> log; readonly List<TcpClient> clients = new List<TcpClient>();
    readonly AccountStore accounts;
    TcpListener listener; CancellationTokenSource cancel;
    public bool IsRunning { get { return listener != null; } }
    public LobbyServer(Action<string> logger) {
      log=logger;
      accounts=new AccountStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accounts.db"));
    }
    public void Start() {
      if (IsRunning) return;
      cancel=new CancellationTokenSource(); listener=new TcpListener(IPAddress.Any,7104); listener.Start();
      log("FreeNet 로비 서버 시작: 0.0.0.0:7104"); Task.Run(()=>AcceptLoop(cancel.Token));
    }
    public void Stop() {
      if (!IsRunning) return; cancel.Cancel(); listener.Stop(); listener=null;
      lock(clients) { foreach(var c in clients) try{c.Close();}catch{} clients.Clear(); }
      log("FreeNet 로비 서버 중지");
    }
    async Task AcceptLoop(CancellationToken token) {
      while(!token.IsCancellationRequested) try {
        var c=await listener.AcceptTcpClientAsync(); lock(clients) clients.Add(c);
        // The 1999 client enters its parser on the first socket notification and
        // crashes on a buffer shorter than its four-byte E1 header. E1/05 is a
        // known no-op in the client, so a complete empty frame safely primes it.
        var bootstrap=new byte[] { 0xE1, 0x05, 0x04, 0x00 };
        await c.GetStream().WriteAsync(bootstrap,0,bootstrap.Length,token);
        log("접속: "+c.Client.RemoteEndPoint+" / 안전 초기 프레임 E1-05-04-00 전송");
        Task.Run(()=>ClientLoop(c,token));
      } catch(ObjectDisposedException){} catch(Exception ex){ if(!token.IsCancellationRequested) log("서버 오류: "+ex.Message); }
    }
    async Task ClientLoop(TcpClient c, CancellationToken token) {
      var buffer=new byte[8192];
      var pending=new List<byte>();
      try { using(c) using(var stream=c.GetStream()) while(!token.IsCancellationRequested) {
        int n=await stream.ReadAsync(buffer,0,buffer.Length,token); if(n==0) break;
        log("수신 "+n+" bytes: "+BitConverter.ToString(buffer,0,Math.Min(n,48)));
        for(int i=0;i<n;i++) pending.Add(buffer[i]);
        // The client sends a one-byte protocol selector before framed packets.
        if(pending.Count>0 && pending[0]==0x31) pending.RemoveAt(0);
        while(pending.Count>=4) {
          if(pending[0]!=0xE1) { log("알 수 없는 바이트 폐기: "+pending[0].ToString("X2")); pending.RemoveAt(0); continue; }
          int length=pending[2] | (pending[3]<<8);
          if(length<4 || length>8192) { log("잘못된 패킷 길이: "+length); pending.RemoveAt(0); continue; }
          if(pending.Count<length) break;
          var packet=pending.GetRange(0,length).ToArray(); pending.RemoveRange(0,length);
          if(!await HandlePacket(c,stream,packet,token)) await Relay(c,packet,token);
        }
      }} catch(Exception ex) { if(!token.IsCancellationRequested) log("연결 종료: "+ex.Message); }
      finally { lock(clients) clients.Remove(c); log("접속 종료"); }
    }
    async Task<bool> HandlePacket(TcpClient client, NetworkStream stream, byte[] packet, CancellationToken token) {
      byte command=packet[1];
      if(command==0x2A) {
        // Create account: header(4), password verifier(16), login token(4), id(NUL).
        if(packet.Length<25) { await SendStatus(stream,0x2A,1,token); return true; }
        string id=ReadString(packet,24);
        byte[] verifier=Slice(packet,4,16), loginToken=Slice(packet,20,4);
        AccountStore.CreateResult result=accounts.Create(id,verifier,loginToken);
        bool accepted=result!=AccountStore.CreateResult.Conflict && result!=AccountStore.CreateResult.Invalid;
        log("계정 생성 "+(result==AccountStore.CreateResult.Created?"성공":result==AccountStore.CreateResult.ExistingSame?"재전송 성공":result==AccountStore.CreateResult.Conflict?"중복 거부":"형식 거부")+": "+SafeId(id));
        await SendStatus(stream,0x2A,accepted?0:1,token);
        return true;
      }
      if(command==0x36) {
        // Login: fixed client data(20), login token(4), id(NUL), machine data.
        if(packet.Length<25) { await SendStatus(stream,0x36,2,token); return true; }
        string id=ReadString(packet,24); byte[] verifier=Slice(packet,4,16), loginToken=Slice(packet,20,4);
        bool authenticated=accounts.Authenticate(id,verifier,loginToken);
        log("로그인 "+(authenticated?"성공":"실패")+": "+SafeId(id));
        // The original client maps status 1 to success and 2 to invalid/in-use.
        await SendStatus(stream,0x36,authenticated?1:2,token);
        return true;
      }
      if(command==0x29) {
        // Post-login CD-key/session validation. Zero means accepted; the client
        // then advances to its lobby handshake instead of retrying login.
        log("로그인 후 세션 인증 성공");
        await SendStatus(stream,0x29,0,token);
        return true;
      }
      return false;
    }
    async Task Relay(TcpClient source, byte[] packet, CancellationToken token) {
      List<TcpClient> peers; lock(clients) peers=new List<TcpClient>(clients);
      foreach(var peer in peers) if(peer!=source && peer.Connected) try { await peer.GetStream().WriteAsync(packet,0,packet.Length,token); } catch { }
    }
    static async Task SendStatus(NetworkStream stream, byte command, int status, CancellationToken token) {
      var response=new byte[] { 0xE1,command,0x08,0x00,(byte)status,(byte)(status>>8),(byte)(status>>16),(byte)(status>>24) };
      await stream.WriteAsync(response,0,response.Length,token);
    }
    static byte[] Slice(byte[] source,int offset,int count) { var value=new byte[count]; Buffer.BlockCopy(source,offset,value,0,count); return value; }
    static string ReadString(byte[] packet,int offset) {
      int end=offset; while(end<packet.Length && packet[end]!=0) end++;
      try { return Encoding.GetEncoding(949).GetString(packet,offset,end-offset).Trim(); }
      catch { return Encoding.ASCII.GetString(packet,offset,end-offset).Trim(); }
    }
    static string SafeId(string id) { return string.IsNullOrEmpty(id)?"(비어 있음)":id.Replace("\r","").Replace("\n",""); }
  }

  sealed class AccountStore {
    public enum CreateResult { Created, ExistingSame, Conflict, Invalid }
    sealed class Account { public string Id; public string Verifier; public string Token; }
    readonly string path; readonly object sync=new object();
    public AccountStore(string filePath) { path=filePath; }
    public CreateResult Create(string id,byte[] verifier,byte[] token) {
      id=Normalize(id); if(!Valid(id) || AllZero(verifier) || AllZero(token)) return CreateResult.Invalid;
      lock(sync) {
        var all=Load(); Account existing; string verifierHex=Hex(verifier),tokenHex=Hex(token);
        if(all.TryGetValue(id,out existing)) return FixedEquals(existing.Verifier,verifierHex)&&FixedEquals(existing.Token,tokenHex)?CreateResult.ExistingSame:CreateResult.Conflict;
        all[id]=new Account { Id=id, Verifier=verifierHex, Token=tokenHex }; Save(all); return CreateResult.Created;
      }
    }
    public bool Authenticate(string id,byte[] verifier,byte[] token) {
      id=Normalize(id); lock(sync) { var all=Load(); Account account; return all.TryGetValue(id,out account) && FixedEquals(account.Verifier,Hex(verifier)) && FixedEquals(account.Token,Hex(token)); }
    }
    Dictionary<string,Account> Load() {
      var result=new Dictionary<string,Account>(StringComparer.OrdinalIgnoreCase);
      if(!File.Exists(path)) return result;
      foreach(var line in File.ReadAllLines(path,Encoding.UTF8)) try {
        var fields=line.Split('\t'); if(fields.Length!=3) continue;
        var id=Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
        result[id]=new Account { Id=id, Verifier=fields[1], Token=fields[2] };
      } catch { }
      return result;
    }
    void Save(Dictionary<string,Account> all) {
      var temp=path+".tmp"; var lines=new List<string>();
      foreach(var account in all.Values) lines.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(account.Id))+"\t"+account.Verifier+"\t"+account.Token);
      File.WriteAllLines(temp,lines.ToArray(),Encoding.UTF8);
      if(File.Exists(path)) File.Replace(temp,path,path+".bak",true); else File.Move(temp,path);
    }
    static string Normalize(string id) { return (id??"").Trim().ToLowerInvariant(); }
    static bool Valid(string id) { return id.Length>=2 && id.Length<=20 && id.IndexOfAny(new[]{'\r','\n','\t','\0'})<0; }
    static bool AllZero(byte[] value) { foreach(var b in value) if(b!=0) return false; return true; }
    static string Hex(byte[] value) { return BitConverter.ToString(value).Replace("-",""); }
    static bool FixedEquals(string a,string b) { if(a==null||b==null||a.Length!=b.Length)return false; int diff=0; for(int i=0;i<a.Length;i++)diff|=a[i]^b[i]; return diff==0; }
  }
}
