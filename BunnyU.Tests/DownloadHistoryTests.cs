using BunnyU.Core;

internal static class DownloadHistoryTests
{
    public static void Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"bunny-history-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"downloads.json");var history=new DownloadHistory(path);
            var chapter=new Chapter(1,"第一集","",10,"https://secret.invalid/signed?token=do-not-persist",false,false,0);
            var records=history.AddBatch(new Album(9,"作品","","",""),[chapter,chapter with{Id=2,Name="第二集"}],root);
            check(records.Count==2&&records[0].BatchId==records[1].BatchId&&records[0].Id!=records[1].Id,"download history groups batch with unique task identities");
            check(!File.ReadAllText(path).Contains("do-not-persist"),"history excludes media URLs and credentials");
            history.Update(records[0].Id,DownloadState.Running,"下载中");
            var saved=File.ReadAllText(path);history.Update(records[0].Id,DownloadState.Running,"下载中",progress:new(128,512));
            check(File.ReadAllText(path)==saved&&history.Records[0].Bytes==128,"progress updates memory without disk writes on every tick");
            history.Update(records[0].Id,DownloadState.Completed,"完成",Path.Combine(root,"第一集.mp3"),new(512,512));
            var restored=new DownloadHistory(path);
            check(restored.Records[0].State==DownloadState.Completed&&restored.Records[0].FilePath==Path.Combine(root,"第一集.mp3")&&restored.Records[0].Bytes==512,"completed download metadata survives restart");
            check(restored.Records[1].State==DownloadState.Interrupted,"unfinished tasks become interrupted without automatic network work");
            var damaged=Path.Combine(root,"damaged.json");File.WriteAllText(damaged,"{broken");var broken=new DownloadHistory(damaged);
            broken.AddBatch(new Album(9,"作品","","",""),[chapter],root);
            check(broken.Error is not null&&File.ReadAllText(damaged)=="{broken"&&broken.Records.Count==1,"corrupt history is preserved while current-session records still work");
            var fileParent=Path.Combine(root,"not-directory");File.WriteAllText(fileParent,"keep");var unwritable=new DownloadHistory(Path.Combine(fileParent,"history.json"));
            unwritable.AddBatch(new Album(9,"作品","","",""),[chapter],root);
            check(unwritable.Error is not null&&unwritable.Records.Count==1,"history save failure stays visible without aborting download tasks");
        }
        finally{Directory.Delete(root,true);}
    }
}
