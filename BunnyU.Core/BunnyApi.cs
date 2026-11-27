using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace BunnyU.Core;

public sealed class BunnyApi(HttpClient client, string deviceId) : IBunnyApi
{
    public const string AuthBase = "https://api.zuidie.net/";
    public const string ContentBase = "https://bunny.zuidie.net/";
    public async Task<IReadOnlyList<RecommendationSection>> RecommendationsAsync(UserSession? session,CancellationToken cancellationToken=default)
    {
        using var doc=await GetAsync(ContentBase+$"v1/album/audioDramaRecPage?userId={session?.UserId??0}",session,cancellationToken);
        return ResponseParser.Recommendations(doc.RootElement);
    }
    public async Task<IReadOnlyList<AlbumCategory>> CategoriesAsync(CancellationToken cancellationToken=default)
    {
        using var doc=await GetAsync(AuthBase+"v1/album/search/condition",null,cancellationToken);
        return ResponseParser.Categories(doc.RootElement);
    }
    public async Task<SearchPage> CategoryAlbumsAsync(string category,int page,UserSession? session,CancellationToken cancellationToken=default)
    {
        using var doc=await GetAsync(ContentBase+$"v1/album/index/byCate?album_sort_id={Uri.EscapeDataString(category)}&page={Math.Max(1,page)}&pageSize=20&userId={session?.UserId??0}",session,cancellationToken);
        return ResponseParser.CategoryAlbums(doc.RootElement,page);
    }
    public async Task<UserSession> AccountAsync(UserSession session,CancellationToken cancellationToken=default)
    {
        using var doc=await GetAsync(AuthBase+$"v1/user/info/{session.UserId}?userId={session.UserId}",session,cancellationToken);
        return ResponseParser.Account(doc.RootElement,session);
    }
    public async Task SendCodeAsync(string mobile, CancellationToken cancellationToken = default)
    {
        ValidatePhone(mobile);
        // LoginBottomSheet / TelLoginScreen use Api.getVerificationCode (scene=1).
        // CommonApi.sendVerificationCode (scene=3) belongs to password recovery.
        using var doc = await PostAsync(AuthBase + "v1/user/send/msgcode", new() { ["account"] = mobile, ["scene"] = "1", ["sendType"] = "1" }, cancellationToken);
        ResponseParser.Data(doc.RootElement);
    }
    public async Task<UserSession> LoginAsync(string mobile, string code, CancellationToken cancellationToken = default)
    {
        ValidatePhone(mobile);
        if (code.Length is < 4 or > 8 || !code.All(char.IsAsciiDigit)) throw new BunnyApiException("请输入正确的短信验证码。");
        using var doc = await PostAsync(AuthBase + "v1/user/login/pwdOrCode", new() {
            ["mobile"] = mobile, ["code"] = code, ["password"] = "", ["type"] = "android", ["channel"] = "1", ["deviceId"] = deviceId
        }, cancellationToken);
        return ResponseParser.Login(doc.RootElement, mobile);
    }
    public async Task<SearchPage> SearchAsync(string keyword, int page, UserSession? session, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) throw new BunnyApiException("先输入作品名称或关键词。");
        var url = ContentBase + $"v1/search/all?keyword={Uri.EscapeDataString(keyword.Trim())}&page={Math.Max(1,page)}&pageSize=20&userId={session?.UserId ?? 0}";
        using var doc = await GetAsync(url, session, cancellationToken);
        return ResponseParser.Search(doc.RootElement, page);
    }
    public async Task<ChapterPage> ChaptersAsync(long albumId, int page, UserSession? session, CancellationToken cancellationToken = default)
    {
        if (albumId <= 0) throw new BunnyApiException("作品编号无效。");
        var url = ContentBase + $"v1/album/sound/index?albumId={albumId}&page={Math.Max(1,page)}&pageSize=50&userId={session?.UserId ?? 0}&sort=0";
        using var doc = await GetAsync(url, session, cancellationToken);
        return ResponseParser.Chapters(doc.RootElement, page);
    }
    private Task<JsonDocument> GetAsync(string url, UserSession? session, CancellationToken ct)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, url), session, ct);
    private Task<JsonDocument> PostAsync(string url, Dictionary<string,string> fields, CancellationToken ct)
    {
        // Match the app's Dio form encoding: token boundary, quoted field names,
        // and no per-field Content-Type. The server rejects the .NET defaults
        // with an HTTP 200 HTML form-data error instead of a JSON response.
        var form = new MultipartFormDataContent("----bunny-form-"+Guid.NewGuid().ToString("N"));
        foreach(var parameter in form.Headers.ContentType!.Parameters)
            if(parameter.Name=="boundary")parameter.Value=parameter.Value!.Trim('"');
        foreach (var (key,value) in fields)
        {
            var part=new StringContent(value,Encoding.UTF8);
            part.Headers.ContentType=null;
            part.Headers.ContentDisposition=new ContentDispositionHeaderValue("form-data"){Name="\""+key+"\""};
            form.Add(part);
        }
        return SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = form }, null, ct);
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, UserSession? session, CancellationToken ct)
    {
        using (request)
        {
            request.Headers.UserAgent.ParseAdd("BunnyUWindows/0.1");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Legacy server protocol values, not a collection of real Android device identifiers.
            request.Headers.TryAddWithoutValidation("platform", "Android");
            request.Headers.TryAddWithoutValidation("version", "2.9.7");
            request.Headers.TryAddWithoutValidation("device", "Windows Desktop");
            request.Headers.TryAddWithoutValidation("Android-Id", deviceId);
            if (session is not null) request.Headers.TryAddWithoutValidation("Authorization", session.Token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new BunnyApiException("登录状态已失效或没有访问权限，请重新登录。", true);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new BunnyApiException("请求过于频繁，请稍后再试。");
            if (response.Content.Headers.ContentLength > 8 * 1024 * 1024) throw new BunnyApiException("响应数据过大，请减少查询范围。");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + read > 8 * 1024 * 1024) throw new BunnyApiException("响应数据过大。");
                await buffer.WriteAsync(chunk.AsMemory(0,read),ct);
            }
            var bytes=buffer.ToArray();
            // UTF-8 BOM is tolerated, but HTML/error text is never treated as success.
            var offset=bytes.Length>=3&&bytes[0]==0xEF&&bytes[1]==0xBB&&bytes[2]==0xBF?3:0;
            JsonDocument document;
            try { document=JsonDocument.Parse(bytes.AsMemory(offset)); }
            catch (JsonException)
            {
                var prefix=Encoding.UTF8.GetString(bytes,0,Math.Min(bytes.Length,4096));
                if(prefix.Contains("WAF",StringComparison.OrdinalIgnoreCase)&&
                    (prefix.Contains("form-data",StringComparison.OrdinalIgnoreCase)||prefix.Contains("from-data",StringComparison.OrdinalIgnoreCase)))
                    throw new BunnyApiException("服务器拒绝了表单请求格式（防护页），本次未确认成功；请使用最新版本，不要连续重发验证码。");
                if(prefix.Contains("<html",StringComparison.OrdinalIgnoreCase)||prefix.Contains("<!doctype",StringComparison.OrdinalIgnoreCase))
                    throw new BunnyApiException($"服务器返回网页而不是接口数据（HTTP {(int)response.StatusCode}）。本次操作未确认成功。");
                throw new BunnyApiException(bytes.Length==0?"服务器返回了空响应，本次操作未确认成功。":"服务器响应不是有效 JSON，本次操作未确认成功。");
            }
            if(!response.IsSuccessStatusCode)
            {
                using(document)
                {
                    // Preserve useful server validation messages, including HTTP 400.
                    ResponseParser.Data(document.RootElement);
                    throw new BunnyApiException($"服务暂不可用（HTTP {(int)response.StatusCode}），请稍后再试。");
                }
            }
            return document;
        }
    }
    public static void ValidatePhone(string mobile)
    {
        if (mobile.Length != 11 || mobile[0] != '1' || !mobile.All(char.IsAsciiDigit)) throw new BunnyApiException("请输入 11 位手机号。");
    }
}
