using BunnyU.Core;
using System.Text.Json;

internal static class PlaybackQualityTests
{
    public static void Run(Action<bool,string> check)
    {
        var addresses=new PlaybackAddresses("https://test/normal","https://test/high","https://test/flac","https://test/original");
        check(addresses.Select(PlaybackQuality.Default)==addresses.Normal,"default playback selects lowest available quality");
        check(addresses.Select(PlaybackQuality.Highest)==addresses.Lossless,"highest playback prefers FLAC");
        check((addresses with{Lossless=""}).Select(PlaybackQuality.Highest)==addresses.High,"highest falls back to high when FLAC missing");
        check((addresses with{Lossless="",High="invalid"}).Select(PlaybackQuality.Highest)==addresses.Normal,"highest skips invalid addresses and falls back to normal");
        check((addresses with{Normal=""}).Select(PlaybackQuality.Default)==addresses.High,"default ascends when normal missing");
        check(addresses.Select(PlaybackQuality.High)==addresses.High,"high-quality preference selects high MP3");
        check(addresses.SelectDownload(DownloadQuality.Highest)==addresses.Lossless,"download defaults to highest available quality");
        check((addresses with{High=""}).SelectDownload(DownloadQuality.High)==addresses.Normal,"chosen high download falls down to normal, never up to FLAC");
        check((addresses with{Normal="",Original=""}).SelectDownload(DownloadQuality.Normal)=="","normal download does not silently upgrade");
        check((addresses with{Normal="",High="",Lossless=""}).Select(PlaybackQuality.Highest)==addresses.Original,"original address remains last fallback");
        using var doc=JsonDocument.Parse("""{"code":0,"data":{"total":1,"data":[{"id":1,"url":"https://test/original","soundInfo":{"name":"章","url_mp3_normal":"https://test/normal","url_mp3_higher":"https://test/high","url_flac":"https://test/flac"}}]}}""");
        var chapter=ResponseParser.Chapters(doc.RootElement,1).Items.Single();
        check(chapter.Playback?.Select(PlaybackQuality.Highest)==addresses.Lossless,"parser retains every quality despite outer generic URL");
        var root=Path.Combine(Path.GetTempPath(),"bunny-quality-"+Guid.NewGuid().ToString("N"));
        try{var settings=new DownloadSettings(Path.Combine(root,"settings.json"));settings.SavePreferences(new(PlaybackQuality:PlaybackQuality.Highest));settings.SaveDirectory(root);check(settings.LoadPreferences().PlaybackQuality==PlaybackQuality.Highest,"playback quality survives restart and download-directory save");}
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
