using System.Net;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Serilog;

namespace AttendanceAgent;

public sealed class CmsClient : ICmsClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    public CmsClient(HttpClient? httpClient=null){_ownsClient=httpClient is null;_http=httpClient??new HttpClient();_http.Timeout=TimeSpan.FromSeconds(30);}
    public async Task<(bool Success,string Message)> PushAttlogAsync(string baseUrl,string serial,IReadOnlyList<StoredLog> logs,CancellationToken cancellationToken=default)
    {
        if(logs.Count==0)return(true,"No logs to push");
        var lines=logs.Select(x=>$"{x.FingerId}\t{NormalizeTimestamp(x.PunchTime)}\t{x.Status}");
        var url=$"{baseUrl.TrimEnd('/')}/iclock/cdata?SN={Uri.EscapeDataString(serial)}&table=ATTLOG";
        try{using var content=new StringContent(string.Join("\n",lines),Encoding.UTF8,"text/plain");using var response=await _http.PostAsync(url,content,cancellationToken);var body=await response.Content.ReadAsStringAsync(cancellationToken);return response.StatusCode==HttpStatusCode.OK&&body.Contains("OK",StringComparison.Ordinal)?(true,"OK"):(false,$"HTTP {(int)response.StatusCode}: {Truncate(body)}");}
        catch(TaskCanceledException) when(!cancellationToken.IsCancellationRequested){return(false,$"Timeout connecting to CMS at {baseUrl}");}
        catch(HttpRequestException ex){return(false,$"Connection error to CMS at {baseUrl}: {ex.Message}");}
        catch(Exception ex){return(false,$"Request failed to CMS at {baseUrl}: {ex.Message}");}
    }
    public async Task<(bool Success,IReadOnlyList<Employee> Employees,string Message)> GetProvisionableEmployeesAsync(string baseUrl,string serial,CancellationToken cancellationToken=default)
    {
        var url=$"{baseUrl.TrimEnd('/')}/iclock/employees?SN={Uri.EscapeDataString(serial)}";
        try{using var response=await _http.GetAsync(url,cancellationToken);var body=await response.Content.ReadAsStringAsync(cancellationToken);if(response.StatusCode!=HttpStatusCode.OK)return(false,[],$"HTTP {(int)response.StatusCode}: {Truncate(body)}");using var doc=JsonDocument.Parse(body);if(doc.RootElement.ValueKind!=JsonValueKind.Array)return(false,[],$"Expected JSON array but got {doc.RootElement.ValueKind}");var employees=new List<Employee>();foreach(var item in doc.RootElement.EnumerateArray()){if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("fingerId",out var finger)||!item.TryGetProperty("name",out var name)){Log.Warning("Skipping malformed employee entry: {Entry}",item.ToString());continue;}employees.Add(new(finger.ToString(),name.ToString()));}return(true,employees,"OK");}
        catch(TaskCanceledException) when(!cancellationToken.IsCancellationRequested){return(false,[],$"Timeout connecting to CMS at {baseUrl}");}
        catch(HttpRequestException ex){return(false,[],$"Connection error to CMS at {baseUrl}: {ex.Message}");}
        catch(JsonException ex){return(false,[],$"Failed to parse JSON response: {ex.Message}");}
        catch(Exception ex){return(false,[],$"Request failed to CMS at {baseUrl}: {ex.Message}");}
    }
    internal static string NormalizeTimestamp(string value)=>DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces,out var dt)?dt.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture):value.Replace('T',' ').Split('+')[0];
    private static string Truncate(string value)=>value.Length<=500?value:value[..500];
    public void Dispose(){if(_ownsClient)_http.Dispose();}
}
