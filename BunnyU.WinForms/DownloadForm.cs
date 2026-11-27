using BunnyU.Core;
using System.Windows.Forms.Automation;

namespace BunnyU.WinForms;

// Select chapters and destination only. The queue outlives this dialog.
internal sealed class DownloadForm:Form
{
    private sealed record Entry(Chapter Chapter)
    {
        public override string ToString(){var reason=ChapterDownloader.UnavailableReason(Chapter);return Chapter.Name+(reason.Length>0?"（"+reason+"）":"");}
    }
    private readonly CheckedListBox _chapters=new(){Dock=DockStyle.Fill,AccessibleName="下载章节",TabIndex=0,CheckOnClick=false,IntegralHeight=false};
    private string _directory="";
    private readonly Label _message=new(){AutoSize=true,Dock=DockStyle.Fill,TabStop=false,Text="正在加载章节…"};
    private readonly Button _all=new(){Text="全选",AutoSize=true,TabIndex=0};
    private readonly Button _none=new(){Text="取消全选",AutoSize=true,TabIndex=1};
    private readonly Button _start=new(){Text="开始下载",AutoSize=true,TabIndex=2,Enabled=false};
    private readonly Button _close=new(){Text="返回",AutoSize=true,TabIndex=3,DialogResult=DialogResult.Cancel};
    private readonly IBunnyApi _api;
    private readonly Album _album;
    private readonly UserSession? _session;
    private readonly Chapter? _single;
    private readonly DownloadRegistry _registry;
    private readonly DownloadSettings _settings;
    internal Func<string?,string?>? TestFolderPicker;
    private readonly CancellationTokenSource _life=new();
    private bool _loading,_loaded;
    private int _interaction;
    public int QueuedCount{get;private set;}
    public DownloadForm(IBunnyApi api,Album album,UserSession? session,Chapter? single,DownloadRegistry registry,DownloadSettings? settings=null)
    {
        _api=api;_album=album;_session=session;_single=single;_registry=registry;
        _settings=settings??new DownloadSettings(Path.Combine(AppContext.BaseDirectory,"settings.json"));
        Text=(single is null?"选择下载章节 - ":"下载 - ")+album.Name;
        Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;StartPosition=FormStartPosition.CenterParent;
        ClientSize=new(670,410);MinimumSize=new(500,340);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(12),ColumnCount=1,RowCount=4,TabStop=false};
        layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));
        layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text="章节（空格勾选）",AutoSize=true,TabStop=false},0,0);layout.Controls.Add(_chapters,0,1);
        layout.Controls.Add(_message,0,2);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,TabStop=false,TabIndex=1};buttons.Controls.AddRange([_all,_none,_start,_close]);layout.Controls.Add(buttons,0,3);
        Controls.Add(layout);CancelButton=_close;
        _directory=_settings.LoadDirectory()??"";
        _chapters.ItemCheck+=(_,e)=>{if(e.NewValue==CheckState.Checked&&ChapterDownloader.UnavailableReason(((Entry)_chapters.Items[e.Index]).Chapter).Length>0)e.NewValue=CheckState.Unchecked;};
        _all.Click+=(_,_)=>SelectAll(true);_none.Click+=(_,_)=>SelectAll(false);
        _start.Click+=async(_,_)=>{if(!_loaded)await LoadAsync();else Submit();};
        Shown+=async(_,_)=>await LoadAsync();Deactivate+=(_,_)=>_interaction++;
        FormClosed+=(_,_)=>{_life.Cancel();if(!_loading)_life.Dispose();};
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData){_interaction++;return base.ProcessCmdKey(ref msg,keyData);}
    private void SelectAll(bool selected){for(var i=0;i<_chapters.Items.Count;i++)_chapters.SetItemChecked(i,selected&&ChapterDownloader.UnavailableReason(((Entry)_chapters.Items[i]).Chapter).Length==0);}
    private void Report(string text)
    {
        if(IsDisposed)return;_message.Text=text;
        AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,AutomationNotificationProcessing.ImportantMostRecent,text);
    }
    private async Task LoadAsync()
    {
        if(_loading)return;_loading=true;_start.Enabled=false;var interaction=_interaction;var ct=_life.Token;
        try
        {
            IReadOnlyList<Chapter> items=_single is not null?[_single]:await ChapterDownloader.AllChaptersAsync(_api,_album.Id,_session,ct);
            if(IsDisposed||ct.IsCancellationRequested)return;
            _chapters.Items.Clear();foreach(var chapter in items)_chapters.Items.Add(new Entry(chapter));SelectAll(true);
            _loaded=true;_start.Text="开始下载";
            Report(_settings.Error??$"共 {items.Count} 章，已勾选 {_chapters.CheckedItems.Count} 章。");
            if(_chapters.Items.Count>0)_chapters.SelectedIndex=0;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        catch(Exception ex){Report(MainForm.Error(ex));}
        finally
        {
            _loading=false;
            if(IsDisposed||ct.IsCancellationRequested)_life.Dispose();
            else{_start.Enabled=true;if(!_loaded)_start.Text="重新加载章节";if(interaction==_interaction&&ActiveForm==this)_chapters.Focus();}
        }
    }
    private void Submit()
    {
        var items=_chapters.CheckedItems.Cast<Entry>().Select(e=>e.Chapter).ToArray();
        if(items.Length==0){Report("请先勾选可下载的章节。");_chapters.Focus();return;}
        try
        {
            if(!Directory.Exists(_directory)&&!ChooseDirectory()){Report("未选择保存文件夹，尚未开始下载。");return;}
            _start.Enabled=false;
            if(_settings.LoadDirectory()!=_directory)_settings.SaveDirectory(_directory);
            QueuedCount=_registry.Enqueue(_album,items,DownloadSettings.AlbumDirectory(_directory,_album.Name)).Count;
            DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex){_start.Enabled=true;Report(MainForm.Error(ex));}
    }
    internal Task TestLoadAsync()=>LoadAsync();
    internal void TestSubmit()=>Submit();
    internal void TestDirectory(string path)=>_directory=path;
    private bool ChooseDirectory()
    {
        string? selected;
        if(TestFolderPicker is not null)selected=TestFolderPicker(_directory);
        else
        {
            using var dialog=new FolderBrowserDialog{Description="选择下载保存文件夹，以后默认使用此位置，专辑将单独建立子文件夹",UseDescriptionForTitle=true,SelectedPath=Directory.Exists(_directory)?_directory:""};
            selected=dialog.ShowDialog(this)==DialogResult.OK?dialog.SelectedPath:null;
        }
        if(string.IsNullOrWhiteSpace(selected))return false;
        _directory=selected;return true;
    }
}
