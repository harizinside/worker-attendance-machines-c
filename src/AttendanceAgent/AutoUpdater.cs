using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using AttendanceAgent.Commands;
using Serilog;

namespace AttendanceAgent;

public sealed record ReleaseInfo(string TagName, string DownloadUrl);

public sealed class AutoUpdater : IDisposable
{
    private const string Owner="harizinside";
    private const string Repo="worker-attendance-machines-c";
    private const string AssetName="attendance-agent-windows.zip";
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    public AutoUpdater(HttpClient? httpClient=null){_ownsClient=httpClient is null;_http=httpClient??new HttpClient();_http.Timeout=Timeout.InfiniteTimeSpan;_http.DefaultRequestHeaders.UserAgent.ParseAdd("attendance-agent");_http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");}
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken=default)
    {
        var url=$"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        try
        {
            using var cts=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cts.Token);
            if(response.StatusCode!=HttpStatusCode.OK){Log.Warning("Update check failed: HTTP {StatusCode}",(int)response.StatusCode);return null;}
            var body=await response.Content.ReadAsStringAsync(cts.Token);
            using var doc=JsonDocument.Parse(body);
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("tag_name",out var tag)||!root.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Array)return null;
            foreach(var asset in assets.EnumerateArray())
                if(asset.ValueKind==JsonValueKind.Object&&asset.TryGetProperty("name",out var name)&&name.GetString()==AssetName&&asset.TryGetProperty("browser_download_url",out var download))
                    return new(tag.GetString()??"",download.GetString()??"");
            Log.Warning("Latest release has no {Asset} asset",AssetName);
            return null;
        }
        catch(TaskCanceledException) when(!cancellationToken.IsCancellationRequested){Log.Warning("Update check timed out");return null;}
        catch(HttpRequestException ex){Log.Warning(ex,"Update check failed: connection error");return null;}
        catch(JsonException ex){Log.Warning(ex,"Update check failed: bad JSON");return null;}
        catch(Exception ex){Log.Warning(ex,"Update check failed; continuing with current version");return null;}
    }
    public static bool IsNewer(string remoteTag,Version current)
    {
        if(string.IsNullOrWhiteSpace(remoteTag))return false;
        var text=remoteTag.Trim();
        if(text.StartsWith("v",StringComparison.OrdinalIgnoreCase))text=text[1..];
        return Version.TryParse(text,out var remote)&&remote>current;
    }
    public async Task CheckAndPromptAsync(CommandRunner runner)
    {
        try
        {
            if(!runner.GetAutoUpdateEnabled())return;
            var current=typeof(Program).Assembly.GetName().Version??new Version(0,0,0);
            var release=await GetLatestReleaseAsync();
            if(release is null||!IsNewer(release.TagName,current))return;
            Console.WriteLine($"\nUpdate tersedia: {release.TagName} (versi sekarang: v{current}).");
            if(!Program.Confirm("Download dan install sekarang? (Y/n): "))return;
            var stagingDir=Path.Combine(Path.GetTempPath(),"attendance-agent-update");
            var zipPath=await DownloadAsync(release.DownloadUrl,stagingDir);
            if(zipPath is null)return;
            var extractedDir=Extract(zipPath,stagingDir);
            if(extractedDir is null)return;
            ApplyUpdate(extractedDir);
        }
        catch(Exception ex){Log.Warning(ex,"Auto-update failed; continuing with current version");}
    }
    private async Task<string?> DownloadAsync(string url,string stagingDir)
    {
        try
        {
            Directory.CreateDirectory(stagingDir);
            var zipPath=Path.Combine(stagingDir,AssetName);
            using var cts=new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cts.Token);
            response.EnsureSuccessStatusCode();
            await using var source=await response.Content.ReadAsStreamAsync(cts.Token);
            await using var target=new FileStream(zipPath,FileMode.Create,FileAccess.Write,FileShare.None,64*1024,useAsync:true);
            var total=response.Content.Headers.ContentLength;
            var buffer=new byte[64*1024];
            long downloaded=0;int read;
            while((read=await source.ReadAsync(buffer,cts.Token))>0)
            {
                await target.WriteAsync(buffer.AsMemory(0,read),cts.Token);
                downloaded+=read;
                PrintProgress(downloaded,total);
            }
            Console.WriteLine();
            Log.Information("Downloaded update to {ZipPath} ({Bytes} bytes)",zipPath,downloaded);
            return zipPath;
        }
        catch(Exception ex){Log.Warning(ex,"Failed to download update");Console.WriteLine($"Gagal mengunduh update: {ex.Message}");return null;}
    }
    private static void PrintProgress(long downloaded,long? total)
    {
        if(Console.IsOutputRedirected)return;
        const int width=20;
        string bar;
        if(total is >0)
        {
            var pct=(int)Math.Min(100,downloaded*100/total.Value);
            var filled=pct*width/100;
            bar=$"[{new string('#',filled)}{new string('-',width-filled)}] {pct,3}%";
        }
        else bar=$"{downloaded} bytes";
        Console.Write($"\rDownloading: {bar}");
    }
    private static string? Extract(string zipPath,string stagingDir)
    {
        try
        {
            var extractedDir=Path.Combine(stagingDir,"extracted");
            if(Directory.Exists(extractedDir))Directory.Delete(extractedDir,true);
            ZipFile.ExtractToDirectory(zipPath,extractedDir);
            return extractedDir;
        }
        catch(Exception ex){Log.Warning(ex,"Failed to extract update package");Console.WriteLine("Gagal mengekstrak update.");return null;}
    }
    private static void ApplyUpdate(string extractedDir)
    {
        var exePath=Environment.ProcessPath;
        if(string.IsNullOrEmpty(exePath)){Log.Warning("Cannot determine app path; skipping auto-update install");Console.WriteLine("Tidak bisa menentukan lokasi aplikasi.");return;}
        var appDir=Path.GetDirectoryName(exePath)!;
        var scriptPath=Path.Combine(Path.GetDirectoryName(extractedDir)!,"apply-update.cmd");
        var lines=new[]
        {
            "@echo off",
            ":wait",
            $"tasklist /FI \"PID eq {Environment.ProcessId}\" 2>nul | find /I \"attendance-agent\" >nul",
            "if not errorlevel 1 (",
            "  ping -n 2 127.0.0.1 >nul",
            "  goto wait",
            ")",
            $"xcopy /y /e /i \"{extractedDir}\" \"{appDir}\"",
            $"start \"\" \"{exePath}\"",
            "del \"%~f0\""
        };
        File.WriteAllText(scriptPath,string.Join("\r\n",lines)+Environment.NewLine);
        Console.WriteLine("\nUpdate selesai diunduh. Aplikasi akan ditutup dan otomatis terbuka lagi dengan versi terbaru...");
        Log.Information("Launching apply-update script {Script}",scriptPath);
        Log.CloseAndFlush();
        Process.Start(new ProcessStartInfo{FileName="cmd.exe",Arguments=$"/c \"{scriptPath}\"",UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden});
        Environment.Exit(0);
    }
    public void Dispose(){if(_ownsClient)_http.Dispose();}
}
