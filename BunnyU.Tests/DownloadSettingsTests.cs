using System.Text.Json.Nodes;
using BunnyU.Core;

internal static class DownloadSettingsTests
{
    public static void Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"bunny-settings-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"software","settings.json");var settings=new DownloadSettings(path);
            check(settings.LoadDirectory() is null&&!File.Exists(path),"first run has no assumed download folder and does not create config on read");
            settings.SaveDirectory(root);var loaded=new DownloadSettings(path);
            check(loaded.LoadDirectory()==root&&File.Exists(path),"download location persists in the selected software config path");
            var doc=JsonNode.Parse(File.ReadAllText(path))!.AsObject();doc["otherSetting"]=42;File.WriteAllText(path,doc.ToJsonString());
            settings.SaveDirectory(root);check(JsonNode.Parse(File.ReadAllText(path))!["otherSetting"]!.GetValue<int>()==42,"saving download folder preserves other preferences");
            check(settings.LoadPreferences().VolumeStepPercent==5&&!settings.LoadPreferences().DownloadCompleteSound,"legacy config defaults to five percent volume and disabled notification sound");
            settings.SavePreferences(new(root,true,12));var preferences=new DownloadSettings(path).LoadPreferences();
            check(preferences.VolumeStepPercent==12&&preferences.DownloadCompleteSound&&preferences.DownloadDirectory==root,"general and playback preferences persist together");
            settings.SaveDirectory(root);check(settings.LoadPreferences()==preferences,"first-download location save preserves sound and volume preferences");
            check(preferences.ConcurrentDownloads==1,"default concurrency remains one for existing settings");
            settings.SavePreferences(preferences with{ConcurrentDownloads=5});check(new DownloadSettings(path).LoadPreferences().ConcurrentDownloads==5,"download concurrency persists up to five");
            try{settings.SavePreferences(preferences with{ConcurrentDownloads=6});throw new Exception("invalid concurrency accepted");}
            catch(BunnyApiException){check(settings.LoadPreferences().ConcurrentDownloads==5,"invalid concurrency leaves saved settings intact");}
            var valid=File.ReadAllText(path);
            try{settings.SavePreferences(preferences with{VolumeStepPercent=0});throw new Exception("invalid step accepted");}
            catch(BunnyApiException){check(File.ReadAllText(path)==valid,"invalid volume increment does not overwrite preferences");}
            check(DownloadSettings.AlbumDirectory(root,"专辑甲")==Path.Combine(root,"专辑甲")&&DownloadSettings.AlbumDirectory(root,"专辑乙")==Path.Combine(root,"专辑乙"),"remembered base folder creates separate album folders without IDs");
            var before=File.ReadAllText(path);
            try{settings.SaveDirectory(Path.Combine(root,"missing"));throw new Exception("missing directory accepted");}
            catch(BunnyApiException){check(File.ReadAllText(path)==before,"invalid download folder does not overwrite valid preferences");}
            File.WriteAllText(path,"invalid json");check(settings.LoadDirectory() is null&&settings.Error is not null,"damaged config is reported");
            try{settings.SaveDirectory(root);throw new Exception("damaged config overwritten");}
            catch(BunnyApiException){check(File.ReadAllText(path)=="invalid json","damaged config preserved on save failure");}
            var blocked=Path.Combine(root,"not-a-directory");File.WriteAllText(blocked,"keep");
            try{new DownloadSettings(Path.Combine(blocked,"settings.json")).SaveDirectory(root);throw new Exception("save failure hidden");}
            catch(BunnyApiException ex){check(ex.Message.Contains("尚未开始下载"),"unwritable software config produces actionable failure");}
        }
        finally{Directory.Delete(root,true);}
    }
}
