using System.Net;
using System.Text;
using System.Text.Json;
using BunnyU.Core;

var passed=0;
void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);Console.WriteLine("PASS "+label);passed++;}
JsonDocument Json(string value)=>JsonDocument.Parse(value);
using(var d=Json("""{"code":0,"data":{"data":[{"title":"推荐","recommend_text":"热门","children":[{"id":999,"album_id":91,"name":"作品"},{"album_id":91,"name":"重复"}]}]}}"""))
{
    var sections=ResponseParser.Recommendations(d.RootElement);Check(sections.Count==1&&sections[0].Albums.Count==1&&sections[0].Albums[0].Id==91,"recommendation uses album_id, deduplicates within section");
}
using(var d=Json("""{"code":200,"data":{"cond":[{"field":"unrelated","data":[]},{"field":"album_sort_id","data":[{"name":"古风","value":"1"},{"name":"现代","value":"5"}]}]}}"""))
    Check(ResponseParser.Categories(d.RootElement).Select(c=>c.Id).SequenceEqual(new[]{"1","5"}),"categories use server values rather than row indexes");
using(var d=Json("""{"code":0,"data":{"total":104,"data":[{"id":99,"album_id":1480,"name":"作品"}]}}"""))
{
    var page=ResponseParser.CategoryAlbums(d.RootElement,2);Check(page.Total==104&&page.Page==2&&page.Items[0].Id==1480,"category pagination and album identity");
}

using(var d=Json("""{"code":0,"data":{"albums":[{"album_id":91,"name":"葡萄"},{"album_id":91,"name":"重复"},{"album_id":0,"name":"空"}],"album":{"album_id":91,"name":"重复"},"recommendAlbum":[{"album_id":99,"name":"推荐"}],"albumTotal":1}}"""))
{
    var page=ResponseParser.Search(d.RootElement,1);Check(page.Items.Count==1&&page.Items[0].Id==91,"search deduplicates, excludes recommendations/zero IDs");
}
using(var d=Json("""{"code":0,"data":{"albums":null,"album":{"album_id":91,"name":"exact"},"albumTotal":1}}"""))
    Check(ResponseParser.Search(d.RootElement,1).Items.Count==1,"highlighted single search result");
using(var d=Json("""{"code":200,"data":{"userId":"42","token":"secret-token","mobile":"13800000000"}}"""))
{
    var s=ResponseParser.Login(d.RootElement,"");Check(s.UserId==42&&s.Token=="secret-token"&&s.DisplayName=="138****0000","login response and masked account name");
}
using(var d=Json("""{"code":0,"data":{"id":42,"token":"mock-token","nickname":"听友小兔"}}"""))
    Check(ResponseParser.Login(d.RootElement,"13800000000").DisplayName=="听友小兔","login nickname used as account display name");
using(var d=Json("""{"code":200,"data":{"code":0,"data":{"user_id":42,"nickname":"新昵称"}}}"""))
{
    var s=ResponseParser.Account(d.RootElement,new UserSession(42,"original-token","13800000000"));
    Check(s.DisplayName=="新昵称"&&s.Token=="original-token"&&s.Mobile=="13800000000","profile updates nickname without replacing credentials");
}
try{using var d=Json("""{"code":0,"data":{"id":99,"nickname":"其他账号"}}""");ResponseParser.Account(d.RootElement,new UserSession(42,"token","13800000000"));throw new Exception("wrong identity accepted");}
catch(BunnyApiException){Check(true,"profile identity mismatch rejected");}
var legacy=JsonSerializer.Deserialize<UserSession>("""{"UserId":42,"Token":"token","Mobile":"13800000000"}""");
Check(legacy?.DisplayName=="138****0000","old saved sessions without nickname remain readable");
try{using var d=Json("""{"code":200,"data":{"userId":42}}""");ResponseParser.Login(d.RootElement,"");throw new Exception("missing token accepted");}catch(BunnyApiException){Check(true,"incomplete login cannot become authenticated session");}
using(var d=Json("""{"code":200,"msg":"ok","data":{"code":0,"msg":"ok","data":{"id":42,"token":"nested-token","mobile":"13800000000"}}}"""))
{
    var s=ResponseParser.Login(d.RootElement,"");Check(s.UserId==42&&s.Token=="nested-token","nested auth transport and business envelopes");
}
using(var d=Json("""{"code":0,"data":{"data":{"userId":"42","token":"nested-token"}}}"""))
    Check(ResponseParser.Login(d.RootElement,"13800000000").Mobile=="13800000000","nested data without inner code and phone fallback");
