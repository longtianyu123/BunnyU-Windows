using System.Net;
using System.Windows.Forms.Automation;
using BunnyU.Core;
using BunnyU.Desktop;

namespace BunnyU.WinForms;

internal sealed record Resource(string Key,string Name,string Kind,string Detail,Album? Album=null,Chapter? Chapter=null,BrowseFolder? Browse=null);
internal sealed class BrowseFolder(string name,string kind,string? category=null)
{
    public string Name=name,Kind=kind;
    public string? Category=category;
    public List<Resource> Items=[];
    public int Page,Total;
}
internal sealed class ChapterFolder(Album album)
{
    public Album Album=album;
    public List<Chapter> Items=[];
    public int Page,Total;
    public string? SelectedKey;
}

internal sealed class ResourceListBox : PlainListBox
{
    public event Action<Point>? ContextRequested;
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x7b)
        {
            var x=unchecked((short)(m.LParam.ToInt64()&0xffff));var y=unchecked((short)((m.LParam.ToInt64()>>16)&0xffff));
            Point point;
            if(x==-1&&y==-1)point=PointToScreen(SelectedIndex>=0?new Point(20,GetItemRectangle(SelectedIndex).Bottom):new Point(10,10));
            else{point=new(x,y);var hit=IndexFromPoint(PointToClient(point));if(hit>=0)SelectIndex(hit);}
            ContextRequested?.Invoke(point);return;
        }
        base.WndProc(ref m);
    }
    public ResourceListBox()
    {
        AccessibleName="资源";TabIndex=1;TabStop=true;Dock=DockStyle.Fill;
    }
    protected override bool IsInputKey(Keys keyData)
        =>(keyData&Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
}

