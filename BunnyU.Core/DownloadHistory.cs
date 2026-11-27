using System.Text.Json;

namespace BunnyU.Core;

public enum DownloadState { Waiting,Running,Completed,Failed,Canceled,Interrupted }
public sealed record DownloadRecord(Guid Id,Guid BatchId,long AlbumId,string AlbumName,long ChapterId,string ChapterName,
    string Directory,string? FilePath,DownloadState State,long Bytes,long? Total,string Message,DateTimeOffset UpdatedAt);

// Only metadata is persisted: never account credentials or signed media addresses.
public sealed class DownloadHistory
{
    private readonly string _path;
    private readonly List<DownloadRecord> _records=[];
    private bool _readFailed;
    public string? Error{get;private set;}
    public IReadOnlyList<DownloadRecord> Records=>_records.AsReadOnly();
    public event Action? Changed;
    public DownloadHistory(string path)
    {
        _path=path;
        try
        {
            if(File.Exists(path))
            {
                var records=JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(path))??throw new JsonException();
                if(records.Any(r=>r is null||r.AlbumName is null||r.ChapterName is null||r.Directory is null))throw new JsonException();
                _records.AddRange(records.Select(r=>r.State is DownloadState.Waiting or DownloadState.Running
                    ?r with{State=DownloadState.Interrupted,Message="上次运行未完成，请重新选择章节下载。"}:r));
            }
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException)
        {Error="下载记录读取失败，原文件保留；本次记录仅在当前运行期间可见。";_readFailed=true;}
    }
    public IReadOnlyList<DownloadRecord> AddBatch(Album album,IReadOnlyList<Chapter> chapters,string directory)
    {
        var batch=Guid.NewGuid();
        var added=chapters.Select(c=>new DownloadRecord(Guid.NewGuid(),batch,album.Id,album.Name,c.Id,c.Name,directory,null,
            DownloadState.Waiting,0,null,"等待下载",DateTimeOffset.Now)).ToArray();
        _records.InsertRange(0,added);Save();Changed?.Invoke();return added;
    }
    public void Update(Guid id,DownloadState state,string message,string? filePath=null,DownloadProgress? progress=null)
    {
        var index=_records.FindIndex(r=>r.Id==id);if(index<0)return;
        var previous=_records[index];
        _records[index]=previous with{State=state,Message=message,FilePath=filePath??previous.FilePath,
            Bytes=progress?.Bytes??previous.Bytes,Total=progress?.Total??previous.Total,UpdatedAt=DateTimeOffset.Now};
        // Progress is in-memory; write only state transitions/completions.
        if(progress is null||previous.State!=state)Save();
        Changed?.Invoke();
    }
    public static string StateName(DownloadState state)=>state switch
    {
        DownloadState.Waiting=>"等待下载",DownloadState.Running=>"正在下载",DownloadState.Completed=>"已完成",
        DownloadState.Failed=>"失败",DownloadState.Canceled=>"已取消",_=>"已中断"
    };
    private void Save()
    {
        if(_readFailed)return;
        string? temp=null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(_records));File.Move(temp,_path,true);Error=null;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Error="下载记录保存失败，本次变更可能无法在重启后保留。";}
        finally{if(temp is not null)try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