try{using var d=Json("""{"code":200,"data":{"code":400,"msg":"验证码已过期","data":null}}""");ResponseParser.Login(d.RootElement,"");throw new Exception("inner error ignored");}
catch(BunnyApiException ex){Check(ex.Message=="验证码已过期","inner business error retained");}
try{using var d=Json("""{"code":200,"data":{"code":401,"msg":"登录失败","id":42,"token":"must-not-accept"}}""");ResponseParser.Login(d.RootElement,"");throw new Exception("error credentials accepted");}
catch(BunnyApiException ex){Check(ex.RequiresLogin,"inner error checked before credentials");}
try{using var d=Json("""{"code":0,"data":{"otherUser":{"id":42,"token":"unrelated"}}}""");ResponseParser.Login(d.RootElement,"");throw new Exception("arbitrary nested credentials accepted");}
catch(BunnyApiException ex){Check(ex.Message.Contains("不代表验证码错误"),"unknown structure never blames OTP or accepts unrelated credentials");}
try{using var d=Json("""{"code":0,"data":{"id":42,"token":"   "}}""");ResponseParser.Login(d.RootElement,"");throw new Exception("whitespace token accepted");}
catch(BunnyApiException){Check(true,"blank token is not accepted");}
try{using var d=Json("""{"code":401,"msg":"登录过期"}""");ResponseParser.Data(d.RootElement);throw new Exception("bad code accepted");}catch(BunnyApiException ex){Check(ex.RequiresLogin&&ex.Message=="登录过期","API login error preserved");}

using(var d=Json("""{"code":0,"data":{"total":3,"data":[{"album_sound_id":1,"is_vip":1,"is_free":1,"freeDuration":60,"soundInfo":{"name":"trial","duration":120,"url":"http://media.test/a.mp3"},"url_m3u8_normal":"https://media.test/a.m3u8"},{"album_sound_id":2,"is_vip":1,"freeDuration":0,"soundInfo":{"name":"locked","url":"https://media.test/b.mp3"}},{"album_sound_id":3,"is_vip":0,"soundInfo":{"name":"free","duration":10,"url":"https://media.test/c.mp3"}}]}}"""))
{
    var list=ResponseParser.Chapters(d.RootElement,1).Items;
    Check(list.Count==3&&list[0].MediaUrl.EndsWith(".m3u8"),"nested soundInfo and HLS priority");
    Check(list[0].IsTrial&&list[0].PlaybackLimit==60,"trial bound retained");
    Check(list[1].IsLocked&&list[1].PlaybackLimit is null,"restricted track without trial remains locked");
    Check(!list[2].IsTrial&&!list[2].IsLocked,"public chapter playable");
}
using(var d=Json("""{"code":0,"data":{"data":[{"id":1,"is_vip":1,"is_buyout":1,"soundInfo":{"url":"https://media.test/a.mp3"}}]}}"""))
    Check(ResponseParser.Chapters(d.RootElement,1).Items[0].IsLocked,"buyout flag is not a purchase grant");