internal sealed class MainForm : Form
{
    private readonly TextBox _query=new(){AccessibleName="搜索作品",TabIndex=0,Dock=DockStyle.Fill};
    private readonly ResourceListBox _resources=new();
    private readonly Label _path=new(){Text="搜索结果",AutoSize=true,TabStop=false,Margin=new Padding(0,8,0,8)};
    private readonly NativeStatus _status=new(){Dock=DockStyle.Bottom};
    private readonly PlainListBox _statusDetails=new(){AccessibleName="状态详情",TabIndex=9};
    private readonly GroupBox _statusGroup=new(){Text="状态详情（F8）",Dock=DockStyle.Fill,TabStop=false,TabIndex=4,Visible=false};
    private readonly SessionStore _store=new();
    private readonly HttpClient _http=new(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25)};
    private readonly IBunnyApi _api;
    private UserSession? _session;
    private readonly Player _player;
    private NativeMenu? _menu;
    private readonly DownloadRegistry _downloads;
    private readonly DownloadSettings _settings;
    private UserPreferences _preferences;
    private DownloadManagerForm? _downloadManager;
    private UpdateForm? _updateWindow;
    private readonly System.Windows.Forms.Timer _updateTimer=new(){Interval=6*60*60*1000};
    private bool _checkingUpdate;
    private bool _shutdownRequested;
    private bool _downloadsStopped;
    private readonly CancellationTokenSource _life=new();
    private CancellationTokenSource? _request;
    private readonly List<Album> _albums=[];
    private readonly Dictionary<long,ChapterFolder> _folders=[];
    private ChapterFolder? _folder;
    private readonly BrowseFolder _home=new("首页","home");
    private BrowseFolder? _browse;
    private readonly Stack<(BrowseFolder? Browse,ChapterFolder? Folder,string? Key)> _parents=new();
    private int _page,_total,_navigation;
    private long _interaction;
    private bool _busy,_closed,_renderingResources;
    private string? _autoPageAttempt;
    private string _keyword="",_message="选择推荐或分类后按回车进入，也可以输入关键词搜索。",_lastPlayback="",_parentKey="";
    private DateTime _smsUntil=DateTime.MinValue;
    private readonly TableLayoutPanel _root;
    private readonly string[] _args;
    public MainForm(string[] args,IBunnyApi? testApi=null)
    {
        _args=args;_api=testApi??new BunnyApi(_http,_store.DeviceId);_session=_store.Load();
        _settings=new DownloadSettings(args.Contains("--self-test")?Path.Combine(Path.GetTempPath(),"bunny-preferences-"+Guid.NewGuid().ToString("N"),"settings.json"):Path.Combine(AppContext.BaseDirectory,"settings.json"));
        _preferences=_settings.LoadPreferences();
        _downloads=new DownloadRegistry(args.Contains("--self-test")?Path.Combine(Path.GetTempPath(),"bunny-selftest-"+Guid.NewGuid().ToString("N"),"downloads.json"):
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BunnyU.Windows","downloads.json"),concurrency:_preferences.ConcurrentDownloads);
        _downloads.BatchCompleted+=()=>
        {
            if(!_closed&&!_shutdownRequested&&!args.Contains("--self-test")&&_preferences.DownloadCompleteSound)
                try{System.Media.SystemSounds.Asterisk.Play();}catch(Exception){/* Sound failure must never stop the download queue. */}
        };
        Text=_session is null?"兔U":"兔U - "+_session.DisplayName;Name="BunnyUMain";Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(930,620);MinimumSize=new Size(680,460);StartPosition=FormStartPosition.CenterScreen;
        _player=new Player(this,args.Contains("--self-test")){Quality=_preferences.PlaybackQuality};_player.Changed+=UpdatePlayback;
        _root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(10),TabStop=false};
        _root.RowStyles.Add(new(SizeType.AutoSize));_root.RowStyles.Add(new(SizeType.AutoSize));_root.RowStyles.Add(new(SizeType.Percent,100));_root.RowStyles.Add(new(SizeType.Absolute,0));
        var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,TabStop=false,TabIndex=0};
        searchRow.ColumnStyles.Add(new(SizeType.AutoSize));searchRow.ColumnStyles.Add(new(SizeType.Percent,100));
        searchRow.Controls.Add(new Label{Text="搜索(&S)",AutoSize=true,TabStop=false,Anchor=AnchorStyles.Left},0,0);searchRow.Controls.Add(_query,1,0);
        foreach(var name in new[]{"操作状态","播放状态","音量","播放进度","账号"})_statusDetails.Items.Add(new PlainListEntry(name,name));
        _statusGroup.Controls.Add(_statusDetails);_root.Controls.Add(searchRow,0,0);_root.Controls.Add(_path,0,1);_root.Controls.Add(_resources,0,2);_root.Controls.Add(_statusGroup,0,3);
        Controls.Add(_root);Controls.Add(_status);
        _query.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await SearchAsync();}};
        _resources.DoubleClick+=async(_,_)=>await EnterAsync();
        _resources.ContextRequested+=ShowDownloadMenu;
        _resources.SelectedIndexChanged+=(_,_)=>
        {
            if(!_renderingResources&&_resources.ContainsFocus)TryAutoLoadMore();
        };
        _resources.KeyDown+=async(_,e)=>
        {
            if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;await EnterAsync();}
            else if(e.KeyCode==Keys.Back){e.SuppressKeyPress=true;Back();}
            else if(e.Modifiers==Keys.None&&e.KeyCode is Keys.Up or Keys.Down)
            {
                e.SuppressKeyPress=true;var index=_resources.SelectedIndices.Count>0?_resources.SelectedIndices[0]:-1;
                _resources.SelectIndex(e.KeyCode==Keys.Down?index+1:Math.Max(0,index-1));
            }
        };
        _resources.Enter+=(_,_)=>{if(_resources.SelectedIndices.Count==0&&_resources.Items.Count>0)_resources.SelectIndex(0);};
        Deactivate+=(_,_)=>_interaction++;
        TrackMouse(this);
        ResetHome();Render();
        _updateTimer.Tick+=async(_,_)=>await CheckUpdatesAsync();
        Shown+=async(_,_)=>{_query.Focus();UpdatePlayback();if(args.Contains("--self-test"))await SelfTestAsync();else{_updateTimer.Start();await CheckUpdatesAsync();}};
    }
    private void TrackMouse(Control control){control.MouseDown+=(_,_)=>_interaction++;control.MouseWheel+=(_,_)=>_interaction++;foreach(Control child in control.Controls)TrackMouse(child);}
    private async Task CheckUpdatesAsync()
    {
        if(_checkingUpdate||_closed)return;_checkingUpdate=true;
        try
        {
            using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("BunnyUWindows/"+UpdateForm.CurrentVersion);
            var release=await new AppUpdater(http).CheckAsync(UpdateForm.CurrentVersion,_life.Token);
            if(!_closed&&release is not null)ShowUpdates(release);
        }
        catch(Exception){/* Background update checks remain silent on network errors. */}
        finally{_checkingUpdate=false;}
    }
    private void ShowUpdates(AppRelease? release=null)
    {
        if(_updateWindow is {IsDisposed:false}){_updateWindow.Activate();return;}
        _updateWindow=new UpdateForm(async path=>
        {
            await _downloads.StopAsync();_downloadsStopped=true;
            var launch=new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true};
            launch.ArgumentList.Add("/SILENT");launch.ArgumentList.Add("/SUPPRESSMSGBOXES");launch.ArgumentList.Add("/NORESTART");launch.ArgumentList.Add("/LAUNCHAFTERUPDATE=1");launch.ArgumentList.Add("/DIR="+AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if(System.Diagnostics.Process.Start(launch) is null)throw new IOException("无法打开安装程序。");
            Close();
        },()=>{if(!_closed)BeginInvoke(()=>Close());},release);
        _updateWindow.FormClosed+=(_,_)=>_updateWindow=null;_updateWindow.ShowDialog(this);
    }
    private void ShowPreferences()
    {
        var returnToResources=_resources.ContainsFocus;
        using var dialog=new PreferencesForm(_settings);
        if(dialog.ShowDialog(this)==DialogResult.OK){_preferences=_settings.LoadPreferences();_downloads.SetConcurrency(_preferences.ConcurrentDownloads);_player.Quality=_preferences.PlaybackQuality;Announce("首选项已保存。");}
        if(returnToResources)FocusResources();else _query.Focus();
    }
    private void ShowDownloadMenu(Point point)
    {
        if(_folder is null||_busy)return;
        var chapter=Selected?.Chapter;var album=_folder.Album;
        var command=NativeDownloadMenu.Show(Handle,point,chapter is not null);
        if(command is not (301 or 302))return;
        var selectedKey=Selected?.Key;
        using var dialog=new DownloadForm(_api,album,_session,command==301?chapter:null,_downloads,_settings);
        var result=dialog.ShowDialog(this);
        if(_closed||_shutdownRequested)return;
        Render(selectedKey);FocusResources();
        if(result==DialogResult.OK)Announce($"已加入 {dialog.QueuedCount} 个下载任务，Ctrl+J 查看下载管理。");
    }
    private void ShowDownloadManager()
    {
        if(_downloadManager is {IsDisposed:false}){_downloadManager.Activate();return;}
        _downloadManager=new DownloadManagerForm(_downloads);_downloadManager.FormClosed+=(_,_)=>_downloadManager=null;_downloadManager.Show(this);
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);_menu=new NativeMenu(Handle);_menu.SetSignedIn(_session is not null);}
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x111&&m.LParam==0)
        {
            switch((int)(m.WParam.ToInt64()&0xffff))
            {
                case 101:Login();return;case 102:Logout();return;case 103:Close();return;
                case 303:ShowDownloadManager();return;
                case 401:ShowPreferences();return;
                case 202:ShowUpdates();return;
                case 201:MessageBox.Show(this,"Tab / Shift+Tab：移动控件\nEnter：进入作品或播放章节\nBackspace：返回上一层\nF6：资源\nF8：状态详情\nCtrl+F：搜索\nCtrl+J：下载管理\n空格（资源内）：暂停/继续\nF5：刷新当前层\nCtrl+PageDown：加载下一页\nCtrl+↑ / ↓：按首选项中的百分比调整音量\n← / →：快退快进 5 秒（输入框除外）\nCtrl+空格：暂停 / 继续\nAlt / F10：系统菜单","快捷键");return;
            }
        }
        base.WndProc(ref m);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        _interaction++;
        if(keyData==(Keys.Control|Keys.J)){ShowDownloadManager();return true;}
        if(keyData==Keys.F5){_=RefreshCurrentAsync();return true;}
        if(keyData==(Keys.Control|Keys.PageDown)){_=MoreAsync();return true;}
        if(keyData==Keys.Space&&_resources.ContainsFocus){Toggle();return true;}
        if(keyData==Keys.F6){FocusResources();return true;}
        if(keyData==Keys.F8){ShowStatus();return true;}
        if(keyData==(Keys.Control|Keys.F)){_query.Focus();_query.SelectAll();return true;}
        if(keyData is (Keys.Control|Keys.Up) or (Keys.Control|Keys.Down))
        {_player.Volume+=keyData==(Keys.Control|Keys.Up)?_preferences.VolumeStepPercent:-_preferences.VolumeStepPercent;Announce($"音量 {_player.Volume}%");return true;}
        if(keyData==(Keys.Control|Keys.Space)){Toggle();return true;}
        if(keyData is Keys.Left or Keys.Right && !_query.ContainsFocus && !_statusDetails.ContainsFocus)
        {Announce(_player.SeekRelative(keyData==Keys.Right?5:-5)?_player.Progress:"当前不能调整进度。");return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void Announce(string text)
    {
        _message=text;UpdatePlayback();
        if(IsHandleCreated&&!_closed)AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,AutomationNotificationProcessing.ImportantMostRecent,text);
    }
    private void UpdatePlayback()
    {
        if(_closed)return;
        _status.SetPart(0,_message);_status.SetPart(1,$"音量 {_player.Volume}%");_status.SetPart(2,_player.Progress);_status.SetPart(3,_session?.DisplayName??"未登录");
        var fields=new[]{_message,_player.Status,$"{_player.Volume}%",_player.Progress,_session?.DisplayName??"未登录"};
        var names=new[]{"操作状态","播放状态","音量","播放进度","账号"};
        _statusDetails.SetRows(fields.Select((value,index)=>new PlainListEntry(names[index],names[index]+"："+value)).ToArray());
        if(_lastPlayback!=_player.Status)
        {
            _lastPlayback=_player.Status;
            if(IsHandleCreated)AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,AutomationNotificationProcessing.MostRecent,_lastPlayback);
        }
    }
    private void ShowStatus()
    {
        _statusGroup.Visible=true;_root.RowStyles[3].Height=145*DeviceDpi/96f;UpdatePlayback();
        _statusDetails.SelectIndex(0);
    }
    private void SetBusy(bool busy)=>_busy=busy;
    private void Render(string? selectedKey=null)
    {
        selectedKey??=Selected?.Key;
        var rows=_folder is null?(_browse?.Items??_albums.Select(a=>new Resource("a:"+a.Id,a.Name,"作品目录",a.Subtitle,a)).ToList())
            :_folder.Items.Select(c=>new Resource("c:"+c.Id,c.Name,"音频章节",c.DurationLabel+" · "+c.AccessLabel,null,c)).ToList();
        _renderingResources=true;
        try{_resources.SetRows(rows.Select(r=>new PlainListEntry(r.Key,r.Name,r)).ToArray(),selectedKey);}
        finally{_renderingResources=false;}
        var location=_browse?.Name??$"搜索结果：{_keyword}";
        _path.Text=_folder is null?(_browse is null?$"{location}（{_albums.Count} / {_total}）":$"{location}（{rows.Count} 项）"):$"{location} > {_folder.Album.Name}（{_folder.Items.Count} / {_folder.Total}）";

    }
    private Resource? Selected=>_resources.SelectedEntry?.Data as Resource;
    private void FocusResources()
    {
        if(_closed)return;var index=_resources.SelectedIndices.Count>0?_resources.SelectedIndices[0]:0;_resources.SelectIndex(index);
    }
    private async Task SearchAsync(bool more=false,bool refresh=false)
    {
        if(_busy)return;if(string.IsNullOrWhiteSpace((more||refresh)?_keyword:_query.Text)){GoHome();return;}
        var navigation=++_navigation;var interaction=_interaction;
        _request?.Cancel();using var request=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);_request=request;
        var selectedKey=refresh?Selected?.Key:null;
        var keyword=(more||refresh)?_keyword:_query.Text.Trim();var page=more?_page+1:1;
        if(!more){_folder=null;_folders.Clear();_parentKey="";_browse=null;_parents.Clear();_parents.Push((_home,null,"recommend"));}_busy=true;SetBusy(true);Announce("正在搜索。");
        try
        {
            var result=await _api.SearchAsync(keyword,page,_session,request.Token);if(_closed||navigation!=_navigation)return;
            var previousCount=_albums.Count;
            if(!more)_albums.Clear();foreach(var item in result.Items)if(!_albums.Any(a=>a.Id==item.Id))_albums.Add(item);
            _keyword=keyword;_page=page;_total=result.Items.Count==0||(more&&_albums.Count==previousCount)?_albums.Count:result.Total;Render(selectedKey);
            Announce($"搜索完成，共 {result.Total} 个作品，已加载 {_albums.Count} 个。");
            if(!more&&interaction==_interaction&&Form.ActiveForm==this)FocusResources();
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Exception ex){if(!_closed&&navigation==_navigation)Announce(Error(ex));}
        finally{if(ReferenceEquals(_request,request)){_request=null;if(!_closed)SetBusy(false);}}
    }
    private async Task EnterAsync()
    {
        if(_busy)return;
        if(Selected?.Browse is {} browse)
        {
            _parents.Push((_browse,_folder,Selected.Key));_browse=browse;_folder=null;Render();
            if(browse.Kind!="section"&&browse.Page==0)await LoadBrowseAsync(browse);else FocusResources();return;
        }
        if(Selected?.Album is {} album)
        {
            _parents.Push((_browse,_folder,Selected.Key));
            _parentKey="a:"+album.Id;
            if(!_folders.TryGetValue(album.Id,out var folder)){folder=new(album);_folders.Add(album.Id,folder);}
            _folder=folder;Render(folder.SelectedKey);
            if(folder.Page==0)await LoadFolderAsync(folder);else FocusResources();
        }
        else if(Selected?.Chapter is {} chapter&&_folder is not null)
        {try{_player.Play(chapter,_folder.Items);Announce("正在播放："+chapter.Name);}catch(Exception ex){Announce(Error(ex));}}
        else Announce("请先选择一个资源。");
    }
    private async Task LoadFolderAsync(ChapterFolder folder)
    {
        if(_busy)return;var navigation=++_navigation;var interaction=_interaction;
        _request?.Cancel();using var request=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);_request=request;
        _busy=true;SetBusy(true);Announce("正在加载全部章节。");
        try
        {
            var chapters=await ChapterDownloader.AllChaptersAsync(_api,folder.Album.Id,_session,request.Token);
            if(_closed||navigation!=_navigation||_folder!=folder)return;
            folder.Items=chapters.ToList();folder.Page=1;folder.Total=folder.Items.Count;Render(folder.SelectedKey);
            Announce($"全部章节加载完成，共 {folder.Total} 章。");
            if(interaction==_interaction&&Form.ActiveForm==this)FocusResources();
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Exception ex){if(!_closed&&navigation==_navigation)Announce(Error(ex));}
        finally{if(ReferenceEquals(_request,request)){_request=null;if(!_closed)SetBusy(false);}}
    }
    private async Task RefreshCurrentAsync()
    {
        _autoPageAttempt=null;
        if(_busy){Announce("正在加载，请稍候。");return;}
        if(_folder is not null){_folder.SelectedKey=Selected?.Key;await LoadFolderAsync(_folder);}
        else if(_browse is not null){if(_browse==_home){Render();Announce("首页已刷新。");}else await LoadBrowseAsync(_browse);}
        else if(_keyword.Length>0)await SearchAsync(refresh:true);
        else Announce("请输入关键词后按回车搜索。");
    }
    private void TryAutoLoadMore()
    {
        if(_busy||_closed||_renderingResources||_resources.SelectedIndex<0)return;
        if(_resources.SelectedIndex<Math.Max(0,_resources.Items.Count-5)){_autoPageAttempt=null;return;}
        string key;int count,total;
        if(_folder is not null)return;
        if(_browse is {Kind:"category"} browse){count=browse.Items.Count;total=browse.Total;key=$"category:{browse.Category}:{browse.Page}";}
        else if(_browse is null&&_keyword.Length>0){count=_albums.Count;total=_total;key=$"search:{_keyword}:{_page}";}
        else return;
        if(count==0||count>=total||_autoPageAttempt==key)return;
        _autoPageAttempt=key;_=MoreAsync();
    }
    private async Task MoreAsync()
    {
        if(_busy)return;
        if(_folder is not null)
        {Announce(_folder.Page>0?"已加载全部章节。":"章节尚未加载完成，请按 F5 重试。");}
        else if(_browse is not null){if(_browse.Kind!="category"||_browse.Items.Count>=_browse.Total){Announce("当前层已全部加载。");return;}await LoadBrowseAsync(_browse,true);}
        else {if(_albums.Count>=_total){Announce("已加载全部搜索结果。");return;}await SearchAsync(true);}
    }
    private void Back()
    {
        if(_parents.Count==0){Announce("已在最上层。");return;}
        if(_folder is not null)_folder.SelectedKey=Selected?.Key;
        CancelNavigation();var parent=_parents.Pop();_browse=parent.Browse;_folder=parent.Folder;Render(parent.Key);FocusResources();Announce("已返回："+(_browse?.Name??"搜索结果"));
    }
    private void CancelNavigation(){_navigation++;_request?.Cancel();_request=null;_busy=false;_autoPageAttempt=null;}
    private void ResetHome()
    {
        _home.Items=[new("recommend","推荐","目录","推荐栏目",Browse:new("推荐","recommend")),new("categories","分类","目录","按分类浏览作品",Browse:new("分类","categories"))];
        _browse=_home;_folder=null;_parents.Clear();
    }
    private void GoHome(){CancelNavigation();_browse=_home;_folder=null;_parents.Clear();Render();FocusResources();Announce("首页：推荐、分类。");}
    private async Task LoadBrowseAsync(BrowseFolder folder,bool more=false)
    {
        if(_busy)return;
        var navigation=++_navigation;var interaction=_interaction;var selected=Selected?.Key;
        using var request=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);_request=request;_busy=true;Announce("正在加载："+folder.Name);
        try
        {
            List<Resource> rows;var total=0;var page=more?folder.Page+1:1;
            if(folder.Kind is "recommend" or "section")
            {
                var sections=await _api.RecommendationsAsync(_session,request.Token);
                if(folder.Kind=="section")
                {
                    var section=sections.FirstOrDefault(s=>s.Name==folder.Name);
                    rows=(section?.Albums??[]).Select(AlbumResource).ToList();
                }
                else rows=sections.Select(s=>new Resource("section:"+s.Name,s.Name,"推荐栏目",s.Description,Browse:new BrowseFolder(s.Name,"section"){Items=s.Albums.Select(AlbumResource).ToList(),Page=1,Total=s.Albums.Count})).ToList();
                total=rows.Count;
            }
            else if(folder.Kind=="categories")
            {
                var categories=await _api.CategoriesAsync(request.Token);
                rows=categories.Select(c=>new Resource("category:"+c.Id,c.Name,"分类","回车查看作品",Browse:new(c.Name,"category",c.Id))).ToList();total=rows.Count;
            }
            else
            {
                var result=await _api.CategoryAlbumsAsync(folder.Category!,page,_session,request.Token);
                rows=result.Items.Select(AlbumResource).ToList();total=result.Total;
                if(rows.Count==0)total=more?folder.Items.Count:0;
            }
            if(_closed||navigation!=_navigation||_browse!=folder)return;
            var previousCount=folder.Items.Count;
            if(!more)folder.Items.Clear();foreach(var row in rows)if(!folder.Items.Any(r=>r.Key==row.Key))folder.Items.Add(row);
            if(more&&folder.Items.Count==previousCount)total=folder.Items.Count;
            folder.Page=page;folder.Total=total;Render(selected);Announce($"{folder.Name}：已加载 {folder.Items.Count} 项，共 {total} 项。");
            if(!more&&interaction==_interaction&&ActiveForm==this)FocusResources();
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Exception ex){if(!_closed&&navigation==_navigation)Announce(Error(ex));}
        finally{if(ReferenceEquals(_request,request)){_request=null;_busy=false;}}
    }
    private static Resource AlbumResource(Album a)=>new("a:"+a.Id,a.Name,"作品目录",a.Subtitle,a);
    private void Login()
    {
        var returnToResource=_resources.ContainsFocus;
        if(_session is {} current)
        {
            using var info=new AccountForm(_api,current,updated=>
            {
                if(_session?.UserId!=updated.UserId)return;
                _session=updated;
                try{if(_store.Load() is {} saved&&saved.UserId==updated.UserId&&saved.Token==updated.Token)_store.Save(updated);}catch{}
                UpdateAccountPresentation();
            });
            info.ShowDialog(this);if(returnToResource)FocusResources();else _query.Focus();return;
        }
        using var dialog=new LoginForm(_api,_store,()=>_smsUntil,value=>_smsUntil=value);
        if(dialog.ShowDialog(this)==DialogResult.OK&&dialog.Session is {} session){_session=session;InvalidateAccount();Announce("登录成功。");}
        if(returnToResource)FocusResources();else _query.Focus();
    }
    private void Logout()
    {
        _session=null;_player.Reset();try{_store.Clear();}catch{Announce("退出成功，但本机凭据清理失败。");}
        InvalidateAccount();Announce("已退出本机登录。");
    }
    private void InvalidateAccount(){CancelNavigation();_folders.Clear();_albums.Clear();_keyword="";_page=_total=0;ResetHome();Render();UpdateAccountPresentation();}
    private void UpdateAccountPresentation(){Text=_session is null?"兔U":"兔U - "+_session.DisplayName;_menu?.SetSignedIn(_session is not null);UpdatePlayback();}
    private void Toggle(){try{_player.Toggle();}catch(Exception ex){Announce(Error(ex));}}
    public static string Error(Exception ex)=>ex switch{BunnyApiException=>ex.Message,OperationCanceledException=>"请求超时，请重试。",HttpRequestException=>"网络连接失败，请重试。",_=>"操作失败："+ex.Message};
    protected override void OnFormClosed(FormClosedEventArgs e)
    {_closed=true;_life.Cancel();_request?.Cancel();_updateTimer.Dispose();_player.Dispose();_menu?.Dispose();_http.Dispose();_downloads.Dispose();_life.Dispose();base.OnFormClosed(e);}
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if(!_downloadsStopped&&_downloads.IsBusy)
        {
            e.Cancel=true;base.OnFormClosing(e);if(_shutdownRequested)return;
            _shutdownRequested=true;Announce("正在停止下载并退出。");
            await _downloads.StopAsync();_downloadsStopped=true;Close();return;
        }
        base.OnFormClosing(e);
    }
    private async Task SelfTestAsync()=>await FormSelfTest.RunAsync(this,_args);
    internal ResourceListBox TestResources=>_resources;
    internal async Task TestAutoPagingAsync(Action<bool,string> check)
    {
        if(_api is not PagingTestApi api)throw new InvalidOperationException();
        async Task Idle(){var until=DateTime.UtcNow.AddSeconds(5);while(_busy&&DateTime.UtcNow<until)await Task.Delay(10);check(!_busy,"page request finished");}
        var albums=Enumerable.Range(1,10).Select(id=>new Album(id,"作品"+id,"","","")).ToArray();
        _browse=new("分类","category","1"){Items=albums.Select(AlbumResource).ToList(),Page=1,Total=20};Render();
        _resources.SelectedIndex=0;TryAutoLoadMore();check(api.CategoryCalls==0,"browsing early rows does not prefetch");
        _resources.SelectedIndex=5;var selected=Selected!.Key;var top=_resources.TopIndex;TryAutoLoadMore();TryAutoLoadMore();
        check(api.CategoryCalls==1,"last five category rows trigger one request without duplicates");
        api.CategoryReply.SetResult(new(Enumerable.Range(11,10).Select(id=>new Album(id,"作品"+id,"","","")).ToArray(),2,20));await Idle();
        check(_resources.Items.Count==20&&Selected?.Key==selected&&_resources.TopIndex==top,"category append preserves selected item and scroll position");
        _resources.SelectedIndex=19;TryAutoLoadMore();check(api.CategoryCalls==1,"known final page does not request again");
        CancelNavigation();_folder=new(albums[0]);Render();
        var chapters=Enumerable.Range(1,15).Select(id=>new Chapter(id,"章节"+id,"",10,"https://test.invalid/a.mp3",false,false,0)).ToArray();
        api.ChapterResponder=page=>page==1?Task.FromResult(new ChapterPage(chapters[..10],1,15)):api.ChapterReply.Task;
        var loading=LoadFolderAsync(_folder);
        check(api.ChapterCalls==2&&_resources.Items.Count==0,"entering album immediately requests all pages before displaying list");
        api.ChapterReply.SetResult(new(chapters[10..],2,15));await loading;
        check(_resources.Items.Count==15&&_folder.Total==15,"complete album chapter list is displayed together");
        _resources.SelectedIndex=14;TryAutoLoadMore();await MoreAsync();check(api.ChapterCalls==2,"browsing last chapter does not request another page");
        _folder.SelectedKey=Selected?.Key;
        api.ChapterResponder=page=>page==1?Task.FromResult(new ChapterPage(chapters[..10],1,15)):Task.FromException<ChapterPage>(new HttpRequestException("test failure"));
        await LoadFolderAsync(_folder);check(_resources.Items.Count==15&&Selected?.Key=="c:15","failed refresh preserves previous complete chapter list and selection");
        CancelNavigation();_folder=null;_browse=new("分类","category","2"){Items=albums.Select(AlbumResource).ToList(),Page=1,Total=20};Render();
        api.CategoryReply=new(TaskCreationOptions.RunContinuationsAsynchronously);_resources.SelectedIndex=9;TryAutoLoadMore();
        api.CategoryReply.SetException(new HttpRequestException("test failure"));await Idle();TryAutoLoadMore();
        check(api.CategoryCalls==2,"failed automatic request is not retried continuously");
        _resources.SelectedIndex=0;TryAutoLoadMore();_resources.SelectedIndex=9;
        api.CategoryReply=new(TaskCreationOptions.RunContinuationsAsynchronously);TryAutoLoadMore();
        GoHome();api.CategoryReply.SetResult(new(albums,2,20));await Task.Delay(30);
        check(_browse==_home&&_resources.Items.Count==2,"late auto-page response cannot replace navigated home list");
    }
    internal IBunnyApi TestApi=>_api;
    internal Player TestPlayer=>_player;
    internal NativeStatus TestStatus=>_status;
    internal void TestVolumePreference(Action<bool,string> check)
    {
        var saved=_preferences;var volume=_player.Volume;
        try
        {
            _preferences=_preferences with{VolumeStepPercent=7};_player.Volume=50;var message=new Message();
            ProcessCmdKey(ref message,Keys.Control|Keys.Up);check(_player.Volume==57,"volume shortcut uses configured percentage");
            ProcessCmdKey(ref message,Keys.Control|Keys.Down);check(_player.Volume==50,"volume down uses same configured percentage");
            _player.Volume=98;ProcessCmdKey(ref message,Keys.Control|Keys.Up);check(_player.Volume==100,"configured volume increment respects upper limit");
        }
        finally{_preferences=saved;_player.Volume=volume;}
    }
    internal async Task TestBrowseAsync(Action<bool,string> check)
    {
        check(_resources.Items.Cast<PlainListEntry>().Select(i=>i.Text).SequenceEqual(new[]{"推荐","分类"}),"home has two local entries");
        _resources.SelectIndex(1);await EnterAsync();
        check(_browse?.Kind=="categories"&&_resources.Items.Count>0,"explicit category entry loads categories");
        _resources.SelectIndex(0);var categoryKey=Selected!.Key;await EnterAsync();
        check(_browse?.Kind=="category"&&_resources.Items.Count>0,"explicit category loads albums");
        _resources.SelectIndex(0);var albumKey=Selected!.Key;await EnterAsync();
        check(_folder is not null&&_resources.Items.Count>0,"category album opens chapters");
        Back();check(Selected?.Key==albumKey,"back restores category album selection");
        Back();check(Selected?.Key==categoryKey,"back restores category selection");
        Back();check(Selected?.Key=="categories","back restores home selection");
        _resources.SelectIndex(0);await EnterAsync();check(_browse?.Kind=="recommend"&&_resources.Items.Count>0,"recommendation sections loaded");
        _resources.SelectIndex(0);await EnterAsync();check(_browse?.Kind=="section"&&_resources.Items.Count>0,"recommendation albums loaded");
        Back();Back();check(Selected?.Key=="recommend","recommendation parent restored");
    }
    internal void SeedTestRows(){_browse=null;_albums.Add(new Album(1,"测试作品","测试作者","",""));_total=1;Render();}
    internal void TestAccountPresentation(Action verifySignedIn,Action verifySignedOut)
    {
        if(!_args.Contains("--self-test"))throw new InvalidOperationException();
        var original=_session;
        try
        {
            _session=new UserSession(42,"self-test-token","13800000000","测试听友");UpdateAccountPresentation();verifySignedIn();
            _session=null;UpdateAccountPresentation();verifySignedOut();
        }
        finally{_session=original;UpdateAccountPresentation();}
    }
}
