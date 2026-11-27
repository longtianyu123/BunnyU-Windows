using BunnyU.Core;

namespace BunnyU.WinForms;

internal sealed class UpdateForm:Form
{
    private readonly HttpClient _http=new(){Timeout=Timeout.InfiniteTimeSpan};
    private readonly CancellationTokenSource _life=new();
    private readonly TextBox _info=new(){ReadOnly=true,Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,AccessibleName="版本信息",TabIndex=0};
    private readonly QuietProgressView _progress=new(){Dock=DockStyle.Fill,AccessibleName="更新下载进度",TabIndex=1};
    private readonly ProgressBar _bar=new(){Dock=DockStyle.Fill,Minimum=0,Maximum=100,TabStop=false,AccessibleName="更新下载进度"};
    private readonly Button _install=new(){Text="重试更新",AccessibleName="重试更新",AutoSize=true,Enabled=false,TabIndex=0};
    private readonly Button _close=new(){Text="关闭",AutoSize=true,DialogResult=DialogResult.Cancel,TabIndex=1};
    private AppRelease? _release;
    private readonly Func<string,Task> _launch;
    private readonly Action _exit;
    private bool _working,_mandatory,_launching;
    public UpdateForm(Func<string,Task> launch,Action exit,AppRelease? known=null)
    {
        _launch=launch;_exit=exit;_release=known;_mandatory=known is not null;
        Text="软件更新";Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new(620,360);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new(12),TabStop=false};
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,26));layout.RowStyles.Add(new(SizeType.Absolute,80));layout.RowStyles.Add(new(SizeType.AutoSize));
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,TabStop=false,TabIndex=2};buttons.Controls.AddRange([_install,_close]);
        layout.Controls.Add(_info,0,0);layout.Controls.Add(_bar,0,1);layout.Controls.Add(_progress,0,2);layout.Controls.Add(buttons,0,3);Controls.Add(layout);CancelButton=_close;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BunnyUWindows/"+CurrentVersion);
        _progress.UpdateProgress("尚未下载更新。");
        Shown+=async(_,_)=>{if(_release is not null)await InstallAsync();else await CheckAsync();};_install.Click+=async(_,_)=>{if(_release is null)await CheckAsync();else await InstallAsync();};
        FormClosed+=(_,_)=>{_life.Cancel();if(!_working){_http.Dispose();_life.Dispose();}if(_mandatory&&!_launching)_exit();};
    }
    public static Version CurrentVersion=>typeof(UpdateForm).Assembly.GetName().Version??new Version(0,0,0);
    private async Task CheckAsync()
    {
        _working=true;_info.Text="正在检查更新…";
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);deadline.CancelAfter(TimeSpan.FromSeconds(60));
            _release=await new AppUpdater(_http).CheckAsync(CurrentVersion,deadline.Token);if(IsDisposed)return;
            _info.Text=$"当前版本：{CurrentVersion}\r\n已是最新版本。";
            if(_release is not null){_mandatory=true;_working=false;await InstallAsync();}
        }
        catch(OperationCanceledException)when(_life.IsCancellationRequested){}
        catch(Exception ex){if(!IsDisposed){_info.Text=MainForm.Error(ex);_install.Enabled=true;}}
        finally{Finish();}
    }
    private async Task InstallAsync()
    {
        if(_working||_release is null)return;_working=true;_install.Enabled=false;
        _mandatory=true;_close.Text="退出程序";_info.Text=$"当前版本：{CurrentVersion}\r\n正在更新到：{_release.Version}\r\n{_release.Notes}\r\n下载完成后自动安装并重新打开兔U。";_bar.Value=0;
        try
        {
            var progress=new Progress<DownloadProgress>(p=>{if(!IsDisposed&&_working&&!_launching){_bar.Value=p.Total>0?(int)Math.Clamp(p.Bytes*100d/p.Total.Value,0,100):0;_progress.UpdateProgress(p.Total>0?$"正在下载安装包：{p.Bytes*100d/p.Total:F1}%\r\n{p.Bytes/1048576d:F1} / {p.Total/1048576d:F1} MB":$"已下载 {p.Bytes/1048576d:F1} MB");}});
            var path=await new AppUpdater(_http).DownloadAsync(_release,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BunnyU.Windows","updates"),progress,_life.Token);
            if(IsDisposed)return;_launching=true;_bar.Value=100;_progress.UpdateProgress("校验通过，正在自动安装，完成后将重新打开兔U。");await _launch(path);
        }
        catch(OperationCanceledException)when(_life.IsCancellationRequested){}
        catch(Exception ex){_launching=false;if(!IsDisposed){_info.Text=MainForm.Error(ex);_install.Enabled=true;}}
        finally{Finish();}
    }
    private void Finish(){_working=false;if(IsDisposed){_http.Dispose();_life.Dispose();}}
}
