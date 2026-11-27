using System.Runtime.InteropServices;

namespace BunnyU.WinForms;

// .NET 10 MainMenu/MenuItem are compatibility stubs marked WFDEV006.
// Use actual Win32 HMENU (the same OS menu underlying classic MainMenu),
// retaining native Alt/F10/mnemonics and MSAA, rather than MenuStrip.
internal sealed class NativeMenu : IDisposable
{
    private nint _menu;
    private readonly nint _window;
    private nint _account;
    [DllImport("user32.dll")]static extern nint CreateMenu();
    [DllImport("user32.dll")]static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool AppendMenu(nint menu,uint flags,nuint id,string? text);
    [DllImport("user32.dll")]static extern bool SetMenu(nint window,nint menu);
    [DllImport("user32.dll")]static extern bool DrawMenuBar(nint window);
    [DllImport("user32.dll")]static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool ModifyMenu(nint menu,uint position,uint flags,nuint id,string text);
    [DllImport("user32.dll")]static extern uint EnableMenuItem(nint menu,uint id,uint flags);
    public NativeMenu(nint window)
    {
        _window=window;_menu=CreateMenu();var account=CreatePopupMenu();_account=account;var help=CreatePopupMenu();var settings=CreatePopupMenu();
        if(_menu==0||account==0||help==0||settings==0)throw new InvalidOperationException("无法创建系统菜单。");
        AppendMenu(account,0,101,"验证码登录(&L)…");AppendMenu(account,0,102,"退出本机登录(&O)");
        AppendMenu(account,0,303,"下载管理(&D)\tCtrl+J");
        AppendMenu(account,0x800,0,null);AppendMenu(account,0,103,"退出程序(&Q)");
        AppendMenu(help,0,201,"快捷键(&K)…");
        AppendMenu(help,0,202,"检查更新(&U)…");
        AppendMenu(settings,0,401,"首选项(&P)…");
        AppendMenu(_menu,0x10,(nuint)account,"账号(&A)");AppendMenu(_menu,0x10,(nuint)settings,"设置(&S)");AppendMenu(_menu,0x10,(nuint)help,"帮助(&H)");
        if(!SetMenu(window,_menu))throw new InvalidOperationException("无法设置系统菜单。");DrawMenuBar(window);
    }
    public void SetSignedIn(bool signedIn)
    {
        ModifyMenu(_account,101,0,101,signedIn?"账号信息(&I)…":"验证码登录(&L)…");
        EnableMenuItem(_account,102,signedIn?0u:1u);DrawMenuBar(_window);
    }
    public void Dispose(){if(_menu!=0){SetMenu(_window,0);DestroyMenu(_menu);_menu=0;}}
}

internal static class NativeDownloadMenu
{
    [DllImport("user32.dll")]static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool AppendMenu(nint menu,uint flags,nuint id,string text);
    [DllImport("user32.dll")]static extern uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint owner,nint rect);
    [DllImport("user32.dll")]static extern bool DestroyMenu(nint menu);
    public static uint Show(nint owner,Point screenPoint,bool canDownload)
    {
        var menu=CreatePopupMenu();if(menu==0)throw new InvalidOperationException("无法创建下载菜单。");
        try
        {
            AppendMenu(menu,canDownload?0u:1u,301,"下载(&D)…");AppendMenu(menu,0,302,"批量下载(&B)…");
            return TrackPopupMenu(menu,0x100|0x2,screenPoint.X,screenPoint.Y,0,owner,0);
        }
        finally{DestroyMenu(menu);}
    }
}
