using System.Net;
using System.Text;
using AttendanceAgent;

namespace AttendanceAgent.Tests;

public sealed class AutoUpdaterTests
{
    [Theory]
    [InlineData("v0.1.5","0.1.2",true)]
    [InlineData("0.1.5","0.1.2",true)]
    [InlineData("v0.1.5","0.1.9",false)]
    [InlineData("v0.1.5","0.1.5",false)]
    [InlineData("bukan-versi","0.1.2",false)]
    [InlineData("","0.1.2",false)]
    public void IsNewerComparesRemoteTagAgainstCurrentVersion(string remoteTag,string currentVersion,bool expected)=>Assert.Equal(expected,AutoUpdater.IsNewer(remoteTag,Version.Parse(currentVersion)));

    [Fact] public async Task ParsesLatestReleasePayload()
    {
        var json="""{"tag_name":"v0.1.9","assets":[{"name":"source.zip","browser_download_url":"https://x/source.zip"},{"name":"attendance-agent-windows.zip","browser_download_url":"https://x/attendance-agent-windows.zip"}]}""";
        var http=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")})));
        using var updater=new AutoUpdater(http);
        var release=await updater.GetLatestReleaseAsync();
        Assert.NotNull(release);
        Assert.Equal("v0.1.9",release!.TagName);
        Assert.Equal("https://x/attendance-agent-windows.zip",release.DownloadUrl);
    }
    [Fact] public async Task ReturnsNullWhenAssetMissing()
    {
        var json="""{"tag_name":"v0.1.9","assets":[{"name":"source.zip","browser_download_url":"https://x/source.zip"}]}""";
        var http=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")})));
        using var updater=new AutoUpdater(http);
        Assert.Null(await updater.GetLatestReleaseAsync());
    }
    [Fact] public async Task ReturnsNullOnNonSuccessStatus()
    {
        var http=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound){Content=new StringContent("not found")})));
        using var updater=new AutoUpdater(http);
        Assert.Null(await updater.GetLatestReleaseAsync());
    }
    [Fact] public async Task ReturnsNullOnMalformedJson()
    {
        var http=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{bukan json",Encoding.UTF8,"application/json")})));
        using var updater=new AutoUpdater(http);
        Assert.Null(await updater.GetLatestReleaseAsync());
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>callback(request);}
}
