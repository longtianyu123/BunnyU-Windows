using System.Diagnostics;
using BunnyU.Core;

namespace BunnyU.WinForms;

internal sealed class DownloadManagerForm:Form
{
    private sealed record Filter(string Key,string Name);
    private readonly PlainListBox _categories=new(){AccessibleName="下载分类",TabIndex=0};
    private readonly PlainListBox _list=new(){AccessibleName="下载内容",TabIndex=1};
    private readonly QuietProgressView _progress=new(){Dock=DockStyle.Fill,AccessibleName="选中任务进度",TabIndex=2};
    private readonly Label _summary=new(){Dock=DockStyle.Bottom,AutoSize=true,TabStop=false};
    private readonly DownloadRegistry _registry;
    private readonly Dictionary<string,Guid> _selection=[];
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=400};
    private string _current="active";
    private bool _dirty,_refreshing;
    private Guid? _progressId;
    public DownloadManagerForm(DownloadRegistry registry)
    {
        _registry=registry;Text="兔U - 下载管理";Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new(900,560);MinimumSize=new(620,380);StartPosition=FormStartPosition.CenterParent;
        var split=new SplitContainer{Dock=DockStyle.Fill,TabStop=false,TabIndex=0,Size=new(900,530),SplitterDistance=230,Panel1MinSize=150,Panel2MinSize=250};
        split.Panel1.TabStop=false;split.Panel1.TabIndex=0;split.Panel2.TabStop=false;split.Panel2.TabIndex=1;
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,TabStop=false};
        right.RowStyles.Add(new(SizeType.Percent,100));right.RowStyles.Add(new(SizeType.AutoSize));right.RowStyles.Add(new(SizeType.Absolute,100));
        right.Controls.Add(_list,0,0);right.Controls.Add(new Label{Text="选中任务进度（F8）",AutoSize=true,TabStop=false},0,1);right.Controls.Add(_progress,0,2);
        split.Panel1.Controls.Add(_categories);split.Panel2.Controls.Add(right);Controls.Add(split);Controls.Add(_summary);
        foreach(var filter in new[]{new Filter("active","正在下载"),new Filter("failed","下载失败"),new Filter("completed","下载完成")})
            _categories.Items.Add(new PlainListEntry(filter.Key,filter.Name,filter));
        _categories.SelectedIndex=0;
        _categories.SelectedIndexChanged+=(_,_)=>{RememberSelection();_current=CurrentFilter.Key;RefreshRows();};
        _list.SelectedIndexChanged+=(_,_)=>{if(!_refreshing&&_list.SelectedEntry?.Data is DownloadRecord record){_selection[_current]=record.Id;RefreshProgress();}};
        _list.Enter+=(_,_)=>SelectListRow();
        _list.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;ShowDetails();}};
        _list.DoubleClick+=(_,_)=>ShowDetails();
        _progress.Enter+=(_,_)=>RefreshProgress(true);
        _registry.History.Changed+=HistoryChanged;_timer.Tick+=(_,_)=>{if(_dirty){_dirty=false;RefreshRows();}};_timer.Start();
        Shown+=(_,_)=>_categories.Focus();FormClosed+=(_,_)=>{_registry.History.Changed-=HistoryChanged;_timer.Dispose();};RefreshRows();
    }
    private Filter CurrentFilter=>_categories.SelectedEntry?.Data as Filter??new("active","正在下载");
    private void HistoryChanged()=>_dirty=true;
    private void RememberSelection(){if(_list.SelectedEntry?.Data is DownloadRecord record)_selection[_current]=record.Id;}
    private void RefreshRows()
    {
        var filter=CurrentFilter;var rows=_registry.History.Records.Where(r=>filter.Key switch
        {
            "active"=>r.State is DownloadState.Waiting or DownloadState.Running,
            "completed"=>r.State==DownloadState.Completed,
            _=>r.State is DownloadState.Failed or DownloadState.Canceled or DownloadState.Interrupted
        }).ToArray();
        _selection.TryGetValue(filter.Key,out var selected);
        _refreshing=true;
        try
        {
            _list.SetRows(rows.Select(r=>new PlainListEntry(r.Id.ToString(),r.ChapterName,r)).ToArray(),selected.ToString());
            if(_list.ContainsFocus&&_list.SelectedItems.Count==0)SelectListRow();
        }
        finally{_refreshing=false;}
        RememberSelection();
        if(_list.AccessibleName!=filter.Name)_list.AccessibleName=filter.Name;
        var summary=_registry.History.Error??$"{filter.Name}，共 {rows.Length} 项。F8 查看所选任务进度，F5 刷新。";
        if(_summary.Text!=summary)_summary.Text=summary;
        RefreshProgress();
    }
    private void RefreshProgress(bool force=false)
    {
        var selected=_list.SelectedEntry?.Data as DownloadRecord;
        if(!_progress.ContainsFocus)_progressId=selected?.Id;
        var record=_progressId.HasValue?_registry.History.Records.FirstOrDefault(r=>r.Id==_progressId.Value):null;
        var text=record is null?"请选择一个下载任务。":$"{record.ChapterName}\r\n{DownloadHistory.StateName(record.State)}，"+
            (record.Total>0?$"{Math.Clamp(record.Bytes*100d/record.Total.Value,0,100):F1}%（{record.Bytes/1048576d:F2} / {record.Total.Value/1048576d:F2} MB）":$"已下载 {record.Bytes/1048576d:F2} MB，总大小未知")+$"\r\n{record.Message}";
        _progress.UpdateProgress(text);
    }
    private void SelectListRow()
    {
        if(_list.SelectedIndex<0&&_list.Items.Count>0)_list.SelectedIndex=0;
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==Keys.Tab&&_categories.ContainsFocus){_list.Focus();return true;}
        if(keyData==(Keys.Shift|Keys.Tab)&&_list.ContainsFocus){_categories.Focus();return true;}
        if(keyData==Keys.Tab&&_list.ContainsFocus){_progress.Focus();return true;}
        if(keyData==(Keys.Shift|Keys.Tab)&&_progress.ContainsFocus){_list.Focus();return true;}
        if(keyData==Keys.Tab&&_progress.ContainsFocus){_categories.Focus();return true;}
        if(keyData==(Keys.Shift|Keys.Tab)&&_categories.ContainsFocus){_progress.Focus();return true;}
        if(keyData==(Keys.Control|Keys.J)){_categories.Focus();return true;}
        if(keyData==Keys.F8){SelectListRow();RefreshProgress(true);_progress.Focus();return true;}
        if(keyData==Keys.F5){RememberSelection();RefreshRows();RefreshProgress(true);return true;}
        if(keyData==Keys.Escape){Close();return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void ShowDetails()
    {
        if(_list.SelectedEntry?.Data is not DownloadRecord record)return;
        using var details=new Form{Text="下载详情",Font=Font,AutoScaleMode=AutoScaleMode.Dpi,ClientSize=new(610,370),StartPosition=FormStartPosition.CenterParent};
        var text=new TextBox{ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,TabIndex=0,AccessibleName="下载详情"};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,TabStop=false,TabIndex=1};
        var open=new Button{Text="打开保存位置",AutoSize=true,TabIndex=0};var cancel=new Button{Text="取消本批任务",AutoSize=true,TabIndex=1,Enabled=_registry.CanCancel(record)};
        var close=new Button{Text="关闭",AutoSize=true,TabIndex=2,DialogResult=DialogResult.Cancel};buttons.Controls.AddRange([open,cancel,close]);
        text.Text=$"章节：{record.ChapterName}\r\n专辑：{record.AlbumName}\r\n状态：{DownloadHistory.StateName(record.State)}\r\n进度：{record.Bytes/1048576d:F1} MB"+(record.Total>0?$" / {record.Total/1048576d:F1} MB":"")+$"\r\n保存位置：{record.FilePath??record.Directory}\r\n结果：{record.Message}\r\n更新时间：{record.UpdatedAt.LocalDateTime:g}";
        open.Click+=(_,_)=>{try{if(!Directory.Exists(record.Directory))throw new IOException("保存目录尚不存在。");Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={record.Directory},UseShellExecute=true});}catch(Exception ex){MessageBox.Show(details,MainForm.Error(ex),"打开保存位置");}};
        cancel.Click+=(_,_)=>{_registry.Cancel(record);cancel.Enabled=false;text.AppendText("\r\n已请求取消本批任务。");};
        details.Controls.Add(text);details.Controls.Add(buttons);details.CancelButton=close;details.Shown+=(_,_)=>{text.Focus();text.Select(0,0);};details.ShowDialog(this);_list.Focus();
    }
    internal PlainListBox TestCategories=>_categories;
    internal PlainListBox TestList=>_list;
    internal QuietProgressView TestProgress=>_progress;
    internal void TestRefreshProgress()=>RefreshProgress(true);
    internal void TestRefresh()=>RefreshRows();
}
