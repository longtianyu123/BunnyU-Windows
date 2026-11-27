using System.Security.Cryptography;
using System.Text.Json;

namespace BunnyU.Core;

public sealed record AppRelease(Version Version,Uri Installer,string Sha256,string Notes,string Signature);
public sealed class AppUpdater(HttpClient http,string? trustedKey=null)
{
    public const string Repository="longtianyu123/BunnyU-Windows";
    public const string ManifestUrl="https://github.com/"+Repository+"/releases/latest/download/update.json";
    public const string TrustedPublicKey="MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2qgswPzhkIDBJvmiy+GGbl5jXYmHeQx7+k+WZNRnQnygu09Erg5wzKyaYjCa08whzmstX7PikYbfoGpiTv8EBg==";
    private static readonly string[] ProxyPrefixes=["https://gh-proxy.org/","https://ghfast.top/"];
    public static AppRelease ParseManifest(string json,string? publicKey=null)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var versionText=ResponseParser.Text(root,"version");
        if(!Version.TryParse(versionText,out var version)||version.Major<0)throw new BunnyApiException("更新版本信息无效。");
        var address=ResponseParser.Text(root,"installerUrl");
        if(!Uri.TryCreate(address,UriKind.Absolute,out var installer)||installer.Scheme!="https"||installer.Host!="github.com"||!installer.IsDefaultPort||installer.UserInfo.Length>0||
            !installer.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)||!installer.AbsolutePath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
            throw new BunnyApiException("更新安装包地址不属于本软件的发布仓库。");
        var sha=ResponseParser.Text(root,"sha256");if(sha.Length!=64||!sha.All(Uri.IsHexDigit))throw new BunnyApiException("更新安装包校验信息无效。");
        var notes=ResponseParser.Text(root,"notes");var signature=ResponseParser.Text(root,"signature");
        try
        {
            using var verifier=ECDsa.Create();verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey??TrustedPublicKey),out _);
            var payload=System.Text.Encoding.UTF8.GetBytes(string.Join("\n",version.ToString(),installer.AbsoluteUri,sha.ToLowerInvariant(),notes));
            if(!verifier.VerifyData(payload,Convert.FromBase64String(signature),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))throw new CryptographicException();
        }
        catch(Exception ex)when(ex is FormatException or CryptographicException){throw new BunnyApiException("更新清单签名无效，已阻止更新。");}
        return new(version,installer,sha,notes,signature);
    }
    public static bool IsNewer(Version candidate,Version installed)
    {
        static Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
        return Normalize(candidate)>Normalize(installed);
    }
    public async Task<AppRelease?> CheckAsync(Version installed,CancellationToken ct)
    {
        foreach(var address in new[]{ManifestUrl}.Concat(ProxyPrefixes.Select(prefix=>prefix+ManifestUrl)))
        {
            try
            {
                using var attempt=CancellationTokenSource.CreateLinkedTokenSource(ct);attempt.CancelAfter(TimeSpan.FromSeconds(15));
                var release=ParseManifest(await ReadManifestAsync(address,attempt.Token),trustedKey);return IsNewer(release.Version,installed)?release:null;
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)when(ex is HttpRequestException or IOException or OperationCanceledException or JsonException or BunnyApiException){}
        }
        throw new BunnyApiException("直连和备用更新通道均无法取得有效版本信息，请稍后重试。");
    }
    private async Task<string> ReadManifestAsync(string address,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,address);
        request.Headers.CacheControl=new(){NoCache=true};
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)throw new BunnyApiException("暂时无法取得更新信息，请稍后重试。");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();
        var chunk=new byte[8192];int count;
        while((count=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+count>262144)throw new BunnyApiException("更新清单过大。");await buffer.WriteAsync(chunk.AsMemory(0,count),ct);}
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task<string> DownloadAsync(AppRelease release,string folder,IProgress<DownloadProgress>? progress,CancellationToken ct)
    {
        // Revalidate public inputs before downloading an executable.
        release=ParseManifest(JsonSerializer.Serialize(new{version=release.Version.ToString(),installerUrl=release.Installer.AbsoluteUri,sha256=release.Sha256,notes=release.Notes,signature=release.Signature}),trustedKey);
        Exception? last=null;
        foreach(var address in new[]{release.Installer.AbsoluteUri}.Concat(ProxyPrefixes.Select(prefix=>prefix+release.Installer.AbsoluteUri)))
        {
            try{return await DownloadFromAsync(release,new Uri(address),folder,progress,ct);}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)when(ex is HttpRequestException or BunnyApiException){last=ex;}
        }
        throw new BunnyApiException("安装包下载未完成，直连和备用通道均失败。"+(last is BunnyApiException?last.Message:""));
    }
    private async Task<string> DownloadFromAsync(AppRelease release,Uri address,string folder,IProgress<DownloadProgress>? progress,CancellationToken ct)
    {
        Directory.CreateDirectory(folder);var target=Path.Combine(folder,"BunnyU-Setup-"+release.Version+".exe");var part=target+"."+Guid.NewGuid().ToString("N")+".part";
        using var stall=CancellationTokenSource.CreateLinkedTokenSource(ct);stall.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var probe=new HttpRequestMessage(HttpMethod.Get,address);probe.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,0);probe.Headers.AcceptEncoding.ParseAdd("identity");
            using var initial=await http.SendAsync(probe,HttpCompletionOption.ResponseHeadersRead,stall.Token);
            if(!initial.IsSuccessStatusCode&&initial.StatusCode!=System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)throw new BunnyApiException("安装包下载失败，请稍后重试。");
            var range=initial.Content.Headers.ContentRange;
            if(initial.StatusCode==System.Net.HttpStatusCode.PartialContent&&range?.From==0&&range.To==0&&range.Length is >=1048576 and <=536870912)
            {
                var size=range.Length.Value;initial.Dispose();
                try
                {
                    await ParallelUpdateDownload.DownloadAsync(http,address,part,size,progress,ct);
                    await using(var verified=new FileStream(part,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true))
                    {
                        var header=new byte[2];await verified.ReadExactlyAsync(header,ct);verified.Position=0;
                        var digest=await SHA256.HashDataAsync(verified,ct);
                        if(header[0]!='M'||header[1]!='Z'||!Convert.ToHexString(digest).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new BunnyApiException("安装包校验失败，已取消安装。");
                    }
                    ct.ThrowIfCancellationRequested();File.Move(part,target,true);return target;
                }
                catch(HttpRequestException){if(File.Exists(part))File.Delete(part);progress?.Report(new(0,size));}
            }
            // A server that ignores Range already returned the whole file. Reuse it.
            // Otherwise retry a standard GET when range transfer is unsupported.
            stall.CancelAfter(TimeSpan.FromSeconds(60));
            using var response=initial.StatusCode==System.Net.HttpStatusCode.OK?initial:await http.GetAsync(address,HttpCompletionOption.ResponseHeadersRead,stall.Token);
            if(!response.IsSuccessStatusCode)throw new BunnyApiException("安装包下载失败，请稍后重试。");
            var length=response.Content.Headers.ContentLength;if(length>512L*1024*1024)throw new BunnyApiException("安装包大小异常。");
            await using var source=await response.Content.ReadAsStreamAsync(stall.Token);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var bytes=new byte[65536];long total=0;var executableHeader=new byte[2];
            await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                int read;
                while((read=await source.ReadAsync(bytes,stall.Token))>0)
                {
                    stall.CancelAfter(TimeSpan.FromSeconds(60));
                    for(var i=0;i<read&&total+i<2;i++)executableHeader[(int)total+i]=bytes[i];
                    total+=read;if(total>512L*1024*1024)throw new BunnyApiException("安装包大小异常。");
                    hash.AppendData(bytes,0,read);await output.WriteAsync(bytes.AsMemory(0,read),stall.Token);progress?.Report(new(total,length));
                }
                await output.FlushAsync(ct);
            }
            if(total<4096||(length.HasValue&&total!=length)||executableHeader[0]!='M'||executableHeader[1]!='Z'||!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new BunnyApiException("安装包校验失败，已取消安装。");
            ct.ThrowIfCancellationRequested();File.Move(part,target,true);return target;
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new BunnyApiException("下载更新超时，请重试。");}
        finally{if(File.Exists(part))File.Delete(part);}
    }
}
