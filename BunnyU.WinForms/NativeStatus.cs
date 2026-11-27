using System.Runtime.InteropServices;

namespace BunnyU.WinForms;

// Native msctls_statusbar32 supports screen-reader status-bar traversal.
internal sealed class NativeStatus : Control
{
    private readonly string[] _values=["就绪","音量 50%","进度 0:00 / 0:00","未登录"];
    public NativeStatus()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,false);
        TabStop=false;AccessibleName="播放状态";AccessibleRole=AccessibleRole.StatusBar;Height=28;
    }
    [StructLayout(LayoutKind.Sequential)]struct Init{public int Size;public uint Classes;}
    [DllImport("comctl32.dll")]static extern bool InitCommonControlsEx(ref Init init);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint SendMessage(nint h,uint msg,nint w,string text);
    [DllImport("user32.dll")]static extern nint SendMessage(nint h,uint msg,nint w,int[] parts);
    protected override CreateParams CreateParams
    {
        get{var init=new Init{Size=Marshal.SizeOf<Init>(),Classes=4};InitCommonControlsEx(ref init);var cp=base.CreateParams;cp.ClassName="msctls_statusbar32";return cp;}
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Parts();for(int i=0;i<4;i++)SendMessage(Handle,0x40B,i,_values[i]);}
    protected override void OnResize(EventArgs e){base.OnResize(e);if(IsHandleCreated)Parts();}
    private void Parts()
    {
        var scale=DeviceDpi/96d;var account=(int)(130*scale);var progress=(int)(190*scale);var volume=(int)(100*scale);
        var start=Math.Max(120,Width-account-progress-volume);SendMessage(Handle,0x404,4,new[]{start,start+volume,start+volume+progress,-1});
    }
    public void SetPart(int index,string text,bool announce=false)
    {
        if(_values[index]==text)return;_values[index]=text;
        if(IsHandleCreated){SendMessage(Handle,0x40B,index,text);if(announce)AccessibilityNotifyClients(AccessibleEvents.NameChange,index);}
    }
    public string[] Values=>_values.ToArray();
}
