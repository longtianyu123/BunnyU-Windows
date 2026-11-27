using System.Diagnostics;

namespace BunnyU.Core;

public sealed record DownloadProgress(long Bytes,long? Total);
public sealed record DownloadResult(string Path,bool AlreadyExists);

public sealed class ChapterDownloader(HttpClient http)
{
    public static string UnavailableReason(Chapter chapter)
    {
        if(chapter.IsLocked)return "需要购买或会员权限";
        if(chapter.IsTrial)return "仅可试听，不能下载完整章节";
        if(!MediaAddress.IsDirectAudio(chapter.DownloadUrl??chapter.MediaUrl))return "没有可下载的音频文件地址";
        return "";
    }
    public static string SafeName(string value)
    {
        var invalid=Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var name=new string(value.Select(c=>char.IsControl(c)||invalid.Contains(c)?'_':c).Take(90).ToArray()).Trim().TrimEnd('.');
        if(name.Length==0)return "未命名";
        var stem=name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if(stem is "CON" or "PRN" or "AUX" or "NUL"||
            (stem.Length==4&&(stem.StartsWith("COM")||stem.StartsWith("LPT"))&&"123456789¹²³".Contains(stem[3])))name="_"+name;
        return name;
    }
    public static async Task<IReadOnlyList<Chapter>> AllChaptersAsync(IBunnyApi api,long albumId,UserSession? session,CancellationToken ct)
    {
        var result=new List<Chapter>();var ids=new HashSet<long>();
        for(var page=1;;page++)
        {
            ct.ThrowIfCancellationRequested();
            var next=await api.ChaptersAsync(albumId,page,session,ct);
            var added=0;foreach(var chapter in next.Items)if(ids.Add(chapter.Id)){result.Add(chapter);added++;}
            if(next.Items.Count==0||result.Count>=next.Total)return result;
            if(added==0)throw new BunnyApiException("章节分页重复，请稍后刷新或重新打开。");
        }
    }
    public async Task<DownloadResult> DownloadAsync(Chapter chapter,string directory,IProgress<DownloadProgress>? progress,CancellationToken ct)
    {
        var reason=UnavailableReason(chapter);if(reason.Length>0)throw new BunnyApiException(reason);
        ct.ThrowIfCancellationRequested();
        var target=Path.GetFullPath(directory);Directory.CreateDirectory(target);
        var temporary=Path.Combine(target,".bunny-"+Guid.NewGuid().ToString("N")+".part");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            // Media requests deliberately carry no account token to storage/CDN hosts.
            using var response=await http.GetAsync(chapter.DownloadUrl??chapter.MediaUrl,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode)throw new BunnyApiException($"下载失败（HTTP {(int)response.StatusCode}）。");
            var type=response.Content.Headers.ContentType?.MediaType??"";
            if(type.Contains("html")||type.Contains("json")||type.Contains("mpegurl"))throw new BunnyApiException("服务器没有返回音频文件。");
            await using var source=await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer=new byte[65536];var first=0;
            while(first<512)
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var n=await source.ReadAsync(buffer.AsMemory(first,512-first),timeout.Token);if(n==0)break;first+=n;
            }
            var extension=AudioExtension(buffer.AsSpan(0,first));
            var name=SafeName(chapter.Name);
            if(new[]{".mp3",".wav",".flac",".ogg",".m4a",".aac"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))name=SafeName(Path.GetFileNameWithoutExtension(name));
            var destination=Path.Combine(target,name+extension);
            if(File.Exists(destination))return new(destination,true);
            long bytes=first;var total=response.Content.Headers.ContentLength;
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                await file.WriteAsync(buffer.AsMemory(0,first),timeout.Token);
                var timer=Stopwatch.StartNew();progress?.Report(new(bytes,total));
                while(true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var n=await source.ReadAsync(buffer,timeout.Token);if(n==0)break;
                    await file.WriteAsync(buffer.AsMemory(0,n),timeout.Token);bytes+=n;
                    if(timer.ElapsedMilliseconds>=300){progress?.Report(new(bytes,total));timer.Restart();}
                }
                if(total.HasValue&&total.Value!=bytes)throw new BunnyApiException("音频下载不完整，请重试。");
                await file.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            try{File.Move(temporary,destination,false);}
            catch(IOException)when(File.Exists(destination)){return new(destination,true);}
            progress?.Report(new(bytes,total));return new(destination,false);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new BunnyApiException("下载连接超时，请重试。");}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static string AudioExtension(ReadOnlySpan<byte> data)
    {
        if(data.StartsWith("ID3"u8))return ".mp3";
        if(data.StartsWith("fLaC"u8))return ".flac";
        if(data.StartsWith("OggS"u8))return ".ogg";
        if(data.Length>=12&&data[..4].SequenceEqual("RIFF"u8)&&data.Slice(8,4).SequenceEqual("WAVE"u8))return ".wav";
        if(data.Length>=12&&data.Slice(4,4).SequenceEqual("ftyp"u8))return ".m4a";
        if(data.Length>=2&&data[0]==255&&(data[1]&0xe0)==0xe0)return (data[1]&0xf6)==0xf0?".aac":".mp3";
        throw new BunnyApiException("响应不是支持的音频文件，未保存；暂不支持仅有 HLS 分段的章节。");
    }
}
