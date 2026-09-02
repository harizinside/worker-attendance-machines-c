using System.Globalization;
using System.Text;
using Serilog;

namespace AttendanceAgent.Commands;

public sealed class CommandRunner(string dbPath, IZkClient zk, ICmsClient cms)
{
    private static readonly IReadOnlyDictionary<int,string> StatusLabels=new Dictionary<int,string>{{0,"Masuk"},{1,"Keluar"},{2,"Istirahat Keluar"},{3,"Istirahat Masuk"},{4,"Masuk"},{5,"Keluar"}};
    private Store OpenStore(){var store=new Store(dbPath);store.Init();return store;}
    private static IReadOnlyList<MachineConfig> Resolve(Store store,string? name){var machines=store.GetMachines();if(name is null)return machines;var machine=machines.FirstOrDefault(x=>x.Name==name);return machine is not null?[machine]:throw new ConfigException($"Error: Machine '{name}' not found. Available: {string.Join(", ",machines.Select(x=>x.Name))}");}
    public async Task FetchAsync(string? machineName)
    {
        using var store=OpenStore();var settings=store.GetAppSettings();
        foreach(var machine in Resolve(store,machineName))
        {
            Console.WriteLine($"\n{new string('=',60)}\nMachine: {machine.Name} ({machine.SerialNumber})\n{new string('=',60)}");
            var result=zk.PullLogs(machine.Ip,machine.Port);
            if(!result.Success||result.Data is not List<AttendanceRecord> records){Console.WriteLine($"  FAILED: {result.Message}");store.RecordFetchResult(machine.SerialNumber,false);Console.WriteLine($"  Consecutive failures: {store.GetMachineState(machine.SerialNumber)?.ConsecutiveFailCount??0}");continue;}
            var inserted=store.UpsertLogs(machine.SerialNumber,records);Console.WriteLine($"  Pulled {records.Count} records, inserted {inserted} new rows");
            var unsynced=store.UnsyncedLogs(machine.SerialNumber);
            if(unsynced.Count>0){var push=await cms.PushAttlogAsync(settings.CmsBaseUrl,machine.SerialNumber,unsynced);if(push.Success){store.MarkPushed(machine.SerialNumber,unsynced.Select(x=>(x.FingerId,x.PunchTime)));Console.WriteLine($"  Pushed {unsynced.Count} logs to CMS: {push.Message}");}else Console.WriteLine($"  Failed to push to CMS: {push.Message}");}else Console.WriteLine("  No unsynced logs to push");
            var info=zk.GetDeviceInfo(machine.Ip,machine.Port);if(info.Success&&info.Data is DeviceInfo device){if(device.RecCapacity>0){var pct=device.AttendanceCount*100d/device.RecCapacity;Console.WriteLine($"  Device capacity: {device.AttendanceCount}/{device.RecCapacity} ({pct:F1}%) — users: {device.UsersCount}");if(pct>=settings.CapacityWarningPct){Log.Warning("Machine {Name} ({Serial}) capacity at {Usage:F1}% — approaching limit!",machine.Name,machine.SerialNumber,pct);Console.WriteLine($"  WARNING: Capacity at {pct:F1}% — approaching limit of {settings.CapacityWarningPct}%");}}else Console.WriteLine($"  Device capacity: unknown (rec_capacity={device.RecCapacity})");}else Console.WriteLine($"  Could not get device info: {info.Message}");
            store.RecordFetchResult(machine.SerialNumber,true);Console.WriteLine("  Fetch OK");
        }
    }
    public void Export(string? machineName,string? from,string? to,string output)
    {
        using var store=OpenStore();var machines=Resolve(store,machineName);var rows=new List<string>();
        rows.Add("machine,finger_id,punch_time,status,keterangan");
        foreach(var machine in machines)foreach(var log in store.QueryForExport(machine.SerialNumber,from,to))rows.Add(string.Join(',',Csv(machine.Name),Csv(log.FingerId),Csv(FormatPunchTime(log.PunchTime)),log.Status,Csv(StatusLabels.GetValueOrDefault(log.Status,"?"))));
        var count=rows.Count-1;var range=from is not null||to is not null?$"{from??"..."} to {to??"..."}":"all dates";
        if(count==0){if(File.Exists(output))File.Delete(output);Console.WriteLine($"No attendance records found for {machineName??"semua mesin"} ({range})");return;}File.WriteAllLines(output,rows,new UTF8Encoding(false));Console.WriteLine($"Exported {count} records for {(machineName is null?$"{machines.Count} mesin":$"'{machineName}'")} ({range}) to {output}");
    }
    public void Delete(string machineName,bool force)
    {
        using var store=OpenStore();var machine=Resolve(store,machineName)[0];var count=store.UnsyncedCount(machine.SerialNumber);if(count>0&&!force){Console.Error.WriteLine($"Error: Ada {count} log belum sync ke CMS. Gunakan --force untuk tetap hapus.");return;}Console.WriteLine($"Deleting attendance logs on {machineName} ({machine.SerialNumber})...");var result=zk.ClearLogs(machine.Ip,machine.Port);Console.WriteLine(result.Success?$"  Success: {result.Message}":$"  Failed: {result.Message}");
    }
    public void Status(string? machineName)
    {
        using var store=OpenStore();const string header="Machine                   Reachable  Capacity%    Unsynced   Last Fetch             Fail Count";var separator=new string('-',header.Length);Console.WriteLine($"{separator}\n{header}\n{separator}");
        foreach(var machine in Resolve(store,machineName)){var result=zk.GetDeviceInfo(machine.Ip,machine.Port);var reachable="No";var capacity="N/A";if(result.Success&&result.Data is DeviceInfo info){reachable="Yes";capacity=info.RecCapacity>0?$"{info.AttendanceCount*100d/info.RecCapacity:F1}%":"0.0%";}var state=store.GetMachineState(machine.SerialNumber);var last=state?.LastFetchOkAt?.Split('.')[0]??"Never";Console.WriteLine($"{machine.Name[..Math.Min(24,machine.Name.Length)],-25} {reachable,-10} {capacity,-12} {store.UnsyncedCount(machine.SerialNumber),-10} {last,-22} {state?.ConsecutiveFailCount??0,-10}");}Console.WriteLine(separator);
    }
    public async Task SyncUsersAsync(string machineName)
    {
        using var store=OpenStore();var machine=Resolve(store,machineName)[0];var settings=store.GetAppSettings();Console.WriteLine($"Fetching employees from CMS for {machineName} ({machine.SerialNumber})...");var response=await cms.GetProvisionableEmployeesAsync(settings.CmsBaseUrl,machine.SerialNumber);if(!response.Success){Console.WriteLine($"Error: Failed to fetch employees from CMS: {response.Message}");return;}if(response.Employees.Count==0){Console.WriteLine("No employees found in CMS for this machine.");return;}Console.WriteLine($"Pushing {response.Employees.Count} users to {machineName}...");var result=zk.PushUsers(machine.Ip,response.Employees,machine.Port);Console.WriteLine(result.Success?$"  {result.Message}":$"  Failed: {result.Message}");
    }
    public async Task ScanAsync(string? subnet,int port)
    {
        var discovery=await MachineDiscovery.DiscoverAsync(zk,subnet,port);if(discovery.Error is not null){Console.Error.WriteLine($"Error: {discovery.Error} Isi manual, mis. --subnet 192.168.1");return;}Console.WriteLine($"Scanning {discovery.Subnet}.0/24 port {port}...");if(discovery.Found.Count==0){Console.WriteLine("Tidak ada mesin ditemukan.");return;}using var store=OpenStore();var rows=MatchScanResults(discovery.Found,store.GetMachines());const string header="IP               Port   Serial               Device Name          Status";var separator=new string('-',header.Length);Console.WriteLine($"{separator}\n{header}\n{separator}");foreach(var row in rows)Console.WriteLine($"{row.Ip,-16} {row.Port,-6} {row.SerialNumber??"?",-20} {row.DeviceName??"?",-20} {row.Status}");Console.WriteLine(separator);
    }
    public void UpdateTime(string? machineName){using var store=OpenStore();foreach(var machine in Resolve(store,machineName)){var now=DateTime.Now.AddTicks(-(DateTime.Now.Ticks%TimeSpan.TicksPerSecond));Console.WriteLine($"Updating time on {machine.Name} ({machine.SerialNumber}) to {now:yyyy-MM-dd HH:mm:ss}...");var result=zk.SetDeviceTime(machine.Ip,now,machine.Port);Console.WriteLine(result.Success?$"  Success: {result.Message}":$"  Failed: {result.Message}");}}
    public AppSettings GetSettings(){using var store=OpenStore();return store.GetAppSettings();}
    public void SetCmsBaseUrl(string url){using var store=OpenStore();store.SetCmsBaseUrl(url);}
    public void SetCapacityWarningPct(int pct){using var store=OpenStore();store.SetCapacityWarningPct(pct);}
    public IReadOnlyList<MachineConfig> ListMachines(){using var store=OpenStore();return store.GetMachines();}
    public void AddOrUpdateMachine(MachineConfig machine){using var store=OpenStore();store.UpsertMachine(machine);}
    public bool RemoveMachine(string name){using var store=OpenStore();return store.RemoveMachine(name);}
    public async Task RegisterViaScanAsync(string? subnet,int port){using var store=OpenStore();await MachineDiscovery.RunInteractiveAsync(store,zk,subnet,port);}
    public static IReadOnlyList<IdentifiedDevice> MatchScanResults(IEnumerable<IdentifiedDevice> found,IReadOnlyList<MachineConfig> machines){var bySerial=machines.ToDictionary(x=>x.SerialNumber);return found.Select(x=>{var matched=x.SerialNumber is not null&&bySerial.TryGetValue(x.SerialNumber,out var m)?m:null;var status=matched is null?"Belum terdaftar":matched.Ip==x.Ip?$"Terdaftar ({matched.Name})":$"IP BERUBAH — config: {matched.Ip}, ditemukan: {x.Ip} ({matched.Name})";return x with{Status=status};}).ToList();}
    // Match Python's datetime.isoformat(): no fractional part when the timestamp has none
    // (punch times are always whole seconds), unlike the "O" round-trip format used in storage.
    private static string FormatPunchTime(string raw)
    {
        if(!DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt))return raw;
        var hasFraction=dt.Ticks%TimeSpan.TicksPerSecond!=0;
        return dt.ToString(hasFraction?"yyyy-MM-ddTHH:mm:ss.ffffffzzz":"yyyy-MM-ddTHH:mm:sszzz",CultureInfo.InvariantCulture);
    }
    private static string Csv(string value)=>value.IndexOfAny([',','"','\n','\r'])>=0?$"\"{value.Replace("\"","\"\"")}\"":value;
}
