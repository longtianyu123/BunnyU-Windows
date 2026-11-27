namespace BunnyU.Core;

public enum PlaybackQuality { Default,High,Highest }
public enum DownloadQuality { Highest,High,Normal }
public sealed record PlaybackAddresses(string Normal,string High,string Lossless,string Original)
{
    public string SelectDownload(DownloadQuality quality)
    {
        var candidates=quality switch{DownloadQuality.Normal=>new[]{Normal,Original},DownloadQuality.High=>new[]{High,Normal,Original},_=>new[]{Lossless,High,Normal,Original}};
        return candidates.FirstOrDefault(MediaAddress.IsDirectAudio)??"";
    }
    public string Select(PlaybackQuality quality)
    {
        var candidates=quality switch
        {
            PlaybackQuality.Highest=>new[]{Lossless,High,Normal,Original},
            PlaybackQuality.High=>new[]{High,Normal,Lossless,Original},
            _=>new[]{Normal,High,Lossless,Original}
        };
        return candidates.FirstOrDefault(MediaAddress.IsHttp)??"";
    }
}
