using System.Text.Json;
using System.Text.Json.Nodes;

namespace BunnyU.Core;

public sealed record UserPreferences(string? DownloadDirectory=null,bool DownloadCompleteSound=false,int VolumeStepPercent=5,int ConcurrentDownloads=1,bool AutoCheckUpdates=true);

public sealed class DownloadSettings(string filePath)
{
    public string FilePath{get;}=Path.GetFullPath(filePath);
    public string? Error{get;private set;}
    public string? LoadDirectory()
        =>LoadPreferences().DownloadDirectory;
    public UserPreferences LoadPreferences()
    {
        try
        {
            var root=Read();var value=root["downloadDirectory"]?.GetValue<string>();Error=null;
            var directory=!string.IsNullOrWhiteSpace(value)&&Path.IsPathFullyQualified(value)?Path.GetFullPath(value):null;
            var sound=root["downloadCompleteSound"] is JsonValue soundValue&&soundValue.TryGetValue<bool>(out var enabled)&&enabled;
            var step=root["volumeStepPercent"] is JsonValue stepValue&&stepValue.TryGetValue<int>(out var number)&&number is >=1 and <=100?number:5;
            var concurrent=root["concurrentDownloads"] is JsonValue countValue&&countValue.TryGetValue<int>(out var count)&&count is >=1 and <=5?count:1;
            var autoUpdates=!(root["autoCheckUpdates"] is JsonValue autoValue&&autoValue.TryGetValue<bool>(out var check)&&!check);
            return new(directory,sound,step,concurrent,autoUpdates);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {Error="无法读取软件目录中的 settings.json，请检查配置文件。";return new();}
    }
    public void SaveDirectory(string directory)
    {
        if(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory))throw new BunnyApiException("请选择存在的保存文件夹。");
        directory=Path.GetFullPath(directory);
        Write(root=>root["downloadDirectory"]=directory);
    }
    public void SavePreferences(UserPreferences preferences)
    {
        if(preferences.VolumeStepPercent is <1 or >100)throw new BunnyApiException("音量调整幅度应为 1% 到 100%。");
        if(preferences.ConcurrentDownloads is <1 or >5)throw new BunnyApiException("同时下载任务数应为 1 到 5 个。");
        var directory=preferences.DownloadDirectory;
        if(!string.IsNullOrWhiteSpace(directory)&&(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory)))throw new BunnyApiException("请选择存在的保存文件夹。");
        Write(root=>
        {
            root["downloadDirectory"]=string.IsNullOrWhiteSpace(directory)?null:Path.GetFullPath(directory);
            root["downloadCompleteSound"]=preferences.DownloadCompleteSound;
            root["volumeStepPercent"]=preferences.VolumeStepPercent;
            root["concurrentDownloads"]=preferences.ConcurrentDownloads;
            root["autoCheckUpdates"]=preferences.AutoCheckUpdates;
        });
    }
    private void Write(Action<JsonObject> update)
    {
        string? temporary=null;
        try
        {
            var root=Read();update(root);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,root.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,FilePath,true);Error=null;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {throw new BunnyApiException("无法保存软件目录中的 settings.json，尚未开始下载。请检查软件文件夹的写入权限和配置文件。");}
        finally{if(temporary is not null)try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    public static string AlbumDirectory(string root,string albumName)=>Path.Combine(Path.GetFullPath(root),ChapterDownloader.SafeName(albumName));
    private JsonObject Read()=>File.Exists(FilePath)?JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject??throw new JsonException():new JsonObject();
}
