using BunnyU.Core;
using LibVLCSharp.Shared;
using System.Globalization;

namespace BunnyU.WinForms;

internal sealed class Player : IDisposable
{
    private LibVLC? _vlc;
    private MediaPlayer? _mediaPlayer;
    private readonly Control _owner;
    private readonly bool _silent;
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=250};
    private List<Chapter> _queue=[];
    private bool _disposed,_trialEnded;
    private int _volume=50;
    public Chapter? Current{get;private set;}
    public PlaybackQuality Quality{get;set;}
    public string Status{get;private set;}="尚未播放";
    public event Action? Changed;
    public double Position=>Math.Max(0,(_mediaPlayer?.Time??0)/1000d);
    public double Duration
    {
        get{var duration=_mediaPlayer?.Length>0?_mediaPlayer.Length/1000d:Current?.DurationSeconds??0;return Current?.PlaybackLimit is {} limit?Math.Min(duration>0?duration:limit,limit):duration;}
    }
    public string Progress=>$"进度 {Chapter.FormatTime(Position)} / {Chapter.FormatTime(Duration)}";
    public bool IsPlaying=>_mediaPlayer?.IsPlaying==true;
    public bool CanSeek=>Current is not null&&_mediaPlayer?.IsSeekable==true&&!_trialEnded;
    public int Volume{get=>_volume;set{_volume=Math.Clamp(value,0,100);if(_mediaPlayer is not null)_mediaPlayer.Volume=_volume;Changed?.Invoke();}}
    public Player(Control owner,bool silent=false){_owner=owner;_silent=silent;_timer.Tick+=(_,_)=>Tick();_timer.Start();}
    private void Initialize()
    {
        if(_mediaPlayer is not null)return;
        LibVLCSharp.Shared.Core.Initialize(Path.Combine(AppContext.BaseDirectory,"libvlc","win-x64"));
        _vlc=_silent?new LibVLC("--aout=dummy","--no-video","--no-osd"):new LibVLC("--no-video","--no-osd");
        _mediaPlayer=new MediaPlayer(_vlc){Volume=_volume};
        _mediaPlayer.Playing+=(_,_)=>Ui(()=>SetStatus(Current?.IsTrial==true?"正在试听":"正在播放"));
        _mediaPlayer.Paused+=(_,_)=>Ui(()=>SetStatus("已暂停"));
        _mediaPlayer.EncounteredError+=(_,_)=>Ui(()=>SetStatus("播放失败，请检查网络或权限。"));
        _mediaPlayer.EndReached+=(_,_)=>Ui(()=>
        {
            if(_mediaPlayer.State!=VLCState.Ended)return;
            if(Current?.IsTrial==true){_trialEnded=true;SetStatus("试听结束");return;}
            var i=_queue.FindIndex(c=>c.Id==Current?.Id);
            if(i>=0&&i+1<_queue.Count){try{Play(_queue[i+1],_queue);}catch(Exception ex){SetStatus(ex.Message);}}
            else SetStatus("播放完毕");
        });
    }
    public void Play(Chapter chapter,IReadOnlyList<Chapter> queue)
    {
        if(chapter.IsLocked)throw new BunnyApiException("此章节需要购买或会员权限。");
        var address=chapter.Playback?.Select(Quality);
        if(!MediaAddress.IsHttp(address))address=chapter.MediaUrl;
        if(!MediaAddress.IsHttp(address))throw new BunnyApiException("此章节没有可用的播放地址。");
        Initialize();_mediaPlayer!.Stop();Current=chapter;_queue=queue.ToList();_trialEnded=false;
        using var media=new Media(_vlc!,new Uri(address!));media.AddOption(":network-caching=1500");
        if(chapter.PlaybackLimit is {} cap)media.AddOption(":stop-time="+cap.ToString(CultureInfo.InvariantCulture));
        SetStatus("正在缓冲");if(!_mediaPlayer.Play(media))throw new BunnyApiException("无法开始播放。");
    }
    public void Toggle()
    {
        if(Current is null)return;
        if(_trialEnded||_mediaPlayer?.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)Play(Current,_queue);
        else _mediaPlayer?.Pause();
    }
    public bool SeekRelative(double delta)
    {if(!CanSeek||_mediaPlayer is null)return false;_mediaPlayer.Time=(long)(Math.Clamp(Position+delta,0,Duration)*1000);Changed?.Invoke();return true;}
    public void Stop(){_mediaPlayer?.Stop();SetStatus("已停止");}
    public void Reset(){Stop();Current=null;_queue.Clear();SetStatus("尚未播放");}
    private void Tick()
    {
        if(!_trialEnded&&Current?.PlaybackLimit is {} cap&&Position>=cap){_trialEnded=true;_mediaPlayer?.Stop();SetStatus("试听结束");}
        Changed?.Invoke();
    }
    private void SetStatus(string text){Status=text;Changed?.Invoke();}
    private void Ui(Action action){if(!_disposed&&_owner.IsHandleCreated&&!_owner.IsDisposed)_owner.BeginInvoke(()=>{if(!_disposed)action();});}
    public void Dispose(){_disposed=true;_timer.Stop();_timer.Dispose();_mediaPlayer?.Stop();_mediaPlayer?.Dispose();_vlc?.Dispose();}
}
