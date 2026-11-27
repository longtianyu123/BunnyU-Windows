using BunnyU.Core;

namespace BunnyU.WinForms;

internal sealed class AccountForm:Form
{
    private readonly IBunnyApi _api;
    private readonly Action<UserSession> _changed;
    private readonly TextBox _info=new(){ReadOnly=true,Multiline=true,AcceptsTab=false,ScrollBars=ScrollBars.Vertical,TabStop=true,Dock=DockStyle.Fill,AccessibleName="账号信息",TabIndex=0};
    private readonly Button _refresh=new(){Text="刷新资料",AccessibleName="刷新资料",AutoSize=true,TabIndex=1};
    private readonly Button _close=new(){Text="关闭",AccessibleName="关闭",AutoSize=true,TabIndex=2,DialogResult=DialogResult.Cancel};
    private readonly Label _message=new(){AutoSize=true,TabStop=false,AccessibleName="资料状态"};
    private readonly CancellationTokenSource _life=new();
    private UserSession _session;
    public AccountForm(IBunnyApi api,UserSession session,Action<UserSession> changed)
    {
        _api=api;_session=session;_changed=changed;
        Text="兔U - 账号信息";Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(470,290);MinimumSize=new Size(410,270);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=3,TabStop=false};
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,TabStop=false,TabIndex=1};buttons.Controls.AddRange([_refresh,_close]);
        layout.Controls.Add(_info,0,0);layout.Controls.Add(_message,0,1);layout.Controls.Add(buttons,0,2);Controls.Add(layout);CancelButton=_close;
        Fill();Shown+=(_,_)=>{_info.Focus();_info.Select(0,0);};
        _refresh.Click+=async(_,_)=>await RefreshAsync();FormClosed+=(_,_)=>{_life.Cancel();_life.Dispose();};
    }
    private void Fill(){_info.Text=$"账号名称：{_session.DisplayName}\r\n账号 ID：{_session.UserId}\r\n手机号：{_session.MaskedMobile}";}
    private async Task RefreshAsync()
    {
        _refresh.Enabled=false;_message.Text="正在读取资料…";
        try{var updated=await _api.AccountAsync(_session,_life.Token);if(IsDisposed)return;_session=updated;_changed(updated);Fill();_message.Text="资料已刷新。";}
        catch(OperationCanceledException)when(_life.IsCancellationRequested){}
        catch(Exception ex){if(!IsDisposed)_message.Text=MainForm.Error(ex);}
        finally{if(!IsDisposed){_refresh.Enabled=true;_message.AccessibilityObject.RaiseAutomationNotification(System.Windows.Forms.Automation.AutomationNotificationKind.Other,System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent,_message.Text);}}
    }
}
