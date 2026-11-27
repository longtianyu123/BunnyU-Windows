using System.Net;
using System.Text;
using System.Text.Json;
using BunnyU.Core;

internal static class DownloadTests
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        using(var document=JsonDocument.Parse("""{"code":0,"data":{"total":1,"data":[{"id":1,"soundInfo":{"name":"测试","url_m3u8_normal":"https://test.invalid/play.m3u8","url_mp3_normal":"https://test.invalid/download.mp3"}}]}}"""))
        {
            var parsed=ResponseParser.Chapters(document.RootElement,1).Items.Single();
            check(parsed.MediaUrl.EndsWith("m3u8")&&parsed.DownloadUrl!.EndsWith("mp3"),"download prefers file while playback preserves HLS priority");
        }
        var root=Path.Combine(Path.GetTempPath(),"bunny-download-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler=new AudioHandler();using var http=new HttpClient(handler);var downloader=new ChapterDownloader(http);
            var chapter=new Chapter(42,"../CON:测试/章节", "",10,"https://test.invalid/audio",false,false,0);
            var result=await downloader.DownloadAsync(chapter,root,null,default);
            check(!result.AlreadyExists&&Path.GetDirectoryName(result.Path)==root&&File.ReadAllBytes(result.Path).SequenceEqual(handler.Bytes),"audio bytes saved intact inside selected folder with safe filename");
            var duplicate=await downloader.DownloadAsync(chapter,root,null,default);
            check(duplicate.AlreadyExists&&File.ReadAllBytes(result.Path).SequenceEqual(handler.Bytes),"existing audio is preserved without overwrite");
            var before=handler.Count;
            foreach(var restricted in new[]{chapter with{RequiresEntitlement=true},chapter with{RequiresEntitlement=true,TrialSeconds=5}})
            {
                try{await downloader.DownloadAsync(restricted,root,null,default);throw new Exception("restriction ignored");}
                catch(BunnyApiException){check(handler.Count==before,"locked or trial-only download makes no media request");}
            }
            handler.Bytes=Encoding.UTF8.GetBytes("<html>gateway error</html>");
            try{await downloader.DownloadAsync(chapter with{Id=43,Name="错误响应"},root,null,default);throw new Exception("HTML accepted");}
            catch(BunnyApiException){check(Directory.GetFiles(root).Length==1,"HTML success response is not saved as audio");}
            handler.Bytes=Encoding.UTF8.GetBytes("ID3"+new string('x',2048));handler.DeclaredLength=10000;
            try{await downloader.DownloadAsync(chapter with{Id=44,Name="截断音频"},root,null,default);throw new Exception("truncated accepted");}
            catch(BunnyApiException){check(Directory.GetFiles(root).Length==1,"truncated response removes partial file");}
            handler.DeclaredLength=null;
            using var canceled=new CancellationTokenSource();canceled.Cancel();before=handler.Count;
            try{await downloader.DownloadAsync(chapter,root,null,canceled.Token);throw new Exception("cancellation ignored");}
            catch(OperationCanceledException){check(handler.Count==before&&Directory.GetFiles(root).Length==1,"canceled task neither requests nor creates files");}
            using var mid=new CancellationTokenSource();
            var progress=new ImmediateProgress(_=>mid.Cancel());
            try{await downloader.DownloadAsync(chapter with{Id=45,Name="取消音频"},root,progress,mid.Token);throw new Exception("mid cancel ignored");}
            catch(OperationCanceledException){check(Directory.GetFiles(root).Length==1,"cancellation during transfer removes only temporary file");}
            check(ChapterDownloader.UnavailableReason(chapter with{MediaUrl="https://test.invalid/audio.m3u8"}).Length>0,"HLS playlist cannot be mistaken for a complete audio file");
            var pages=new ChapterPages();var all=await ChapterDownloader.AllChaptersAsync(pages,1,null,default);
            check(pages.Calls==2&&all.Select(c=>c.Id).SequenceEqual(new long[]{1,2,3}),"batch loads remaining pages and deduplicates chapters");
            pages.Repeat=true;pages.Calls=0;
            try{await ChapterDownloader.AllChaptersAsync(pages,1,null,default);throw new Exception("repeated pagination accepted");}
            catch(BunnyApiException){check(pages.Calls==2,"repeating pagination stops rather than looping forever");}
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class ImmediateProgress(Action<DownloadProgress> callback):IProgress<DownloadProgress>{public void Report(DownloadProgress value)=>callback(value);}
    private sealed class AudioHandler:HttpMessageHandler
    {
        public byte[] Bytes=Encoding.UTF8.GetBytes("ID3"+new string('x',2048));public long? DeclaredLength;public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Count++;var content=new ByteArrayContent(Bytes);if(DeclaredLength.HasValue)content.Headers.ContentLength=DeclaredLength;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
    private sealed class ChapterPages:IBunnyApi
    {
        public int Calls;public bool Repeat;
        public Task<ChapterPage> ChaptersAsync(long albumId,int page,UserSession? session,CancellationToken cancellationToken=default)
        {
            Calls++;var ids=page==1||Repeat?new long[]{1,2}:new long[]{2,3};
            return Task.FromResult(new ChapterPage(ids.Select(id=>new Chapter(id,"章","",10,"https://test.invalid/a.mp3",false,false,0)).ToArray(),page,3));
        }
        public Task<UserSession> AccountAsync(UserSession s,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task SendCodeAsync(string m,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<UserSession> LoginAsync(string m,string c,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SearchPage> SearchAsync(string k,int p,UserSession? s,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<IReadOnlyList<RecommendationSection>> RecommendationsAsync(UserSession? s,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<IReadOnlyList<AlbumCategory>> CategoriesAsync(CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SearchPage> CategoryAlbumsAsync(string c,int p,UserSession? s,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
