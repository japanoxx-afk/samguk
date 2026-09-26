using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SamKookFreeNet {
 static class DiagnosticUploader {
  const string Owner="japanoxx-afk", Repo="samguk";
  static string Config { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostic-upload.dat"); } }
  public static bool Enabled { get { return File.Exists(Config); } }
  public static void Configure(IWin32Window owner) {
   using(var f=new Form { Text="Git 진단 자동 업로드",Width=570,Height=245,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false }) {
    var info=new Label { Left=15,Top=15,Width=525,Height=75,Text="GitHub fine-grained token을 이 PC의 Windows 계정으로 암호화해 저장합니다.\r\n저장소 japanoxx-afk/samguk의 Contents: Read and write 권한만 부여하세요.\r\n오류 발생 시 sync 기록과 최신 충돌 자료가 공개 저장소 diagnostics 폴더에 올라갑니다." };
    var token=new TextBox { Left=15,Top=95,Width=525,UseSystemPasswordChar=true };
    var save=new Button { Text="저장·활성화",Left=245,Top=135,Width=140,DialogResult=DialogResult.OK };
    var disable=new Button { Text="비활성화",Left=400,Top=135,Width=140 };
    disable.Click+=(s,e)=>{try{if(File.Exists(Config))File.Delete(Config);}catch{}f.DialogResult=DialogResult.Cancel;f.Close();};
    f.Controls.AddRange(new Control[]{info,token,save,disable});f.AcceptButton=save;
    if(f.ShowDialog(owner)==DialogResult.OK) {
     var value=token.Text.Trim();if(value.Length<20)throw new InvalidDataException("GitHub 토큰을 입력하세요.");
     var encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser);
     File.WriteAllBytes(Config,encrypted);
    }
   }
  }
  static string Token() { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Config),null,DataProtectionScope.CurrentUser)); }
  public static async Task<string> Upload(string syncPath,string crashFolder) {
   if(!Enabled)return null;
   string temp=Path.Combine(Path.GetTempPath(),"samkook-diagnostic-"+Guid.NewGuid().ToString("N")+".zip");
   try {
    using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create)) {
     Add(zip,syncPath);Add(zip,Path.ChangeExtension(syncPath,"txt"));
     if(Directory.Exists(crashFolder)) foreach(var p in Latest(crashFolder,4))Add(zip,p);
    }
    byte[] bytes=File.ReadAllBytes(temp);if(bytes.Length>20*1024*1024)throw new InvalidDataException("진단 압축 파일이 안전 업로드 제한(20MB)을 초과했습니다.");
    string name=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".zip";
    string json="{\"message\":\"Upload multiplayer diagnostic "+name+"\",\"content\":\""+Convert.ToBase64String(bytes)+"\"}";
    using(var http=new HttpClient()) {
     http.DefaultRequestHeaders.UserAgent.ParseAdd("SamKook-FreeNet/1.12.4");
     http.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",Token());
     var response=await http.PutAsync("https://api.github.com/repos/"+Owner+"/"+Repo+"/contents/diagnostics/"+name,new StringContent(json,Encoding.UTF8,"application/json"));
     if(!response.IsSuccessStatusCode)throw new InvalidDataException("GitHub HTTP "+(int)response.StatusCode+": "+await response.Content.ReadAsStringAsync());
    }
    return "https://github.com/"+Owner+"/"+Repo+"/tree/main/diagnostics";
   } finally { try{if(File.Exists(temp))File.Delete(temp);}catch{} }
  }
  static void Add(ZipArchive zip,string path) { if(!string.IsNullOrEmpty(path)&&File.Exists(path))zip.CreateEntryFromFile(path,Path.GetFileName(path),CompressionLevel.Optimal); }
  static string[] Latest(string folder,int count) {
   var f=new DirectoryInfo(folder).GetFiles();Array.Sort(f,(a,b)=>b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
   int n=Math.Min(count,f.Length);var r=new string[n];for(int i=0;i<n;i++)r[i]=f[i].FullName;return r;
  }
 }
}
