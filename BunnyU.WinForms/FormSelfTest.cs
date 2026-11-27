using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BunnyU.Core;

namespace BunnyU.WinForms;

internal static class FormSelfTest
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(nint h,StringBuilder text,int max);
    [DllImport("user32.dll")]static extern nint GetMenu(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetMenuString(nint menu,uint id,StringBuilder text,int capacity,uint flags);
    public static async Task RunAsync(MainForm form,string[] args)
    {
        var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        async Task Wait(Func<bool> condition,int seconds=25){var deadline=DateTime.UtcNow.AddSeconds(seconds);while(!condition()&&DateTime.UtcNow<deadline)await Task.Delay(100);Check(condition(),"timed condition");}
        try
        {
            IEnumerable<Control> Descendants(Control root)
            {foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
            Check(!Descendants(form).Any(c=>c is Button),"main window contains no buttons");
            Check(form.TestResources is ListBox&&!form.TestResources.MultiColumn,"resource control is a plain single-column list box");
            using(var paging=new MainForm(["--self-test"],new PagingTestApi()))await paging.TestAutoPagingAsync(Check);
            form.TestVolumePreference(Check);
            var preferencesRoot=Path.Combine(Path.GetTempPath(),"bunny-prefs-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                var settings=new DownloadSettings(Path.Combine(preferencesRoot,"settings.json"));
                using(var preferences=new PreferencesForm(settings))
                {
                    _=preferences.TestCategories.Handle;
                    Check(preferences.TestCategories.Items.Cast<string>().SequenceEqual(new[]{"常规","播放"}),"preferences uses exactly general and playback tree nodes");
                    preferences.TestCategories.SelectedIndex=1;
                    Check(preferences.TestPlaybackSelected&&preferences.TestStep.DropDownStyle==ComboBoxStyle.DropDownList&&preferences.TestStep.Items.Count==100,"playback node exposes a native percentage selection combo");
                    preferences.TestStep.SelectedIndex=11;preferences.TestSound.Checked=true;
                    Check(preferences.TestConcurrent.Items.Count==5&&preferences.TestConcurrent.DropDownStyle==ComboBoxStyle.DropDownList,"simultaneous downloads uses a one-to-five combo");preferences.TestConcurrent.SelectedIndex=4;
                    preferences.TestCategories.SelectedIndex=0;
                    Check(!preferences.TestPlaybackSelected&&preferences.TestStep.SelectedIndex==11&&!File.Exists(settings.FilePath),"tree navigation preserves edits without writing configuration");
                    preferences.TestSave();Check(settings.LoadPreferences().VolumeStepPercent==12&&settings.LoadPreferences().DownloadCompleteSound&&settings.LoadPreferences().ConcurrentDownloads==5,"save applies both preference pages together");
                }
                using(var canceled=new PreferencesForm(settings)){canceled.TestStep.SelectedIndex=24;canceled.TestSound.Checked=false;}
                Check(settings.LoadPreferences().VolumeStepPercent==12&&settings.LoadPreferences().DownloadCompleteSound,"closing preferences without save preserves previous settings");
            }
            finally{if(Directory.Exists(preferencesRoot))Directory.Delete(preferencesRoot,true);}
            await DownloadQueueSelfTest.RunAsync(form.TestApi,Check);
            await ConcurrentDownloadSelfTest.RunAsync(Check);
            QuietProgressSelfTest.Run(Check);
            var name=new StringBuilder(100);GetClassName(form.TestResources.Handle,name,100);Check(name.ToString().Contains("LISTBOX",StringComparison.OrdinalIgnoreCase),"native LISTBOX without header");
            name.Clear();GetClassName(form.TestStatus.Handle,name,100);Check(name.ToString().Contains("msctls_statusbar32",StringComparison.OrdinalIgnoreCase),"native status bar: "+name);Check(GetMenu(form.Handle)!=0,"native HMENU");
            name.Clear();GetMenuString(GetMenu(form.Handle),303,name,100,0);Check(name.ToString().Contains("下载管理"),"download manager entry in native menu");
            name.Clear();GetMenuString(GetMenu(form.Handle),401,name,100,0);Check(name.ToString().Contains("首选项"),"preferences entry in native settings menu");
            form.TestAccountPresentation(()=>
            {name.Clear();GetMenuString(GetMenu(form.Handle),101,name,100,0);Check(form.Text=="兔U - 测试听友"&&name.ToString().Contains("账号信息"),"signed-in title and native account-info menu");},()=>
            {name.Clear();GetMenuString(GetMenu(form.Handle),101,name,100,0);Check(form.Text=="兔U"&&name.ToString().Contains("验证码登录"),"signed-out title and login menu restored");});
            if(args.Contains("--self-test-local"))
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-local.json"),JsonSerializer.Serialize(new{success=true,checks,limits="Local native controls and mock network tests only; no real screen reader or live playback verification."}));form.Close();return;
            }
            await form.TestBrowseAsync(Check);
            form.SeedTestRows();form.TestResources.SelectIndex(0);Check(form.TestResources.SelectedEntry?.Text=="测试作品"&&form.TestResources.SelectedIndices.Count==1,"native selected/focused item metadata (hidden test; foreground focus not asserted)");
            Check(form.TestResources.AccessibilityObject.Role==AccessibleRole.List,"native list role");
            Check(form.SelectNextControl(form.TestResources,true,true,true,true)&&!form.TestResources.ContainsFocus,"Tab traversal leaves list");
            var hits=await form.TestApi.SearchAsync("葡萄",1,null);Check(hits.Items.Count>0,"live public search");
            var chapters=await form.TestApi.ChaptersAsync(hits.Items[0].Id,1,null);var free=chapters.Items.First(c=>!c.IsTrial&&!c.IsLocked&&MediaAddress.IsHttp(c.MediaUrl));Check(true,"live public chapters");
            var p=form.TestPlayer;p.Volume=102;Check(p.Volume==100,"volume upper clamp");p.Volume=-1;Check(p.Volume==0,"volume lower clamp");p.Volume=50;
            p.Play(free,[free]);await Wait(()=>p.Position>0.7);p.Toggle();await Wait(()=>!p.IsPlaying,5);await Wait(()=>p.CanSeek,5);Check(p.SeekRelative(5),"fast forward");Check(p.SeekRelative(-9999),"rewind");p.Toggle();await Wait(()=>p.IsPlaying&&p.Position>0.7);p.Stop();Check(!p.IsPlaying,"stop");
            var trial=free with{RequiresEntitlement=true,ExplicitlyUnlocked=false,TrialSeconds=1.5};p.Play(trial,[trial,free]);await Wait(()=>p.Status=="试听结束");Check(p.Current?.Id==trial.Id,"trial does not auto-advance");p.Reset();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test.json"),JsonSerializer.Serialize(new{success=true,checks,limits="No actual screen reader end-to-end verification; no SMS sent."}));form.Close();
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,args.Contains("--self-test-local")?"self-test-local.json":"self-test.json"),JsonSerializer.Serialize(new{success=false,checks,error=ex.ToString()}));form.Close();}
    }
}
