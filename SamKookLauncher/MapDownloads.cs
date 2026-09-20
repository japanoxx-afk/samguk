using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SamKookFreeNet {
  static class MapDownloads {
    public const string FileName="(N4) 삼한의 영광.skm";
    public const string Hash="a5e1a9e6ea5e7ec0a4de0647a05c79eb78c68981883da8d28c6f90f08ec1c272";
    public const int Length=223917;
    public const string Url="https://raw.githubusercontent.com/japanoxx-afk/samguk/main/maps/samhan-glory.skm";
    const string Root="https://raw.githubusercontent.com/japanoxx-afk/samguk/main/maps/";
    static readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
    public static string Digest(byte[] data) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    public static void Validate(byte[] data) {
      ValidateEntry(data,Length,Hash);
    }
    static void ValidateEntry(byte[] data,int length,string hash) {
      if(data==null || data.Length!=length || Digest(data)!=hash)throw new InvalidDataException("맵 다운로드 크기·SHA256 검증 실패. 기존 파일은 변경하지 않았습니다.");
      if(data[0]!='P' || data[1]!='3' || data[2]!='2' || data[3]!='M')throw new InvalidDataException("맵 파일 서명 오류");
      int w=BitConverter.ToUInt16(data,5),h=BitConverter.ToUInt16(data,7);
      if(w<16 || h<16 || w>512 || h>512 || data.Length<9+w*h*2)throw new InvalidDataException("맵 지형 크기 오류");
    }
    static async Task<byte[]> Download(string url,int maximum,int exact) {
      ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
      using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
      using(var http=new HttpClient()) {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SamKookFreeNet-Maps/1.9.1");
        http.DefaultRequestHeaders.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue { NoCache=true };
        using(var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
          response.EnsureSuccessStatusCode();
          if(response.Content.Headers.ContentLength.HasValue && (response.Content.Headers.ContentLength.Value>maximum || (exact>0 && response.Content.Headers.ContentLength.Value!=exact)))throw new InvalidDataException("맵 응답 크기 불일치");
          using(var source=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
          using(var result=new MemoryStream()) {
            var buffer=new byte[8192];int count;
            while((count=await source.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0) {
              if(result.Length+count>maximum)throw new InvalidDataException("맵 다운로드 크기 초과");
              result.Write(buffer,0,count);
            }
            return result.ToArray();
          }
        }
      }
    }
    public static async Task<string> Install(string gameExe) {
      CheckGame(gameExe);
      var catalog=await Download(Root+"catalog.tsv?check="+DateTime.UtcNow.Ticks,65536,0).ConfigureAwait(false);
      return await InstallCatalog(gameExe,new UTF8Encoding(false,true).GetString(catalog),(file,size)=>Download(Root+Uri.EscapeDataString(file),size,size)).ConfigureAwait(false);
    }
    static string CheckGame(string gameExe) {
      string exe=Path.GetFullPath(gameExe);
      if(!File.Exists(exe) || !string.Equals(Path.GetExtension(exe),".exe",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("맵을 받을 게임 실행 파일 위치를 먼저 선택하세요.");
      return exe;
    }
    public static async Task<string> InstallCatalog(string gameExe,string text,Func<string,int,Task<byte[]>> download) {
      CheckGame(gameExe);
      var entries=ParseCatalog(text);var messages=new List<string>();int errors=0;
      foreach(var entry in entries) {
        var current=entry;
        try {messages.Add(await InstallEntry(gameExe,current.Name,current.Size,current.Hash,()=>download(current.Source,current.Size)).ConfigureAwait(false));}
        catch(Exception ex) {errors++;messages.Add("맵 설치 보류: "+current.Name+" / "+ex.Message);}
      }
      return "GitHub 맵 자동 확인: "+entries.Count+"개 / 설치·동일 파일 확인 "+(entries.Count-errors)+"개 / 보류 "+errors+"개\n"+string.Join("\n",messages);
    }
    sealed class Entry {public string Source,Name,Hash;public int Size;}
    static List<Entry> ParseCatalog(string text) {
      if(text==null || text.Length>65536)throw new InvalidDataException("맵 목록 크기 오류");
      var list=new List<Entry>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
      foreach(string raw in text.TrimStart('\ufeff').Split('\n')) {
        string line=raw.TrimEnd('\r');if(line.Length==0 || line.StartsWith("#"))continue;
        var fields=line.Split('\t');int size;
        if(fields.Length!=4 || !SafeName(fields[0]) || !SafeName(fields[1]) || !Regex.IsMatch(fields[2],"\\A[0-9a-f]{64}\\z") || !int.TryParse(fields[3],out size) || size<9 || size>4*1024*1024 || !names.Add(fields[1]))throw new InvalidDataException("맵 목록 형식·파일명·중복 검증 실패");
        total+=size;if(list.Count>=128 || total>64*1024*1024)throw new InvalidDataException("맵 목록 용량 제한 초과");
        list.Add(new Entry {Source=fields[0],Name=fields[1],Hash=fields[2],Size=size});
      }
      if(list.Count==0)throw new InvalidDataException("배포 맵 목록이 비어 있습니다.");return list;
    }
    static bool SafeName(string name) {
      if(string.IsNullOrWhiteSpace(name) || name.Length>160 || name!=name.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.IndexOfAny(new[]{'/', '\\', ':'})>=0 || !name.EndsWith(".skm",StringComparison.OrdinalIgnoreCase))return false;
      return !Regex.IsMatch(name,"\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)",RegexOptions.IgnoreCase);
    }
    // Injectable download for deterministic offline tests; production URL is fixed.
    public static Task<string> InstallWithDownload(string gameExe,Func<Task<byte[]>> download) {return InstallEntry(gameExe,FileName,Length,Hash,download);}
    static async Task<string> InstallEntry(string gameExe,string name,int length,string hash,Func<Task<byte[]>> download) {
      await Gate.WaitAsync().ConfigureAwait(false);
      string temporary=null;
      try {
        string exe=CheckGame(gameExe);
        string mission=Path.Combine(Path.GetDirectoryName(exe),"Mission"),dest=Path.Combine(mission,name);
        if(File.Exists(dest)) {
          if(new FileInfo(dest).Length==length && Digest(File.ReadAllBytes(dest))==hash)return "맵 설치 확인: "+dest;
          throw new IOException("같은 이름의 다른 맵이 있어 덮어쓰지 않았습니다: "+dest+" / 기존 맵 이름을 변경한 뒤 다시 다운로드하세요.");
        }
        byte[] bytes=await download().ConfigureAwait(false);ValidateEntry(bytes,length,hash);
        Directory.CreateDirectory(mission);
        temporary=Path.Combine(mission,".samkook-map-"+Guid.NewGuid().ToString("N")+".tmp");
        using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);
        // Same-volume rename is atomic. Never overwrite a map created concurrently.
        File.Move(temporary,dest);temporary=null;
        return "맵 다운로드 완료: "+dest;
      } finally {
        if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
        Gate.Release();
      }
    }
  }
}