const string cipher="AAECAwQFBgcICQoLDA0ODz6+QAw/wWrmUkstqqSTUpTQTOL/JejdVSAmFRdBcqbEW3cIFsfHEeiShKgnKjAElw==";
Check(MediaAddress.DecodePayload(cipher)=="http://media.example.test/test.mp3","AES-CBC address envelope independent known vector");
Check(!MediaAddress.IsHttp("file:///C:/private")&&!MediaAddress.IsHttp("vlc://quit")&&MediaAddress.IsHttp("https://media.test/a"),"only HTTP(S) media addresses accepted");
using(var d=Json("""{"urlBody":"not-base64!"}"""))Check(MediaAddress.Resolve(d.RootElement,d.RootElement)=="","invalid address envelope handled without crashing");

var handler=new Recorder();using var http=new HttpClient(handler);var api=new BunnyApi(http,"0123456789abcdef");
handler.Reply="""{"code":200,"msg":"ok","data":true}""";
await api.SendCodeAsync("13800000000");
Check(handler.Uri!.AbsolutePath=="/v1/user/send/msgcode"&&handler.Method==HttpMethod.Post,"SMS route uses POST");
Check(handler.Fields["scene"]=="1"&&handler.Fields["sendType"]=="1"&&handler.Fields["account"]=="13800000000","login SMS scene=1 (not password recovery scene=3)");
Check(handler.MediaType=="multipart/form-data","SMS uses multipart matching client");
Check(!handler.QuotedBoundary&&!handler.FieldContentType&&handler.Body.Contains("name=\"scene\""),"Dio-compatible serialized boundary and field headers");
Check(handler.Body.Contains("name=\"account\"\r\n\r\n13800000000"),"wire form field has quoted name and no text/plain header");
handler.Reply="""{"code":200,"data":{"userId":42,"token":"token","mobile":"13800000000"}}""";
await api.LoginAsync("13800000000","123456");
Check(handler.Uri!.AbsolutePath=="/v1/user/login/pwdOrCode"&&handler.Body.Contains("0123456789abcdef")&&handler.Body.Contains("123456"),"OTP login field submission through mock transport only");
handler.Reply="""{"code":0,"data":{"albums":[],"albumTotal":0}}""";
await api.SearchAsync("A&B 中",2,new UserSession(42,"raw-token","13800000000"));
Check(handler.Uri!.Query.Contains("A%26B")&&handler.Uri.Query.Contains("page=2")&&handler.Uri.Query.Contains("userId=42"),"search query encoded and paged");
Check(handler.Authorization=="raw-token","normal API uses original raw authorization header");
handler.Reply="<html><title>from-data请求error</title><div>宝塔WAF提醒您,from-data 请求异常</div></html>";
var beforeHtml=handler.Count;
try{await api.SendCodeAsync("13800000000");throw new Exception("HTML accepted");}
catch(BunnyApiException ex){Check(ex.Message.Contains("表单请求格式")&&handler.Count==beforeHtml+1,"form rejection identified with no automatic resend");}
handler.Reply="<html>gateway error</html>";
try{await api.SendCodeAsync("13800000000");throw new Exception("HTML accepted");}
catch(BunnyApiException ex){Check(ex.Message.Contains("网页"),"HTML success-status response is not a successful SMS result");}
handler.Reply="";
try{await api.SendCodeAsync("13800000000");throw new Exception("empty accepted");}
catch(BunnyApiException ex){Check(ex.Message.Contains("空响应"),"empty response remains unconfirmed");}
handler.Status=HttpStatusCode.BadRequest;handler.Reply="""{"code":500,"msg":"field account is not set"}""";
try{await api.SendCodeAsync("13800000000");throw new Exception("validation ignored");}
catch(BunnyApiException ex){Check(ex.Message=="field account is not set","HTTP 400 JSON validation message retained");}
handler.Status=HttpStatusCode.OK;handler.Reply="""{"code":200,"msg":"请求成功","data":{"data":""}}""";
await api.SendCodeAsync("13800000000");Check(true,"observed SMS response envelope accepted");
handler.Status=HttpStatusCode.TooManyRequests;
try{await api.SendCodeAsync("13800000000");throw new Exception("429 ignored");}catch(BunnyApiException ex){Check(ex.Message.Contains("频繁"),"rate limit surfaces without auto retry");}
var count=handler.Count;
try{await api.SendCodeAsync("123");throw new Exception("phone validation missing");}catch(BunnyApiException){Check(handler.Count==count,"invalid phone never sends a request");}

