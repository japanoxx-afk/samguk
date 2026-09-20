using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SamKookFreeNet {
  static class MapDownloads {
    public const string FileName="(N4) 삼한의 영광.skm";
    public const string Hash="a5e1a9e6ea5e7ec0a4de0647a05c79eb78c68981883da8d28c6f90f08ec1c272";
    public const int Length=223917;
    public const string Url="https://raw.githubusercontent.com/japanoxx-afk/samguk/main/maps/samhan-glory.skm";
    static readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
    public static string Digest(byte[] data) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
    public static void Validate(byte[] data) {
      if(data==null || data.Length!=Length || Digest(data)!=Hash)throw new InvalidDataException("맵 다운로드 크기·SHA256 검증 실패. 기존 파일은 변경하지 않았습니다.");
      if(data[0]!='P' || data[1]!='3' || data[2]!='2' || data[3]!='M')throw new InvalidDataException("맵 파일 서명 오류");
    }
    static async Task<byte[]> Download() {
      ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
      using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
      using(var http=new HttpClient()) {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SamKookFreeNet-Maps/1.9.0");
        using(var response=await http.GetAsync(Url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
          response.EnsureSuccessStatusCode();
          if(response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value!=Length)throw new InvalidDataException("맵 응답 크기 불일치");
          using(var source=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
          using(var result=new MemoryStream()) {
            var buffer=new byte[8192];int count;
            while((count=await source.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0) {
              if(result.Length+count>Length)throw new InvalidDataException("맵 다운로드 크기 초과");
              result.Write(buffer,0,count);
            }
            return result.ToArray();
          }
        }
      }
    }
    public static Task<string> Install(string gameExe){return InstallWithDownload(gameExe,Download);}
    // Injectable download for deterministic offline tests; production URL is fixed.
    public static async Task<string> InstallWithDownload(string gameExe,Func<Task<byte[]>> download) {
      await Gate.WaitAsync().ConfigureAwait(false);
      string temporary=null;
      try {
        string exe=Path.GetFullPath(gameExe);
        if(!File.Exists(exe) || !string.Equals(Path.GetExtension(exe),".exe",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("맵을 받을 게임 실행 파일 위치를 먼저 선택하세요.");
        string mission=Path.Combine(Path.GetDirectoryName(exe),"Mission"),dest=Path.Combine(mission,FileName);
        if(File.Exists(dest)) {
          if(new FileInfo(dest).Length==Length && Digest(File.ReadAllBytes(dest))==Hash)return "맵 설치 확인: "+dest;
          throw new IOException("같은 이름의 다른 맵이 있어 덮어쓰지 않았습니다: "+dest+" / 기존 맵 이름을 변경한 뒤 다시 다운로드하세요.");
        }
        byte[] bytes=await download().ConfigureAwait(false);Validate(bytes);
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
