using System.Net;
using System.Text;
using BunnyU.Core;

namespace BunnyU.WinForms;

internal static class ConcurrentDownloadSelfTest
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"bunny-parallel-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var handler=new GateHandler();using var registry=new DownloadRegistry(Path.Combine(root,"history.json"),new HttpClient(handler),2);
        async Task Wait(Func<bool> condition){var limit=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<limit)await Task.Delay(10);if(!condition())throw new Exception("concurrency test timeout");}
        try
        {
            registry.Enqueue(new Album(1,"并行测试","","",""),Enumerable.Range(1,6).Select(i=>new Chapter(i,"章节"+i,"",1,"https://test.invalid/"+i,false,false,0)).ToArray(),root);
            await Wait(()=>handler.Requests==2);await Task.Delay(30);check(handler.Requests==2&&handler.Active==2,"download concurrency respects configured two-task limit");
            registry.SetConcurrency(5);await Wait(()=>handler.Requests==5);check(handler.Active==5,"raising concurrency starts queued tasks up to five");
            registry.SetConcurrency(1);handler.Gate.Release();await Wait(()=>handler.Active==4);await Task.Delay(30);
            check(handler.Requests==5,"lowering concurrency keeps active jobs and delays new starts");
            handler.Gate.Release(4);await Wait(()=>handler.Requests==6);check(handler.Active==1&&handler.Maximum<=5,"remaining task waits until the reduced limit permits it");
            handler.Gate.Release();await registry.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));
            check(registry.History.Records.All(r=>r.State==DownloadState.Completed),"all parallel downloads complete with consistent history");
            try{registry.SetConcurrency(6);throw new Exception("limit ignored");}catch(ArgumentOutOfRangeException){check(true,"concurrency above five is rejected");}
        }
        finally{await registry.StopAsync();Directory.Delete(root,true);}
    }
    private sealed class GateHandler:HttpMessageHandler
    {
        public readonly SemaphoreSlim Gate=new(0);public int Requests,Active,Maximum;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);var active=Interlocked.Increment(ref Active);Maximum=Math.Max(Maximum,active);
            try{await Gate.WaitAsync(ct);return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Encoding.ASCII.GetBytes("ID3"+new string('x',2048)))};}
            finally{Interlocked.Decrement(ref Active);}
        }
    }
}
