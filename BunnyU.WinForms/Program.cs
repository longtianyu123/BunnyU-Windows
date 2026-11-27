namespace BunnyU.WinForms;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>MessageBox.Show(e.Exception.Message,"兔U",MessageBoxButtons.OK,MessageBoxIcon.Error);
        Application.Run(new MainForm(args));
    }
}
