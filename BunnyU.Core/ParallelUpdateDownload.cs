using System.Net;
using System.Net.Http.Headers;

namespace BunnyU.Core;

internal static class ParallelUpdateDownload
{
    public static async Task DownloadAsync(HttpClient http,Uri address,string path,long length,IProgress<DownloadProgress>? progress,CancellationToken ct)
    {
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.ReadWrite,65536,FileOptions.Asynchronous|FileOptions.RandomAccess);
        file.SetLength(length);var counts=new long[5];var gate=new object();var lastReport=Environment.TickCount64;
        async Task Segment(int index)
        {
            using var stall=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);stall.CancelAfter(TimeSpan.FromSeconds(60));
            long start=length*index/5,end=length*(index+1)/5-1,received=0;
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,address);request.Headers.Range=new RangeHeaderValue(start,end);request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,stall.Token);
                var range=response.Content.Headers.ContentRange;
                if(response.StatusCode!=HttpStatusCode.PartialContent||range?.Unit!="bytes"||range.From!=start||range.To!=end||range.Length!=length)
                    throw new HttpRequestException("更新服务器未正确返回请求的分段。");
                await using var stream=await response.Content.ReadAsStreamAsync(stall.Token);var buffer=new byte[65536];int read;
                while((read=await stream.ReadAsync(buffer,stall.Token))>0)
                {
                    stall.CancelAfter(TimeSpan.FromSeconds(60));if(received+read>end-start+1)throw new HttpRequestException("更新分段长度异常。");
                    await RandomAccess.WriteAsync(file.SafeFileHandle,buffer.AsMemory(0,read),start+received,stall.Token);received+=read;
                    lock(gate){counts[index]=received;if(Environment.TickCount64-lastReport>=200){lastReport=Environment.TickCount64;progress?.Report(new(counts.Sum(),length));}}
                }
                if(received!=end-start+1)throw new HttpRequestException("更新分段不完整。");
            }
            catch{cancel.Cancel();throw;}
        }
        try{await Task.WhenAll(Enumerable.Range(0,5).Select(Segment));await file.FlushAsync(ct);progress?.Report(new(length,length));}
        catch{ct.ThrowIfCancellationRequested();throw new HttpRequestException("多线程更新下载未完成。");}
    }
}
