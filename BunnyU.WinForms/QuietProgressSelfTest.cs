using BunnyU.Core;

namespace BunnyU.WinForms;

internal static class QuietProgressSelfTest
{
    // Observe real native list mutation messages, not just managed properties.
    private sealed class MutationObserver:NativeWindow,IDisposable
    {
        public int Mutations;
        public MutationObserver(nint handle)=>AssignHandle(handle);
        protected override void WndProc(ref Message message)
        {
            if(message.Msg is 0x180 or 0x181 or 0x182 or 0x184 or 0x186 or 0x197 or 0xB)Mutations++;
            base.WndProc(ref message);
        }
        public void Dispose()=>ReleaseHandle();
    }
    public static void Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"bunny-quiet-progress-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var registry=new DownloadRegistry(Path.Combine(root,"history.json"));
            var record=registry.History.AddBatch(new Album(1,"专辑","","",""),[new Chapter(1,"第一集","",1,"https://test.invalid/a",false,false,0)],root).Single();
            registry.History.Update(record.Id,DownloadState.Running,"正在下载",progress:new(0,1048576));
            using var manager=new DownloadManagerForm(registry);_=manager.TestList.Handle;manager.TestList.SelectedIndex=0;
            var item=manager.TestList.SelectedItem;
            using var observer=new MutationObserver(manager.TestList.Handle);
            registry.History.Update(record.Id,DownloadState.Running,"正在下载",progress:new(524288,1048576));manager.TestRefresh();
            check(observer.Mutations==0&&ReferenceEquals(item,manager.TestList.SelectedItem),"progress-only update sends no native list mutation, redraw, selection or scroll messages");
            check(manager.TestProgress.CurrentText.Contains("50.0%"),"selected task exposes its individual percentage and byte progress");
            var nativeTextChanges=0;manager.TestProgress.TextChanged+=(_,_)=>nativeTextChanges++;
            registry.History.Update(record.Id,DownloadState.Running,"正在下载",progress:new(786432,1048576));manager.TestRefresh();
            check(manager.TestProgress.CurrentText.Contains("75.0%")&&observer.Mutations==0&&nativeTextChanges==0,"progress refreshes automatically without native text or list change events");
            check(manager.TestProgress.AccessibilityObject.Value?.Contains("75.0%")==true,"screen reader can query current progress through read-only accessible value");
            manager.TestCategories.SelectedIndex=2;
            registry.History.Update(record.Id,DownloadState.Completed,"完成",progress:new(1048576,1048576));manager.TestRefresh();manager.TestList.SelectedIndex=0;manager.TestRefreshProgress();
            check(manager.TestProgress.CurrentText.Contains("100.0%")&&manager.TestProgress.CurrentText.Contains("已完成"),"completed task exposes final progress");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
