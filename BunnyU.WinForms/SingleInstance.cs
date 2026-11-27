using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BunnyU.WinForms;

internal sealed class SingleInstance:IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;
    public bool IsPrimary{get;}
    [DllImport("user32.dll")]private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]private static extern nint GetLastActivePopup(nint window);
    public SingleInstance(string? testName=null)
    {
        var name="Local\\BunnyU.Windows."+(testName??WindowsIdentity.GetCurrent().User!.Value+"."+Process.GetCurrentProcess().SessionId);
        _activate=new EventWaitHandle(false,EventResetMode.AutoReset,name+".activate");
        _mutex=new Mutex(false,name+".owner");
        try{IsPrimary=_mutex.WaitOne(0);}catch(AbandonedMutexException){IsPrimary=true;}
    }
    public void SignalExisting()
    {
        var current=Process.GetCurrentProcess();
        foreach(var process in Process.GetProcessesByName(current.ProcessName))
            using(process)try{if(process.Id!=current.Id&&process.SessionId==current.SessionId)AllowSetForegroundWindow(process.Id);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        _activate.Set();
    }
    public void Listen(Action activate)
    {
        _wait=ThreadPool.RegisterWaitForSingleObject(_activate,(_,_)=>activate(),null,Timeout.Infinite,false);
    }
    public static void Activate(Form main)
    {
        if(main.IsDisposed||!main.IsHandleCreated)return;
        if(IsIconic(main.Handle))ShowWindow(main.Handle,9);
        // Respect an open login/settings/update dialog instead of focusing its disabled owner.
        var window=GetLastActivePopup(main.Handle);
        if(window==0||!IsWindowVisible(window))window=main.Handle;
        if(IsIconic(window))ShowWindow(window,9);
        SetForegroundWindow(window);
    }
    public void Dispose(){_wait?.Unregister(null);if(IsPrimary)_mutex.ReleaseMutex();_mutex.Dispose();_activate.Dispose();}
}
