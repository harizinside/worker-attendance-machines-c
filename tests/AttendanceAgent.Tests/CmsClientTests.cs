using System.Net;
using System.Text;
using AttendanceAgent;

namespace AttendanceAgent.Tests;

public sealed class CmsClientTests
{
    [Fact] public async Task PushUsesAdmsWireFormat(){HttpRequestMessage? captured=null;string? body=null;var http=new HttpClient(new Handler(async request=>{captured=request;body=await request.Content!.ReadAsStringAsync();return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("OK: 1")};}));using var cms=new CmsClient(http);var result=await cms.PushAttlogAsync("https://cms.test/","SN 1",[new("EMP1","2025-01-15T08:00:00+07:00",0)]);Assert.True(result.Success);Assert.Contains("SN=SN%201",captured!.RequestUri!.Query);Assert.Equal("EMP1\t2025-01-15 08:00:00\t0",body);Assert.Equal("text/plain",captured.Content!.Headers.ContentType!.MediaType);}
    [Fact] public async Task EmployeesSkipMalformedEntries(){var http=new HttpClient(new Handler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[{\"fingerId\":1,\"name\":\"Budi\"},{\"name\":\"bad\"},5]",Encoding.UTF8,"application/json")})));using var cms=new CmsClient(http);var result=await cms.GetProvisionableEmployeesAsync("https://cms.test","SN1");Assert.True(result.Success);Assert.Equal(new Employee("1","Budi"),Assert.Single(result.Employees));}
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>callback(request);}
}
