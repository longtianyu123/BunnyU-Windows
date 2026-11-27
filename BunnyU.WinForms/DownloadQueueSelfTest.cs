using BunnyU.Core;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace BunnyU.WinForms;

internal static class DownloadQueueSelfTest
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(nint handle,StringBuilder name,int max);
    public static async Task RunAsync(IBunnyApi api,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"bunny-queue-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var handler=new QueueHandler();using var registry=new DownloadRegistry(Path.Combine(directory,"records.json"),new HttpClient(handler));
            var completions=0;registry.BatchCompleted+=()=>completions++;
            var album=new Album(1,"测试作品","","","");var chapter=new Chapter(1,"第一集","",1,"https://test.invalid/hold",false,false,0);
            var settings=new DownloadSettings(Path.Combine(directory,"software","settings.json"));
            using(var selection=new DownloadForm(api,album,null,chapter,registry,settings))
            {
                await selection.TestLoadAsync();var pickerCount=0;selection.TestFolderPicker=_=>{pickerCount++;return null;};selection.TestSubmit();
                check(pickerCount==1&&selection.QueuedCount==0&&!File.Exists(settings.FilePath)&&registry.History.Records.Count==0,"canceling first folder picker creates no config or download task");
                selection.TestFolderPicker=_=>{pickerCount++;return directory;};selection.TestSubmit();
                check(pickerCount==2&&new DownloadSettings(settings.FilePath).LoadDirectory()==directory,"first confirmed download stores selected root in software config");
                check(selection.QueuedCount==1&&selection.DialogResult==DialogResult.OK,"start queues chapter and closes selection dialog");
            }
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(registry.IsBusy&&registry.History.Records.Single().State==DownloadState.Running,"download survives selection window disposal");
            registry.Enqueue(album,[chapter with{Id=2,Name="失败章节",MediaUrl="https://test.invalid/fail"},chapter with{Id=3,Name="后续章节",MediaUrl="https://test.invalid/ok"}],directory);
            using(var next=new DownloadForm(api,album with{Id=2,Name="另一专辑"},null,chapter with{Id=7,Name="另一集",MediaUrl="https://test.invalid/ok"},registry,new DownloadSettings(settings.FilePath)))
            {
                await next.TestLoadAsync();var prompted=false;next.TestFolderPicker=_=>{prompted=true;return null;};next.TestSubmit();
                check(next.QueuedCount==1&&!prompted&&registry.History.Records.First().Directory==Path.Combine(directory,"另一专辑"),"later downloads reuse saved root without asking and create the right album folder");
            }
            using(var manager=new DownloadManagerForm(registry))
            {
                var name=new StringBuilder(100);GetClassName(manager.TestCategories.Handle,name,100);check(name.ToString().Contains("LISTBOX",StringComparison.OrdinalIgnoreCase),"manager categories use native LISTBOX without expandable nodes");
                name.Clear();GetClassName(manager.TestList.Handle,name,100);check(name.ToString().Contains("LISTBOX",StringComparison.OrdinalIgnoreCase),"manager has native LISTBOX without header");
                check(manager.TestCategories.Items.Cast<PlainListEntry>().Select(n=>n.Text).SequenceEqual(new[]{"正在下载","下载失败","下载完成"}),"manager has exactly three requested nodes");
                manager.TestRefresh();check(manager.TestList.Items.Count==4,"active category includes waiting and running tasks");
                manager.TestList.SelectedIndex=0;var selected=manager.TestList.SelectedEntry!.Text;
                manager.TestCategories.SelectedIndex=1;check(manager.TestList.Items.Count==0,"tree selection updates matching list");
                manager.TestCategories.SelectedIndex=0;
                check(manager.TestList.SelectedItems.Count==1&&manager.TestList.SelectedEntry?.Text==selected,"return to tree node restores selected item");
                handler.Release.TrySetResult();await registry.WhenIdle.WaitAsync(TimeSpan.FromSeconds(10));manager.TestRefresh();
                check(manager.TestList.Items.Count==0,"finished jobs leave active list");
                manager.TestCategories.SelectedIndex=1;check(manager.TestList.Items.Count==1&&manager.TestList.Items[0].ToString()=="失败章节","failed job moves to failed category");
                manager.TestCategories.SelectedIndex=2;check(manager.TestList.Items.Count==3,"queue continues after failed job and completes remaining jobs");
                check(completions==2,"completion notification fires once per fully successful batch, excluding failed batch");
                check(File.Exists(Path.Combine(directory,album.Name,"第一集.mp3"))&&File.Exists(Path.Combine(directory,"后续章节.mp3")),"background queue saves actual media without ID filenames");
            }
            var canceled=registry.Enqueue(album,[chapter with{Id=4,Name="取消任务"}],directory).Single();registry.Cancel(canceled);
            await registry.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
            check(handler.Requests==4&&registry.History.Records.First().State==DownloadState.Canceled,"queued batch can be canceled without making media requests");
            check(completions==2,"canceled batch does not trigger completion notification");
            using(var manager=new DownloadManagerForm(registry))
            {
                _=manager.TestCategories.Handle;_=manager.TestList.Handle;manager.TestCategories.SelectedIndex=1;
                check(manager.TestList.Items.Count==2,"canceled tasks remain visible in failure category with detailed reason");
            }
            await registry.StopAsync();
            var stoppingHandler=new QueueHandler();using var stopping=new DownloadRegistry(Path.Combine(directory,"shutdown.json"),new HttpClient(stoppingHandler));
            stopping.Enqueue(album,[chapter with{Id=5,Name="退出任务"},chapter with{Id=6,Name="退出排队任务"}],directory);
            await stoppingHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await stopping.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(stopping.History.Records.All(r=>r.State==DownloadState.Canceled)&&stoppingHandler.Requests==1,"application shutdown cancels active and pending downloads");
        }
        finally{Directory.Delete(directory,true);}
    }
    private sealed class QueueHandler:HttpMessageHandler
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;if(request.RequestUri!.AbsolutePath=="/hold"){Entered.TrySetResult();await Release.Task.WaitAsync(ct);}
            if(request.RequestUri!.AbsolutePath=="/fail")throw new HttpRequestException("Synthetic failure");
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Encoding.ASCII.GetBytes("ID3"+new string('x',2048)))};
        }
    }
}
