using BunnyU.Core;
using System.Windows.Forms.Automation;

namespace BunnyU.WinForms;

internal sealed class PreferencesForm:Form
{
    private readonly PlainListBox _categories=new(){AccessibleName="设置分类",TabIndex=0};
    private readonly Panel _pages=new(){Dock=DockStyle.Fill,TabStop=false,TabIndex=1};
    private readonly TableLayoutPanel _general=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new(12),TabStop=false,TabIndex=0,AutoScroll=true};
    private readonly TableLayoutPanel _playback=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new(12),TabStop=false,TabIndex=0,AutoScroll=true};
    private readonly CheckBox _sound=new(){Text="下载完成后播放提示音",AccessibleName="下载完成后播放提示音",AutoSize=true,TabIndex=0};
    private readonly TextBox _directory=new(){ReadOnly=true,Dock=DockStyle.Fill,AccessibleName="下载保存位置",TabIndex=0};
    private readonly ComboBox _step=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,AccessibleName="音量调整百分比",TabIndex=0};
    private readonly ComboBox _quality=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,AccessibleName="默认播放音质",TabIndex=1};
    private readonly ComboBox _concurrent=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,AccessibleName="同时下载任务数",TabIndex=1};
    private readonly Label _message=new(){AutoSize=true,TabStop=false,Dock=DockStyle.Fill};
    private readonly DownloadSettings _settings;
    private bool PlaybackSelected=>_categories.SelectedIndex==1;
    public PreferencesForm(DownloadSettings settings)
    {
        _settings=settings;Text="设置";Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new(760,360);MinimumSize=new(610,320);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new(10),TabStop=false};
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));
        var split=new SplitContainer{Dock=DockStyle.Fill,TabStop=false,TabIndex=0,Size=new(740,260),SplitterDistance=170,Panel1MinSize=120,Panel2MinSize=330};
        split.Panel1.TabStop=false;split.Panel1.TabIndex=0;split.Panel2.TabStop=false;split.Panel2.TabIndex=1;
        split.Panel1.Controls.Add(_categories);split.Panel2.Controls.Add(_pages);layout.Controls.Add(split,0,0);
        _categories.Items.Add("常规");_categories.Items.Add("播放");
        for(var i=0;i<6;i++)_general.RowStyles.Add(new(SizeType.AutoSize));
        _general.Controls.Add(_sound,0,0);
        _general.Controls.Add(new Label{Text="同时下载任务数",AutoSize=true,TabStop=false,Margin=new(3,12,3,3)},0,1);
        for(var i=1;i<=5;i++)_concurrent.Items.Add(i+" 个");_general.Controls.Add(_concurrent,0,2);
        _general.Controls.Add(new Label{Text="下载保存位置",AutoSize=true,TabStop=false,Margin=new(3,16,3,3)},0,3);
        var location=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,TabStop=false,TabIndex=2};
        location.ColumnStyles.Add(new(SizeType.Percent,100));location.ColumnStyles.Add(new(SizeType.AutoSize));
        var browse=new Button{Text="浏览…",AccessibleName="选择下载文件夹",AutoSize=true,TabIndex=1};
        location.Controls.Add(_directory,0,0);location.Controls.Add(browse,1,0);_general.Controls.Add(location,0,4);
        _general.Controls.Add(new Label{Text="未设置时首次下载会询问。每个专辑单独建立文件夹。",AutoSize=true,TabStop=false},0,5);
        _playback.RowStyles.Add(new(SizeType.AutoSize));_playback.RowStyles.Add(new(SizeType.AutoSize));_playback.RowStyles.Add(new(SizeType.AutoSize));
        _playback.Controls.Add(new Label{Text="音量调整百分比",AutoSize=true,TabStop=false},0,0);
        for(var i=1;i<=100;i++)_step.Items.Add(i+"%");
        _playback.Controls.Add(_step,0,1);
        _playback.Controls.Add(new Label{Text="Ctrl+↑ / Ctrl+↓ 每次增加或减少的音量。",AutoSize=true,TabStop=false},0,2);
        for(var i=0;i<3;i++)_playback.RowStyles.Add(new(SizeType.AutoSize));
        _quality.Items.AddRange(["默认（低到高）","高音质优先","最高音质（高到低）"]);
        _playback.Controls.Add(new Label{Text="默认播放音质",AutoSize=true,TabStop=false,Margin=new(3,12,3,3)},0,3);
        _playback.Controls.Add(_quality,0,4);
        _playback.Controls.Add(new Label{Text="默认：普通→高音质→FLAC。最高：FLAC→高音质→普通。\n高音质优先：高音质→普通→FLAC。均以原始地址兜底。",AutoSize=true,TabStop=false},0,5);
        _pages.Controls.Add(_general);_pages.Controls.Add(_playback);
        var preferences=settings.LoadPreferences();_directory.Text=preferences.DownloadDirectory??"";_sound.Checked=preferences.DownloadCompleteSound;_step.SelectedIndex=preferences.VolumeStepPercent-1;
        _quality.SelectedIndex=(int)preferences.PlaybackQuality;
        _concurrent.SelectedIndex=preferences.ConcurrentDownloads-1;
        _message.Text=settings.Error??"";layout.Controls.Add(_message,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,TabStop=false,TabIndex=1};
        var save=new Button{Text="保存",AccessibleName="保存",AutoSize=true,TabIndex=0};
        var cancel=new Button{Text="取消",AccessibleName="取消",AutoSize=true,TabIndex=1,DialogResult=DialogResult.Cancel};
        buttons.Controls.AddRange([save,cancel]);layout.Controls.Add(buttons,0,2);Controls.Add(layout);CancelButton=cancel;
        _categories.SelectedIndexChanged+=(_,_)=>ShowPage();_categories.SelectedIndex=0;ShowPage();
        browse.Click+=(_,_)=>{
            using var dialog=new FolderBrowserDialog{Description="选择下载保存文件夹",UseDescriptionForTitle=true,SelectedPath=Directory.Exists(_directory.Text)?_directory.Text:""};
            if(dialog.ShowDialog(this)==DialogResult.OK)_directory.Text=dialog.SelectedPath;
        };
        save.Click+=(_,_)=>Save();
        Shown+=(_,_)=>_categories.Focus();
    }
    private void ShowPage()
    {
        _general.Visible=!PlaybackSelected;_playback.Visible=PlaybackSelected;
        if(PlaybackSelected)_playback.BringToFront();else _general.BringToFront();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==Keys.Tab&&_categories.ContainsFocus){if(PlaybackSelected)_step.Focus();else _sound.Focus();return true;}
        if(keyData==(Keys.Shift|Keys.Tab)&&(_sound.ContainsFocus||_step.ContainsFocus)){_categories.Focus();return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void Save()
    {
        try
        {
            _settings.SavePreferences(new(_directory.Text.Length==0?null:_directory.Text,_sound.Checked,_step.SelectedIndex+1,_concurrent.SelectedIndex+1,true,(PlaybackQuality)_quality.SelectedIndex));
            DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex){_message.Text=MainForm.Error(ex);_message.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,AutomationNotificationProcessing.ImportantMostRecent,_message.Text);}
    }
    internal PlainListBox TestCategories=>_categories;
    internal CheckBox TestSound=>_sound;
    internal ComboBox TestStep=>_step;
    internal ComboBox TestConcurrent=>_concurrent;
    internal bool TestPlaybackSelected=>PlaybackSelected;
    internal void TestSave()=>Save();
}