if(args.Contains("--live"))
{
    using var live=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    var publicApi=new BunnyApi(live,"desktop-smoke-01");
    var recommendations=await publicApi.RecommendationsAsync(null);Check(recommendations.Any(s=>s.Albums.Count>0),"live recommendations");
    var categories=await publicApi.CategoriesAsync();Check(categories.Count>0,"live categories");
    var categoryAlbums=await publicApi.CategoryAlbumsAsync(categories[0].Id,1,null);Check(categoryAlbums.Items.Count>0,"live category albums");
    var hits=await publicApi.SearchAsync("葡萄",1,null);
    Check(hits.Items.Count>0,"live anonymous search (read-only)");
    var chapters=await publicApi.ChaptersAsync(hits.Items[0].Id,1,null);
    Check(chapters.Items.Any(c=>!c.IsLocked&&!c.IsTrial&&MediaAddress.IsHttp(c.MediaUrl)),"live public chapter address (read-only)");
    var free=chapters.Items.First(c=>!c.IsLocked&&!c.IsTrial&&MediaAddress.IsHttp(c.MediaUrl));
    File.WriteAllText("public-playback-url.txt",free.MediaUrl);
    if(args.Contains("--download-live"))
    {
        var sample=chapters.Items.Where(c=>ChapterDownloader.UnavailableReason(c).Length==0&&c.DurationSeconds is >0 and <=180).OrderBy(c=>c.DurationSeconds).First();
        var directory=Path.Combine(Path.GetTempPath(),"bunny-live-download-"+Guid.NewGuid().ToString("N"));
        using var media=new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan};
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var saved=await new ChapterDownloader(media).DownloadAsync(sample,directory,null,deadline.Token);
            Check(new FileInfo(saved.Path).Length>512,"live public preview downloads as an actual audio file");
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
await DownloadTests.RunAsync(Check);
DownloadHistoryTests.Run(Check);
DownloadSettingsTests.Run(Check);
await UpdateTests.RunAsync(Check);
PlaybackQualityTests.Run(Check);
Console.WriteLine($"ALL PASSED: {passed}");

sealed class Recorder:HttpMessageHandler
{
    public string Reply="{}",Body="",MediaType="",Authorization="";public bool QuotedBoundary,QuotedNames,FieldContentType;public Dictionary<string,string> Fields=new();public Uri? Uri;public HttpMethod? Method;public int Count;public HttpStatusCode Status=HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        Count++;Uri=request.RequestUri;Method=request.Method;Body=request.Content is null?"":await request.Content.ReadAsStringAsync(cancellationToken);MediaType=request.Content?.Headers.ContentType?.MediaType??"";
        Fields.Clear();QuotedNames=true;FieldContentType=false;
        QuotedBoundary=request.Content?.Headers.ContentType?.Parameters.FirstOrDefault(p=>p.Name=="boundary")?.Value?.StartsWith('"')==true;
        if(request.Content is MultipartFormDataContent multipart)foreach(var part in multipart)
        {
            QuotedNames&=part.Headers.ContentDisposition!.Name!.StartsWith('"');FieldContentType|=part.Headers.ContentType is not null;
            Fields[part.Headers.ContentDisposition.Name.Trim('"')]=await part.ReadAsStringAsync(cancellationToken);
        }
        Authorization=request.Headers.TryGetValues("Authorization",out var values)?values.First():"";
        return new HttpResponseMessage(Status){Content=new StringContent(Reply,Encoding.UTF8,"application/json")};
    }
}
