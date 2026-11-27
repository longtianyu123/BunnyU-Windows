using BunnyU.Core;

namespace BunnyU.WinForms;

// Owned by MainForm. Media transfers outlive the chapter selection dialog.
internal sealed class DownloadRegistry:IDisposable
{
    private sealed class Batch(CancellationTokenSource cancellation,int remaining)
    {public CancellationTokenSource Cancellation=cancellation;public int Remaining=remaining;public bool Successful=true;}
    private sealed record Pending(DownloadRecord Record,Chapter Chapter,Batch Batch);
    public DownloadHistory History{get;}
    private readonly HttpClient _http;
    private readonly Queue<Pending> _queue=[];
    private readonly Dictionary<Guid,Batch> _batches=[];
    private Task _pump=Task.CompletedTask;
    private bool _stopping;
    private int _concurrency;
    private TaskCompletionSource _wake=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsBusy=>!_pump.IsCompleted;
    public Task WhenIdle=>_pump;
    public event Action? BatchCompleted;
    public DownloadRegistry(string path,HttpClient? http=null,int concurrency=1)
    {
        History=new(path);_http=http??new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan};
        SetConcurrency(concurrency);
    }
    public void SetConcurrency(int value)
    {
        if(value is <1 or >5)throw new ArgumentOutOfRangeException(nameof(value));
        _concurrency=value;_wake.TrySetResult();
    }
    public IReadOnlyList<DownloadRecord> Enqueue(Album album,IReadOnlyList<Chapter> chapters,string directory)
    {
        if(_stopping)throw new InvalidOperationException("程序正在退出，无法添加下载。");
        if(chapters.Count==0)throw new BunnyApiException("请先勾选可下载的章节。");
        foreach(var chapter in chapters){var reason=ChapterDownloader.UnavailableReason(chapter);if(reason.Length>0)throw new BunnyApiException(reason);}
        var records=History.AddBatch(album,chapters,Path.GetFullPath(directory));
        var batch=new Batch(new CancellationTokenSource(),records.Count);_batches.Add(records[0].BatchId,batch);
        for(var i=0;i<records.Count;i++)_queue.Enqueue(new(records[i],chapters[i],batch));
        if(_pump.IsCompleted)_pump=RunAsync();
        else _wake.TrySetResult();
        return records;
    }
    public bool CanCancel(DownloadRecord record)=>record.State is DownloadState.Waiting or DownloadState.Running&&_batches.ContainsKey(record.BatchId);
    public void Cancel(DownloadRecord record)
    {
        if(!CanCancel(record)||!_batches.TryGetValue(record.BatchId,out var batch))return;
        batch.Cancellation.Cancel();
        foreach(var waiting in History.Records.Where(r=>r.BatchId==record.BatchId&&r.State==DownloadState.Waiting).ToArray())
            History.Update(waiting.Id,DownloadState.Canceled,"已取消，未开始下载。");
    }
    private async Task RunAsync()
    {
        await Task.Yield();
        var running=new List<Task>();
        while(_queue.Count>0||running.Count>0)
        {
            while(running.Count<_concurrency&&_queue.TryDequeue(out var pending))running.Add(RunOneAsync(pending));
            if(running.Count==0)continue;
            if(_wake.Task.IsCompleted)_wake=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.WhenAny(running.Append(_wake.Task));
            foreach(var done in running.Where(t=>t.IsCompleted).ToArray()){await done;running.Remove(done);}
        }
    }
    private async Task RunOneAsync(Pending pending)
    {
            var downloader=new ChapterDownloader(_http);
            var record=pending.Record;var ct=pending.Batch.Cancellation.Token;var finished=false;
            try
            {
                ct.ThrowIfCancellationRequested();History.Update(record.Id,DownloadState.Running,"正在下载");
                var progress=new Progress<DownloadProgress>(value=>
                {if(!finished&&!ct.IsCancellationRequested)History.Update(record.Id,DownloadState.Running,"正在下载",progress:value);});
                var result=await downloader.DownloadAsync(pending.Chapter,record.Directory,progress,ct);
                finished=true;long? size=null;try{size=new FileInfo(result.Path).Length;}catch(IOException){}catch(UnauthorizedAccessException){}
                History.Update(record.Id,DownloadState.Completed,result.AlreadyExists?"已存在，跳过":"下载完成",result.Path,size.HasValue?new DownloadProgress(size.Value,size):null);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){finished=true;pending.Batch.Successful=false;History.Update(record.Id,DownloadState.Canceled,"已取消，已完成的文件保留。");}
            catch(Exception ex){finished=true;pending.Batch.Successful=false;History.Update(record.Id,DownloadState.Failed,MainForm.Error(ex));}
            finally
            {
                finished=true;
                if(--pending.Batch.Remaining==0)
                {
                    _batches.Remove(record.BatchId);pending.Batch.Cancellation.Dispose();
                    if(pending.Batch.Successful&&!_stopping)BatchCompleted?.Invoke();
                }
            }
    }
    public async Task StopAsync()
    {
        _stopping=true;foreach(var batch in _batches.Values.ToArray())batch.Cancellation.Cancel();await _pump;
    }
    public void Dispose(){foreach(var batch in _batches.Values){batch.Cancellation.Cancel();batch.Cancellation.Dispose();}_http.Dispose();}
}
