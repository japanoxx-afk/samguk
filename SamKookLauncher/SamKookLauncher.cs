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
[assembly: AssemblyVersion("1.3.6.0")]
[assembly: AssemblyFileVersion("1.3.6.0")]

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
    const string LauncherVersion = "1.3.6";
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
      serverAddress.Text = LoadSetting("server-address.txt","26.157.67.215"); serverAddress.Location = new Point(125,157); serverAddress.Size = new Size(180,27);
      var serverHint = new Label { Text = "A PC: 127.0.0.1  /  B PC: A PC의 LAN·VPN IPv4", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(315,162) };
      var hosts = new Button { Text = "hosts 파일 열기", Location = new Point(545,190), Size = new Size(150,36) };
      hosts.Click += OpenHostsFile;

      serverButton.Text = "로컬 서버 시작"; serverButton.Location = new Point(20, 194); serverButton.Size = new Size(150, 42);
      play.Text = "게임 실행"; play.Location = new Point(180, 194); play.Size = new Size(150, 42); play.Font = new Font(Font, FontStyle.Bold);
      update.Text = "런처 업데이트"; update.Location = new Point(340, 194); update.Size = new Size(150, 42);
      serverButton.Click += ToggleServer;
      play.Click += StartGame;
      update.Click += async (s,e) => await CheckUpdate();

      var test=new Button { Text="서버 연결 테스트",Location=new Point(20,242),Size=new Size(180,30) };
      test.Click += async (s,e)=> { test.Enabled=false; try { await CheckConnection(); } catch(Exception ex) { WriteLog(ex.Message); MessageBox.Show(this,ex.Message,"서버 연결"); } finally { test.Enabled=true; } };
      Controls.Add(test);
      log.Location = new Point(20, 282); log.Size = new Size(675, 192); log.Multiline = true; log.ReadOnly = true;
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
    async Task CheckConnection() {
      IPAddress address;
      if(!IPAddress.TryParse(serverAddress.Text.Trim(),out address) || address.AddressFamily!=AddressFamily.InterNetwork)
        throw new ArgumentException("접속 서버 IP에 A PC의 IPv4 주소를 입력하세요.");
      WriteLog("TCP 연결 확인: "+address+":7104");
      using(var client=new TcpClient()) {
        var connect=client.ConnectAsync(address,7104);
        if(await Task.WhenAny(connect,Task.Delay(4000))!=connect) {
          client.Close();
          connect.ContinueWith(t=>{ var ignored=t.Exception; },TaskContinuationOptions.OnlyOnFaulted);
          throw new IOException("연결 시간 초과: A PC 서버 실행, A PC 방화벽 TCP 7104, 두 PC의 LAN/VPN 연결을 확인하세요. B PC에는 A PC의 IP를 입력해야 합니다.");
        }
        try { await connect; } catch(SocketException ex) { throw new IOException("서버 연결 실패 ("+ex.SocketErrorCode+"): A PC에서 로컬 서버를 시작하고 TCP 7104 방화벽 설정과 IP를 확인하세요.",ex); }
      }
      WriteLog("TCP 연결 성공. hosts 등록은 필요하지 않습니다. 게임에는 숫자 IP를 직접 적용합니다.");
    }
    async void StartGame(object sender, EventArgs e) {
      play.Enabled=false;
      try {
        var source = Path.GetFullPath(gamePath.Text.Trim());
        if (!File.Exists(source)) throw new FileNotFoundException("SamKook.exe를 찾을 수 없습니다.", source);
        IPAddress targetAddress;
        var targetText=serverAddress.Text.Trim();
        if(!IPAddress.TryParse(targetText,out targetAddress) || targetAddress.AddressFamily!=AddressFamily.InterNetwork) throw new ArgumentException("접속 서버 IP에 올바른 IPv4 주소를 입력하세요.");
        targetText=targetAddress.ToString();
        SavePath();
        SaveSetting("server-address.txt",targetText);
        if (IPAddress.IsLoopback(targetAddress) && !server.IsRunning) { server.Start(); serverButton.Text = "로컬 서버 중지"; }
        await CheckConnection();
        var runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
        Directory.CreateDirectory(runtime);
        var patched = Path.Combine(runtime, "SamKook.FreeNet.exe");
        PatchServerAddresses(source, patched, targetText);
        var monitor=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GameMonitor.exe");
        if(!File.Exists(monitor)) throw new FileNotFoundException("충돌 진단 도우미가 없습니다. GameMonitor.exe를 런처와 같은 폴더에 두세요.");
        var crashFolder=Path.Combine(runtime,"crashes");
        var game = Process.Start(new ProcessStartInfo { FileName = monitor, Arguments=Quote(patched)+" "+Quote(Path.GetDirectoryName(source))+" "+Quote(crashFolder), WorkingDirectory = Path.GetDirectoryName(source), UseShellExecute = false, CreateNoWindow=true });
        if(game!=null) {
          int pid=game.Id;
          WriteLog("게임 충돌 진단 도우미 시작 PID="+pid+" / 실제 게임 PID·예외 주소: runtime\\crashes\\crash-monitor.log");
          Task.Run(()=> {
            try { game.WaitForExit(); WriteLog("게임 진단 실행 종료 / 종료 코드 0x"+unchecked((uint)game.ExitCode).ToString("X8")+" / 충돌 자료: "+crashFolder); }
            catch(Exception ex) { WriteLog("게임 종료 상태 확인 실패: "+ex.Message); }
            finally { game.Dispose(); }
          });
        }
        WriteLog("게임 실행: 인터넷 플레이 접속을 "+targetText+":7104로 연결합니다.");
      } catch (Exception ex) { WriteLog("게임 실행 중단: "+ex.Message); MessageBox.Show(this, ex.Message, "실행 오류"); }
      finally { play.Enabled=true; }
    }
    static void PatchServerAddresses(string source, string destination, string targetAddress) {
      var data=QueuePatch.ApplyAddress(File.ReadAllBytes(source),targetAddress);
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
          var handoff=Path.Combine(Path.GetTempPath(),"SamKook-handoff-"+Guid.NewGuid().ToString("N"));
          Directory.CreateDirectory(handoff);
          var updater=Path.Combine(handoff,"LauncherUpdater.exe");
          using(var archive=ZipFile.OpenRead(zip)) {
            var entry=archive.GetEntry("LauncherUpdater.exe");
            if(entry==null) throw new InvalidDataException("업데이트 도우미가 ZIP에 없습니다.");
            entry.ExtractToFile(updater);
          }
          string ready=Path.Combine(handoff,"ready");
          using(var helper=Process.Start(new ProcessStartInfo { FileName=updater, Arguments=Quote(zip)+" "+Process.GetCurrentProcess().Id+" "+Quote(AppDomain.CurrentDomain.BaseDirectory)+" "+Quote(ready), UseShellExecute=false,CreateNoWindow=true })) {
            var timer=Stopwatch.StartNew();
            while(!File.Exists(ready) && !helper.HasExited && timer.ElapsedMilliseconds<15000) await Task.Delay(100);
            if(!File.Exists(ready) || helper.HasExited) throw new IOException("업데이트 준비에 실패했습니다. 런처는 종료하지 않았습니다. 게임/다른 런처를 닫고 update.log를 확인하세요.");
            WriteLog("업데이트 검증·백업 완료. 교체 후 자동 재시작합니다.");
            Application.Exit();
          }
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
    static string Quote(string s) {
      var b=new StringBuilder("\""); int slashes=0;
      foreach(char c in s) {
        if(c=='\\') { slashes++; continue; }
        b.Append('\\',c=='\"'?slashes*2+1:slashes); b.Append(c); slashes=0;
      }
      b.Append('\\',slashes*2); b.Append('"'); return b.ToString();
    }
    void WriteLog(string text) {
      var line="["+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")+"] "+text+Environment.NewLine;
      lock(logLock) { try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"freenet.log"),line,Encoding.UTF8); } catch { } }
      if(IsDisposed || Disposing || !IsHandleCreated) return;
      if (InvokeRequired) { try { BeginInvoke(new Action<string>(AppendLog), line); } catch(InvalidOperationException) { } return; }
      AppendLog(line);
    }
    void AppendLog(string line) { if(!IsDisposed && !Disposing) log.AppendText(line); }
  }

  sealed class LobbyServer {
    sealed class Session { public int Challenge; public bool Issued; public volatile bool Authenticated; public volatile bool InLobby; public string Id; public string Room; public byte[] RoomPacket; public IPAddress Address; public NetworkStream Stream; public TcpClient Client; }
    readonly List<Session> sessions=new List<Session>();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NetworkStream,SemaphoreSlim> writes=new System.Runtime.CompilerServices.ConditionalWeakTable<NetworkStream,SemaphoreSlim>();
    readonly Dictionary<string,Session> rooms=new Dictionary<string,Session>(StringComparer.OrdinalIgnoreCase);
    readonly Action<string> log; readonly List<TcpClient> clients = new List<TcpClient>();
    readonly AccountStore accounts;
    readonly int port;
    TcpListener listener; CancellationTokenSource cancel;
    public int ListeningPort { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    public bool IsRunning { get { return listener != null; } }
    public LobbyServer(Action<string> logger, int listenPort=7104) {
      log=logger; port=listenPort;
      accounts=new AccountStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accounts.db"));
    }
    public void Start() {
      if (IsRunning) return;
      cancel=new CancellationTokenSource(); listener=new TcpListener(IPAddress.Any,port); listener.Start();
      log("FreeNet 로비 서버 시작: 0.0.0.0:"+ListeningPort); Task.Run(()=>AcceptLoop(cancel.Token));
      try { foreach(var address in Dns.GetHostAddresses(Dns.GetHostName())) if(address.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(address)) log("B PC에 입력할 A PC IP 후보: "+address+" (B와 연결된 LAN/VPN 주소 선택)"); } catch { }
    }
    public void Stop() {
      if (!IsRunning) return; cancel.Cancel(); listener.Stop(); listener=null;
      lock(clients) { foreach(var c in clients) try{c.Close();}catch{} clients.Clear(); }
      log("FreeNet 로비 서버 중지");
    }
    async Task AcceptLoop(CancellationToken token) {
      while(!token.IsCancellationRequested) try {
        var c=await listener.AcceptTcpClientAsync(); lock(clients) clients.Add(c);
        // E1/05 is ignored by the client. QueuePatch, not this greeting,
        // addresses the independent client send-queue race.
        var bootstrap=new byte[] { 0xE1, 0x05, 0x04, 0x00 };
        await c.GetStream().WriteAsync(bootstrap,0,bootstrap.Length,token);
        log("접속: "+c.Client.RemoteEndPoint+" / 안전 초기 프레임 E1-05-04-00 전송");
        Task.Run(()=>ClientLoop(c,token));
      } catch(ObjectDisposedException){} catch(Exception ex){ if(!token.IsCancellationRequested) log("서버 오류: "+ex.Message); }
    }
    async Task ClientLoop(TcpClient c, CancellationToken token) {
      var buffer=new byte[8192];
      var pending=new List<byte>();
      var session=new Session { Client=c,Stream=c.GetStream(),Address=((IPEndPoint)c.Client.RemoteEndPoint).Address };
      lock(sessions) sessions.Add(session);
      var nonce=new byte[4]; using(var rng=System.Security.Cryptography.RandomNumberGenerator.Create())rng.GetBytes(nonce);
      session.Challenge=BitConverter.ToInt32(nonce,0);
      try { using(c) using(var stream=c.GetStream()) while(!token.IsCancellationRequested) {
        int n=await stream.ReadAsync(buffer,0,buffer.Length,token); if(n==0) break;
        log("수신 "+n+" bytes (계정 검증값 보호를 위해 원문 생략)");
        for(int i=0;i<n;i++) pending.Add(buffer[i]);
        // The client sends a one-byte protocol selector before framed packets.
        if(pending.Count>0 && pending[0]==0x31) pending.RemoveAt(0);
        while(pending.Count>=4) {
          if(pending[0]!=0xE1) { log("알 수 없는 바이트 폐기: "+pending[0].ToString("X2")); pending.RemoveAt(0); continue; }
          int length=pending[2] | (pending[3]<<8);
          if(length<4 || length>8192) { log("잘못된 패킷 길이: "+length); pending.RemoveAt(0); continue; }
          if(pending.Count<length) break;
          var packet=pending.GetRange(0,length).ToArray(); pending.RemoveRange(0,length);
          log("요청 E1-"+packet[1].ToString("X2")+" / 길이 "+length);
          if(!await HandlePacket(session,stream,packet,token))
            log("미지원 요청 E1-"+packet[1].ToString("X2")+" (응답 없음)");
        }
      }} catch(Exception ex) { if(!token.IsCancellationRequested) log("연결 종료: "+ex.Message); }
      finally { session.InLobby=false; lock(sessions) sessions.Remove(session); ReleaseRoom(session); lock(clients) clients.Remove(c); log("접속 종료"); }
    }
    async Task<bool> HandlePacket(Session session, NetworkStream stream, byte[] packet, CancellationToken token) {
      byte command=packet[1];
      if(command==0x09) {
        await Send(stream,RoomList(session,packet),token);
        log("방 목록 응답 전송 (E1-09)"); return true;
      }
      if(command==0x0E) {
        if(!session.Authenticated || !session.InLobby || packet.Length<6 || packet.Length>165 || packet[packet.Length-1]!=0) return true;
        string message=ReadString(packet,4);
        if(message.Length==0 || message.IndexOfAny(new[]{'\r','\n','\t'})>=0) return true;
        var talk=ChatEvent(5,session.Id,message);
        List<Session> peers; lock(sessions) peers=sessions.FindAll(x=>x.Authenticated && x.InLobby);
        await Task.WhenAll(peers.ConvertAll(peer=>SendChat(peer,talk,token)));
        log("로비 채팅 전달: "+peers.Count+"명 (내용은 기록하지 않음)"); return true;
      }
      if(command==0x02) { ReleaseRoom(session); return true; }
      if(command==0x10) { session.InLobby=false; return true; }
      if(command==0x08) {
        // Original creator 429900: 24-byte fixed header followed by room name,
        // password, and host/map description, all NUL terminated. Response
        // status 1 at offset 4 makes 43C0F7 enter host state 4 and close dialog.
        int end=packet.Length>=27?Array.IndexOf(packet,(byte)0,24):-1;
        int passwordEnd=end>=24?Array.IndexOf(packet,(byte)0,end+1):-1;
        int infoEnd=passwordEnd>=0?Array.IndexOf(packet,(byte)0,passwordEnd+1):-1;
        bool valid=session.Authenticated && packet.Length<=512 && end>24 && end-24<=31
          && passwordEnd>=0 && passwordEnd-end<=9 && infoEnd==packet.Length-1;
        string name=valid?ReadString(packet,24):"";
        int operation=valid?BitConverter.ToInt32(packet,4):-1;
        bool accepted=false;
        lock(rooms) {
          Session owner;
          if(valid && name.Length>0 && (operation==0 || operation==12)
            && (!rooms.TryGetValue(name,out owner) || owner==session)
            && (operation==0 || session.Room==name)) {
            if(session.Room!=null && session.Room!=name) rooms.Remove(session.Room);
            session.Room=name; session.RoomPacket=(byte[])packet.Clone(); rooms[name]=session;
            accepted=true; session.InLobby=false;
          }
        }
        await SendStatus(stream,0x08,accepted?1:0,token);
        log(accepted?"방 생성/갱신 승인 (E1-08 / 상태 1). 방 대기 화면 전환 요청 완료.":"방 생성 거부: 인증·패킷 형식·중복 방 이름을 확인하세요.");
        return true;
      }
      if(command==0x05 && !session.Issued) {
        session.Issued=true;
        await SendStatus(stream,0x28,session.Challenge,token);
        log("인증 난수 발급"); return true;
      }
      if(command==0x2A) {
        // Create account: header(4), password verifier(16), login token(4), id(NUL).
        if(packet.Length<25) { await SendStatus(stream,0x2A,0,token); return true; }
        string id=ReadString(packet,24);
        byte[] verifier=Slice(packet,4,16), loginToken=Slice(packet,20,4);
        AccountStore.CreateResult result=accounts.Create(id,verifier,loginToken);
        bool accepted=result!=AccountStore.CreateResult.Conflict && result!=AccountStore.CreateResult.Invalid;
        log("계정 생성 "+(result==AccountStore.CreateResult.Created?"성공":result==AccountStore.CreateResult.ExistingSame?"재전송 성공":result==AccountStore.CreateResult.Conflict?"중복 거부":"형식 거부")+": "+SafeId(id));
        await SendStatus(stream,0x2A,accepted?1:0,token);
        return true;
      }
      if(command==0x36) {
        // Pre-auth client metadata; not a password proof. The client's shared
        // outgoing buffer may contain unrelated stale bytes in this request.
        if(!session.Issued) { session.Issued=true; await SendStatus(stream,0x28,session.Challenge,token); }
        await SendStatus(stream,0x36,1,token);
        log("접속 사전 단계 완료 / 비밀번호 인증 대기");
        return true;
      }
      if(command==0x29) {
        // header + client nonce + server nonce + 20-byte proof + account NUL.
        bool valid=packet.Length>=34 && session.Issued && BitConverter.ToInt32(packet,8)==session.Challenge;
        string id=valid?ReadString(packet,32):"";
        bool authenticated=valid && accounts.VerifyChallenge(id,Slice(packet,4,8),Slice(packet,12,20));
        session.Id=authenticated?id:null; session.Authenticated=authenticated;
        if(!authenticated) { session.InLobby=false; ReleaseRoom(session); }
        await SendStatus(stream,0x29,session.Authenticated?1:0,token);
        log("비밀번호 인증 "+(session.Authenticated?"성공":"실패")+": "+SafeId(id));
        return true;
      }
      if(command==0x0B && session.Authenticated) {
        var names=Encoding.ASCII.GetBytes("FreeNet\0\0"); var response=new byte[4+names.Length];
        response[0]=0xE1;response[1]=0x0B;response[2]=(byte)response.Length;
        Buffer.BlockCopy(names,0,response,4,names.Length);
        await Send(stream,response,token);
        log("로비 채널 목록 전송: FreeNet");
        return true;
      }
      if(command==0x0C && session.Authenticated) {
        ReleaseRoom(session);
        session.InLobby=true;
        // Original handler 43C524: event 7 clears the user list and reads
        // TWO NUL-terminated strings at offset 28, displaying the second.
        // Do not echo incoming flags or unchecked strings into this parser.
        var names=Encoding.ASCII.GetBytes("\0FreeNet\0");
        var response=new byte[28+names.Length];
        response[0]=0xE1; response[1]=0x0F; response[2]=(byte)response.Length;
        response[4]=7;
        Buffer.BlockCopy(names,0,response,28,names.Length);
        await Send(stream,response,token);
        log("채널 입장 알림 전송: FreeNet (E1-0F / 이벤트 7)");
        return true;
      }
      return false;
    }
    void ReleaseRoom(Session session) {
      lock(rooms) {
        Session owner;
        if(session.Room!=null && rooms.TryGetValue(session.Room,out owner) && owner==session) rooms.Remove(session.Room);
        session.Room=null; session.RoomPacket=null;
      }
    }
    static byte[] ChatEvent(int kind,string name,string message) {
      var text=Encoding.GetEncoding(949).GetBytes(name+"\0"+message+"\0");
      var response=new byte[28+text.Length]; response[0]=0xE1;response[1]=0x0F;
      response[2]=(byte)response.Length;response[3]=(byte)(response.Length>>8);response[4]=(byte)kind;
      Buffer.BlockCopy(text,0,response,28,text.Length); return response;
    }
    async Task SendChat(Session peer,byte[] packet,CancellationToken token) {
      using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
        timeout.CancelAfter(3000);
        try { await Send(peer.Stream,packet,timeout.Token); } catch { peer.Client.Close(); }
      }
    }
    byte[] RoomList(Session requester,byte[] query) {
      var entries=new List<byte[]>(); int max=20;
      if(query.Length>=20) max=Math.Max(0,Math.Min(20,BitConverter.ToInt32(query,16)));
      var encoding=Encoding.GetEncoding(949);
      lock(rooms) if(requester.Authenticated) foreach(var pair in rooms) {
        if(entries.Count>=max) break;
        var host=pair.Value; var source=host.RoomPacket; if(source==null || !host.Authenticated || string.IsNullOrEmpty(host.Id))continue;
        int end=Array.IndexOf(source,(byte)0,24), passEnd=Array.IndexOf(source,(byte)0,end+1);
        // Never disclose room passwords; protected-room join is not implemented.
        if(passEnd!=end+1)continue;
        string info=ReadString(source,passEnd+1); int space=info.IndexOf(' ');
        if(space<0)continue;
        string map=info.Substring(space+1);
        if(encoding.GetByteCount(host.Id)>20 || encoding.GetByteCount(map)>31)continue;
        var ip=host.Address;
        if(IPAddress.IsLoopback(ip) && !IPAddress.IsLoopback(requester.Address))
          ip=((IPEndPoint)requester.Client.Client.LocalEndPoint).Address;
        var strings=encoding.GetBytes(pair.Key+"\0\0"+host.Id+" "+map+"\0");
        var row=new byte[32+strings.Length];
        // Client 43BF44: game type at 0, sockaddr family at 8, address at 12,
        // open-room state at 24; name/password/host+map strings begin at 32.
        Buffer.BlockCopy(source,12,row,0,2); row[8]=2; row[24]=4;
        Buffer.BlockCopy(ip.GetAddressBytes(),0,row,12,4);
        Buffer.BlockCopy(strings,0,row,32,strings.Length); entries.Add(row);
      }
      using(var memory=new MemoryStream()) using(var writer=new BinaryWriter(memory)) {
        writer.Write((byte)0xE1);writer.Write((byte)0x09);writer.Write((ushort)0);writer.Write(entries.Count);
        foreach(var entry in entries)writer.Write(entry);
        var response=memory.ToArray();response[2]=(byte)response.Length;response[3]=(byte)(response.Length>>8);return response;
      }
    }
    static async Task Send(NetworkStream stream,byte[] response,CancellationToken token) {
      var gate=writes.GetValue(stream,s=>new SemaphoreSlim(1,1));
      await gate.WaitAsync(token);
      try { await stream.WriteAsync(response,0,response.Length,token); } finally { gate.Release(); }
    }
    async Task Relay(TcpClient source, byte[] packet, CancellationToken token) {
      List<TcpClient> peers; lock(clients) peers=new List<TcpClient>(clients);
      foreach(var peer in peers) if(peer!=source && peer.Connected) try { await peer.GetStream().WriteAsync(packet,0,packet.Length,token); } catch { }
    }
    static async Task SendStatus(NetworkStream stream, byte command, int status, CancellationToken token) {
      var response=new byte[] { 0xE1,command,0x08,0x00,(byte)status,(byte)(status>>8),(byte)(status>>16),(byte)(status>>24) };
      await Send(stream,response,token);
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
    public bool VerifyChallenge(string id,byte[] nonces,byte[] proof) {
      lock(sync) {
        Account account; if(!Load().TryGetValue(Normalize(id),out account))return false;
        string stored=account.Verifier+account.Token;
        if(stored.Length!=40 || nonces.Length!=8 || proof.Length!=20)return false;
        var material=new byte[28];Buffer.BlockCopy(nonces,0,material,0,8);
        for(int i=0;i<20;i++)material[8+i]=Convert.ToByte(stored.Substring(i*2,2),16);
        return FixedEquals(Hex(LegacyHash.Compute(material)),Hex(proof));
      }
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
