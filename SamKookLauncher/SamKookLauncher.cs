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
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]

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
    const string LauncherVersion = "1.0.2";
    const string DefaultGame = @"C:\Users\seo\Downloads\DGGL\Games\SamKook_Win\SamKook.exe";
    readonly TextBox gamePath = new TextBox();
    readonly TextBox log = new TextBox();
    readonly Button play = new Button();
    readonly Button serverButton = new Button();
    readonly Button update = new Button();
    readonly LobbyServer server;
    readonly object logLock = new object();

    public LauncherForm() {
      Text = "삼국통일 FreeNet 런처";
      ClientSize = new Size(720, 440);
      MinimumSize = new Size(650, 400);
      Font = new Font("맑은 고딕", 10F);
      StartPosition = FormStartPosition.CenterScreen;

      var title = new Label { Text = "삼국통일 FreeNet", Font = new Font("맑은 고딕", 20F, FontStyle.Bold), AutoSize = true, Location = new Point(20, 18) };
      var version = new Label { Text = "v" + LauncherVersion, Font = new Font("맑은 고딕", 9F), AutoSize = true, ForeColor = Color.SteelBlue, Location = new Point(275, 35) };
      var hint = new Label { Text = "함께하기 → 인터넷 플레이 전용 로컬 로비", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(24, 58) };
      var pathLabel = new Label { Text = "게임 실행 파일", AutoSize = true, Location = new Point(20, 96) };
      gamePath.Text = LoadPath(); gamePath.Location = new Point(20, 120); gamePath.Width = 585;
      var browse = new Button { Text = "찾기", Location = new Point(615, 117), Size = new Size(80, 31) };
      browse.Click += Browse;

      serverButton.Text = "로컬 서버 시작"; serverButton.Location = new Point(20, 166); serverButton.Size = new Size(150, 42);
      play.Text = "게임 실행"; play.Location = new Point(180, 166); play.Size = new Size(150, 42); play.Font = new Font(Font, FontStyle.Bold);
      update.Text = "런처 업데이트"; update.Location = new Point(340, 166); update.Size = new Size(150, 42);
      serverButton.Click += ToggleServer;
      play.Click += StartGame;
      update.Click += async (s,e) => await CheckUpdate();

      log.Location = new Point(20, 225); log.Size = new Size(675, 190); log.Multiline = true; log.ReadOnly = true;
      log.ScrollBars = ScrollBars.Vertical; log.BackColor = Color.FromArgb(25,25,28); log.ForeColor = Color.Gainsboro;
      log.Font = new Font("Consolas", 9F);
      Controls.AddRange(new Control[] { title, version, hint, pathLabel, gamePath, browse, serverButton, play, update, log });
      server = new LobbyServer(WriteLog);
      FormClosing += (s,e) => server.Stop();
      WriteLog("런처 v"+LauncherVersion+" 준비됨. 로컬 서버 포트: TCP 7104");
    }

    string LoadPath() {
      var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game-path.txt");
      try { return File.Exists(p) ? File.ReadAllText(p).Trim() : DefaultGame; } catch { return DefaultGame; }
    }
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
    void StartGame(object sender, EventArgs e) {
      try {
        var source = Path.GetFullPath(gamePath.Text.Trim());
        if (!File.Exists(source)) throw new FileNotFoundException("SamKook.exe를 찾을 수 없습니다.", source);
        SavePath();
        if (!server.IsRunning) { server.Start(); serverButton.Text = "로컬 서버 중지"; }
        var runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
        Directory.CreateDirectory(runtime);
        var patched = Path.Combine(runtime, "SamKook.FreeNet.exe");
        PatchServerAddresses(source, patched);
        Process.Start(new ProcessStartInfo { FileName = patched, WorkingDirectory = Path.GetDirectoryName(source), UseShellExecute = true });
        WriteLog("게임 실행: 인터넷 플레이 접속을 127.0.0.1:7104로 연결합니다.");
      } catch (Exception ex) { MessageBox.Show(this, ex.ToString(), "실행 오류"); }
    }
    static void PatchServerAddresses(string source, string destination) {
      var data = File.ReadAllBytes(source);
      int changed = 0;
      foreach (var oldAddress in new[] { "210.109.148.16", "211.44.13.187" }) {
        var oldBytes = Encoding.ASCII.GetBytes(oldAddress); var replacement = new byte[oldBytes.Length];
        var local = Encoding.ASCII.GetBytes("127.0.0.1"); Buffer.BlockCopy(local, 0, replacement, 0, local.Length);
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
    TcpListener listener; CancellationTokenSource cancel;
    public bool IsRunning { get { return listener != null; } }
    public LobbyServer(Action<string> logger) { log=logger; }
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
        // The 1999 client enters its packet parser on the first successful socket
        // notification.  With an empty receive buffer it dereferences NULL and
        // crashes.  Its wire format starts with a little-endian packet length;
        // an empty two-byte frame safely primes that parser.
        var bootstrap=new byte[] { 0x02, 0x00 };
        await c.GetStream().WriteAsync(bootstrap,0,bootstrap.Length,token);
        log("접속: "+c.Client.RemoteEndPoint+" / 초기 프레임 02-00 전송");
        Task.Run(()=>ClientLoop(c,token));
      } catch(ObjectDisposedException){} catch(Exception ex){ if(!token.IsCancellationRequested) log("서버 오류: "+ex.Message); }
    }
    async Task ClientLoop(TcpClient c, CancellationToken token) {
      var buffer=new byte[8192];
      try { using(c) using(var stream=c.GetStream()) while(!token.IsCancellationRequested) {
        int n=await stream.ReadAsync(buffer,0,buffer.Length,token); if(n==0) break;
        log("수신 "+n+" bytes: "+BitConverter.ToString(buffer,0,Math.Min(n,48)));
        List<TcpClient> peers; lock(clients) peers=new List<TcpClient>(clients);
        foreach(var p in peers) if(p!=c && p.Connected) try { await p.GetStream().WriteAsync(buffer,0,n,token); } catch { }
      }} catch(Exception ex) { if(!token.IsCancellationRequested) log("연결 종료: "+ex.Message); }
      finally { lock(clients) clients.Remove(c); log("접속 종료"); }
    }
  }
}
