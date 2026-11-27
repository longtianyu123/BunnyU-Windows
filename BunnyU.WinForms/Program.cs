namespace BunnyU.WinForms;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Automated tests use their own UI lifetime and must not activate a user's app.
        using var instance=args.Contains("--self-test")?null:new SingleInstance();
        if(instance is {IsPrimary:false}){instance.SignalExisting();return;}
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>MessageBox.Show(e.Exception.Message,"兔U",MessageBoxButtons.OK,MessageBoxIcon.Error);
        using var main=new MainForm(args);
        main.Shown+=(_,_)=>instance?.Listen(()=>
        {
            try{if(!main.IsDisposed&&main.IsHandleCreated)main.BeginInvoke(()=>SingleInstance.Activate(main));}
            catch(InvalidOperationException){}
        });
        Application.Run(main);
    }
}
