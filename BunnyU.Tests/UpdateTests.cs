using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using BunnyU.Core;

internal static class UpdateTests
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var bytes=new byte[8192];bytes[0]=(byte)'M';bytes[1]=(byte)'Z';
        var sha=Convert.ToHexString(SHA256.HashData(bytes));
        using var signing=ECDsa.Create(ECCurve.NamedCurves.nistP256);var publicKey=Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
        var url="https://github.com/"+AppUpdater.Repository+"/releases/download/v9.0.0/BunnyU-Setup-9.0.0.exe";
        string Manifest(string address,string digest)
        {
            var payload=System.Text.Encoding.UTF8.GetBytes(string.Join("\n","9.0.0",address,digest.ToLowerInvariant(),"test"));
            var signature=Convert.ToBase64String(signing.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            return JsonSerializer.Serialize(new{version="9.0.0",installerUrl=address,sha256=digest,notes="test",signature});
        }
        var release=AppUpdater.ParseManifest(Manifest(url,sha),publicKey);
        check(AppUpdater.IsNewer(release.Version,new(1,0,0))&&!AppUpdater.IsNewer(new(1,0,0),new(1,0,0,0)),"update version comparison rejects same version and downgrade");
        foreach(var bad in new[]{url.Replace("https:","http:"),url.Replace(AppUpdater.Repository,"attacker/repo"),"file:///tmp/update.exe"})
        {
            try{AppUpdater.ParseManifest(Manifest(bad,sha),publicKey);throw new Exception("unsafe URL accepted");}
            catch(BunnyApiException){check(true,"update executable restricted to HTTPS release URL in configured repository");}
        }
        try{AppUpdater.ParseManifest(Manifest(url,"abc"),publicKey);throw new Exception("bad hash accepted");}
        catch(BunnyApiException){check(true,"update requires complete SHA-256 digest");}
        try{AppUpdater.ParseManifest(Manifest(url,sha).Replace("test","tampered"),publicKey);throw new Exception("tampering accepted");}
        catch(BunnyApiException){check(true,"modified proxy manifest fails signature verification");}
        var root=Path.Combine(Path.GetTempPath(),"bunny-update-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var http=new HttpClient(new Handler(bytes));var updater=new AppUpdater(http,publicKey);
            var manifestHandler=new FallbackHandler(System.Text.Encoding.UTF8.GetBytes(Manifest(url,sha)));using var manifestClient=new HttpClient(manifestHandler);
            var found=await new AppUpdater(manifestClient,publicKey).CheckAsync(new Version(1,0),default);
            check(found?.Version==release.Version&&manifestHandler.Requests==2,"signed manifest can be retrieved through proxy when official route fails");
            var file=await updater.DownloadAsync(release,root,null,default);
            check(File.ReadAllBytes(file).SequenceEqual(bytes),"verified update package stored without executing it");
            var packageHandler=new FallbackHandler(bytes);using var packageClient=new HttpClient(packageHandler);
            await new AppUpdater(packageClient,publicKey).DownloadAsync(release,root,null,default);
            check(packageHandler.Requests==2,"installer fallback still verifies the signed digest");
            try{await updater.DownloadAsync(release with{Sha256=new string('0',64)},root,null,default);throw new Exception("wrong hash accepted");}
            catch(BunnyApiException){check(Directory.GetFiles(root,"*.part").Length==0&&File.ReadAllBytes(file).SequenceEqual(bytes),"invalid package hash removes partial and preserves valid cached installer");}
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            try{await updater.DownloadAsync(release,root,null,canceled.Token);throw new Exception("cancel ignored");}
            catch(OperationCanceledException){check(Directory.GetFiles(root,"*.part").Length==0,"canceled update leaves no partial installer");}
            var large=new byte[2*1024*1024+31];RandomNumberGenerator.Fill(large);large[0]=(byte)'M';large[1]=(byte)'Z';
            var largeRelease=AppUpdater.ParseManifest(Manifest(url,Convert.ToHexString(SHA256.HashData(large))),publicKey);
            var ranged=new RangeHandler(large);using var rangedHttp=new HttpClient(ranged);
            await new AppUpdater(rangedHttp,publicKey).DownloadAsync(largeRelease,root,null,default);
            check(ranged.Peak==5&&File.ReadAllBytes(file).SequenceEqual(large),"update installer downloaded in five concurrent ranges and reconstructed exactly");
            var unsupported=new RangeHandler(large){IgnoreSegments=true};using var fallbackHttp=new HttpClient(unsupported);
            await new AppUpdater(fallbackHttp,publicKey).DownloadAsync(largeRelease,root,null,default);
            check(unsupported.FullRequests==1&&File.ReadAllBytes(file).SequenceEqual(large),"range failure falls back to verified single-stream installer download");
            using var midway=new CancellationTokenSource();var blocked=new RangeHandler(large){Block=true};using var blockedHttp=new HttpClient(blocked);
            var transfer=new AppUpdater(blockedHttp,publicKey).DownloadAsync(largeRelease,root,null,midway.Token);
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));midway.Cancel();
            try{await transfer;throw new Exception("parallel cancel ignored");}
            catch(OperationCanceledException){check(Directory.GetFiles(root,"*.part").Length==0,"parallel cancellation cleans temporary file and stops all segments");}
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class Handler(byte[] bytes):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    }
    private sealed class RangeHandler(byte[] bytes):HttpMessageHandler
    {
        public int Peak,FullRequests;private int _active;public bool IgnoreSegments,Block;
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var range=request.Headers.Range?.Ranges.Single();
            if(range is null){FullRequests++;return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};}
            if(range.To!=0)
            {
                var active=Interlocked.Increment(ref _active);Peak=Math.Max(Peak,active);if(active==5)Started.TrySetResult();
                try{if(Block)await Task.Delay(Timeout.Infinite,ct);else await Task.Delay(80,ct);}
                finally{Interlocked.Decrement(ref _active);}
                if(IgnoreSegments)return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
            }
            var start=range.From!.Value;var end=range.To!.Value;
            var content=new ByteArrayContent(bytes[(int)start..((int)end+1)]);content.Headers.ContentRange=new(start,end,bytes.Length);
            return new(HttpStatusCode.PartialContent){Content=content};
        }
    }
    private sealed class FallbackHandler(byte[] bytes):HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;ct.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host=="github.com"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }
    }
}
