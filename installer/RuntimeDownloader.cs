using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

// Targets the Windows-included .NET Framework 4.x, not the missing .NET 10 runtime.
internal static class RuntimeDownloader
{
    private const string Url="https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.15/windowsdesktop-runtime-10.0.15-win-x64.exe";
    private const string Hash="e61b030c1673edfa3c0844502eff83198c46af9e7df70dfc6e2efc5b11ea513a";
    private static readonly long[] Counts=new long[16];
    private static long Total;
    private static int Connections;
    private static string Phase="正在连接微软下载服务器…";
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length<1)return 2;
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit=32;
        if(args.Length>1&&args[1]=="--headless")
        {
            try{Download(args[0],CancellationToken.None).GetAwaiter().GetResult();File.WriteAllText(args[0]+".test.txt","Verified SHA256; parallel connections: "+Connections);return 0;}
            catch(Exception ex){File.WriteAllText(args[0]+".test.txt",ex.ToString());return 1;}
        }
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var cancel=new CancellationTokenSource())using(var form=new Form())
        {
            form.Text="下载运行环境";form.ClientSize=new Size(530,170);form.StartPosition=FormStartPosition.CenterScreen;form.Font=new Font("Microsoft YaHei UI",10);
            var label=new Label{Text=Phase,Dock=DockStyle.Top,Height=65,TabStop=false};
            var bar=new ProgressBar{Dock=DockStyle.Top,Height=28,AccessibleName="运行环境下载进度"};
            var button=new Button{Text="取消",Dock=DockStyle.Bottom,Height=35,TabIndex=0};form.Controls.Add(bar);form.Controls.Add(label);form.Controls.Add(button);
            bool finished=false;int result=1;
            var timer=new System.Windows.Forms.Timer{Interval=400};timer.Tick+=delegate{long bytes=Counts.Sum();bar.Value=Total>0?(int)Math.Min(100,bytes*100/Total):0;label.Text=Phase+"\r\n"+(Total>0?string.Format("{0:F1}%  {1:F1} / {2:F1} MB",bytes*100d/Total,bytes/1048576d,Total/1048576d):"");};
            button.Click+=delegate{cancel.Cancel();button.Enabled=false;};
            form.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!finished){e.Cancel=true;cancel.Cancel();button.Enabled=false;}};
            form.Shown+=async delegate
            {
                timer.Start();
                try{await Download(args[0],cancel.Token);result=0;}
                catch(OperationCanceledException){result=2;}
                catch(Exception ex){MessageBox.Show(form,"下载或校验失败："+ex.Message,"下载运行环境",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                finally{timer.Dispose();finished=true;form.Close();}
            };
            Application.Run(form);return result;
        }
    }
    private static async Task Download(string destination,CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
        var temp=destination+"."+Guid.NewGuid().ToString("N")+".part";
        using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=TimeSpan.FromSeconds(120)})
        try
        {
            bool parallel=false;
            using(var request=new HttpRequestMessage(HttpMethod.Get,Url))
            {
                request.Headers.Range=new RangeHeaderValue(0,0);
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct))
                {
                    response.EnsureSuccessStatusCode();var range=response.Content.Headers.ContentRange;
                    if(response.StatusCode==HttpStatusCode.PartialContent&&range!=null&&range.From==0&&range.To==0&&range.Length.HasValue)
                    {Total=range.Length.Value;parallel=Total>=1048576&&Total<536870912;}
                }
            }
            if(parallel)
            {
                bool fallback=false;
                try{await Parallel(client,temp,ct);}
                catch(OperationCanceledException){throw;}
                catch{ct.ThrowIfCancellationRequested();if(File.Exists(temp))File.Delete(temp);Array.Clear(Counts,0,Counts.Length);fallback=true;}
                if(fallback)await Single(client,temp,ct);
            }
            else await Single(client,temp,ct);
            Phase="正在校验运行环境…";ct.ThrowIfCancellationRequested();
            using(var sha=SHA256.Create())using(var stream=File.OpenRead(temp))
            {if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=Hash)throw new IOException("SHA-256 不匹配，已阻止安装。");}
            ct.ThrowIfCancellationRequested();if(File.Exists(destination))File.Delete(destination);File.Move(temp,destination);
        }
        finally{if(File.Exists(temp))File.Delete(temp);for(int i=0;i<16;i++){var part=temp+"."+i;if(File.Exists(part))File.Delete(part);}}
    }
    private static async Task Parallel(HttpClient client,string temp,CancellationToken ct)
    {
        Phase="正在使用 16 路并行下载运行环境…";
        using(var group=CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var tasks=Enumerable.Range(0,16).Select(async index=>
            {
                try
                {
                    long start=Total*index/16,end=Total*(index+1)/16-1;
                    using(var request=new HttpRequestMessage(HttpMethod.Get,Url))
                    {
                        request.Headers.Range=new RangeHeaderValue(start,end);
                        using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,group.Token))
                        {
                            Interlocked.Increment(ref Connections);var range=response.Content.Headers.ContentRange;
                            if(response.StatusCode!=HttpStatusCode.PartialContent||range==null||range.From!=start||range.To!=end||range.Length!=Total)throw new IOException("服务器不支持分段下载。");
                            using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(temp+"."+index,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                                await Copy(input,output,index,end-start+1,group.Token);
                        }
                    }
                }
                catch{group.Cancel();throw;}
            }).ToArray();
            try{await Task.WhenAll(tasks);}
            catch{ct.ThrowIfCancellationRequested();throw new IOException("分段下载失败。");}
        }
        using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            for(int i=0;i<16;i++)using(var part=File.OpenRead(temp+"."+i))await part.CopyToAsync(output,65536,ct);
    }
    private static async Task Single(HttpClient client,string temp,CancellationToken ct)
    {
        Phase="分段不可用，正在使用兼容方式下载…";
        using(var response=await client.GetAsync(Url,HttpCompletionOption.ResponseHeadersRead,ct))
        {
            response.EnsureSuccessStatusCode();Total=response.Content.Headers.ContentLength??0;
            using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))await Copy(input,output,0,Total,ct);
        }
    }
    private static async Task Copy(Stream input,Stream output,int index,long expected,CancellationToken ct)
    {
        using(var stall=CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var buffer=new byte[65536];long count=0;
            while(true)
            {
                stall.CancelAfter(TimeSpan.FromSeconds(60));int read=await input.ReadAsync(buffer,0,buffer.Length,stall.Token);if(read==0)break;
                count+=read;if(count>536870912||(expected>0&&count>expected))throw new IOException("下载数据长度异常。");
                await output.WriteAsync(buffer,0,read,stall.Token);Interlocked.Exchange(ref Counts[index],count);
            }
            if(expected>0&&count!=expected)throw new IOException("下载未完成。");
        }
    }
}
