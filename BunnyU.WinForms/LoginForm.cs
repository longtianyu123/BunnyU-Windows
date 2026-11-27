using BunnyU.Core;
using System.Windows.Forms.Automation;
using BunnyU.Desktop;

namespace BunnyU.WinForms;

internal sealed class LoginForm : Form
{
    private readonly IBunnyApi _api;
    private readonly SessionStore _store;
    private readonly TextBox _phone=new(){AccessibleName="手机号",MaxLength=11,TabIndex=0,Dock=DockStyle.Fill};
    private readonly TextBox _code=new(){AccessibleName="验证码",MaxLength=8,TabIndex=2,Dock=DockStyle.Fill};
    private readonly Button _send=new(){Text="获取验证码",AccessibleName="获取验证码",TabIndex=1,AutoSize=true};
    private readonly CheckBox _remember=new(){Text="记住登录状态",AccessibleName="记住登录状态",Checked=true,TabIndex=3,AutoSize=true};
    private readonly Button _login=new(){Text="登录",AccessibleName="登录",TabIndex=4,AutoSize=true};
    private readonly Button _cancel=new(){Text="取消",AccessibleName="取消",DialogResult=DialogResult.Cancel,TabIndex=5,AutoSize=true};
    private readonly Label _message=new(){AutoSize=true,MaximumSize=new Size(400,0),TabStop=false,AccessibleName="登录提示"};
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=1000};
    private readonly CancellationTokenSource _life=new();
    private readonly Func<DateTime> _getCooldown;
    private readonly Action<DateTime> _setCooldown;
    private bool _busy,_sending;
    public UserSession? Session{get;private set;}
    public LoginForm(IBunnyApi api,SessionStore store,Func<DateTime> getCooldown,Action<DateTime> setCooldown)
    {
        _api=api;_store=store;_getCooldown=getCooldown;_setCooldown=setCooldown;
        Text="兔U - 验证码登录";Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(450,350);MinimumSize=new Size(420,380);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),AutoScroll=true,ColumnCount=2,RowCount=6,TabStop=false};
        layout.ColumnStyles.Add(new(SizeType.AutoSize));layout.ColumnStyles.Add(new(SizeType.Percent,100));
        layout.Controls.Add(new Label{Text="手机号",AutoSize=true,TabStop=false,Anchor=AnchorStyles.Left},0,0);layout.Controls.Add(_phone,1,0);
        layout.Controls.Add(_send,1,1);layout.Controls.Add(new Label{Text="验证码",AutoSize=true,TabStop=false,Anchor=AnchorStyles.Left},0,2);layout.Controls.Add(_code,1,2);
        layout.Controls.Add(_remember,1,3);layout.Controls.Add(_message,0,4);layout.SetColumnSpan(_message,2);
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,TabStop=false,TabIndex=4};
        buttons.Controls.AddRange([_login,_cancel]);layout.Controls.Add(buttons,1,5);Controls.Add(layout);
        AcceptButton=_login;CancelButton=_cancel;
        _send.Click+=async(_,_)=>await SendAsync();_login.Click+=async(_,_)=>await LoginAsync();
        _timer.Tick+=(_,_)=>RefreshCooldown();_timer.Start();Shown+=(_,_)=>{_phone.Focus();RefreshCooldown();};
        FormClosed+=(_,_)=>{_life.Cancel();_timer.Stop();_timer.Dispose();_life.Dispose();};
    }
    private void Announce(string text){_message.Text=text;_message.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,AutomationNotificationProcessing.ImportantMostRecent,text);}
    private void RefreshCooldown()
    {
        var seconds=(int)Math.Max(0,Math.Ceiling((_getCooldown()-DateTime.UtcNow).TotalSeconds));
        _send.Enabled=!_busy&&seconds==0;
        var caption=_sending?"正在发送验证码…":seconds>0?$"重新获取（{seconds} 秒）":"获取验证码";
        if(_send.Text!=caption)_send.Text=caption;
        if(_send.AccessibleName!=caption)_send.AccessibleName=caption;
    }
    private async Task SendAsync()
    {
        if(_busy||DateTime.UtcNow<_getCooldown())return;_busy=true;_sending=true;RefreshCooldown();
        try{await _api.SendCodeAsync(_phone.Text.Trim(),_life.Token);if(IsDisposed)return;_setCooldown(DateTime.UtcNow.AddSeconds(60));Announce("验证码已发送。");}
        catch(OperationCanceledException)when(_life.IsCancellationRequested){}
        catch(Exception ex){if(!IsDisposed)Announce(MainForm.Error(ex));}
        finally{_busy=false;_sending=false;if(!IsDisposed)RefreshCooldown();}
    }
    private async Task LoginAsync()
    {
        if(_busy)return;_busy=true;_login.Enabled=false;RefreshCooldown();
        try
        {
            var session=await _api.LoginAsync(_phone.Text.Trim(),_code.Text.Trim(),_life.Token);if(IsDisposed)return;
            // Profile lookup is part of the explicit sign-in action; a failed
            // nickname lookup must not turn successful authentication into failure.
            if(string.IsNullOrWhiteSpace(session.Nickname))
            {
                using var profileTimeout=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
                profileTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                try{session=await _api.AccountAsync(session,profileTimeout.Token);}
                catch(Exception ex)when(ex is BunnyApiException or HttpRequestException or OperationCanceledException){}
            }
            if(IsDisposed)return;
            Session=session;
            try{if(_remember.Checked)_store.Save(session);else _store.Clear();}catch{MessageBox.Show(this,"登录成功，但无法保存本机凭据。","兔U");}
            DialogResult=DialogResult.OK;Close();
        }
        catch(OperationCanceledException)when(_life.IsCancellationRequested){}
        catch(Exception ex){if(!IsDisposed)Announce(MainForm.Error(ex));}
        finally{_busy=false;if(!IsDisposed){_login.Enabled=true;RefreshCooldown();}}
    }
}
