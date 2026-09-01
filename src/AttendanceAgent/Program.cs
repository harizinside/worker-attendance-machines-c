using AttendanceAgent.Commands;
using Serilog;

namespace AttendanceAgent;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var logPath=Logging.Setup();
        try
        {
            Log.Information("Agent started | version={Version} | dotnet={DotNet} | platform={Platform} | cwd={Cwd}",typeof(Program).Assembly.GetName().Version,Environment.Version,Environment.OSVersion,Directory.GetCurrentDirectory());Log.Information("Log file: {LogPath}",logPath);
            var config=AgentConfig.Load();using var cms=new CmsClient();var runner=new CommandRunner(config,new ZkClient(),cms);
            if(args.Length==0){await InteractiveAsync(runner);return 0;}
            return await DispatchAsync(runner,args);
        }
        catch(ConfigException ex){Console.Error.WriteLine(ex.Message);return 1;}
        catch(ArgumentException ex){Console.Error.WriteLine($"Error: {ex.Message}\n\n{Usage}");return 2;}
        catch(Exception ex){Log.Fatal(ex,"Unhandled exception");Console.Error.WriteLine($"Unexpected error. Send this log file to support: {logPath}");return 1;}
        finally{await Log.CloseAndFlushAsync();}
    }
    private static async Task<int> DispatchAsync(CommandRunner runner,string[] args)
    {
        var options=Parse(args.Skip(1));switch(args[0]){case"fetch":await runner.FetchAsync(Get(options,"machine"));break;case"export":runner.Export(Get(options,"machine"),Get(options,"from"),Get(options,"to"),Required(options,"out"));break;case"delete":runner.Delete(Required(options,"machine"),options.ContainsKey("force"));break;case"status":runner.Status(Get(options,"machine"));break;case"sync-users":await runner.SyncUsersAsync(Required(options,"machine"));break;case"scan":await runner.ScanAsync(Get(options,"subnet"),int.TryParse(Get(options,"port"),out var p)?p:4370);break;case"update-time":runner.UpdateTime(Get(options,"machine"));break;default:throw new ArgumentException($"Unknown command '{args[0]}'");}return 0;
    }
    private static Dictionary<string,string?> Parse(IEnumerable<string> values){var args=values.ToArray();var result=new Dictionary<string,string?>();for(var i=0;i<args.Length;i++){if(!args[i].StartsWith("--"))throw new ArgumentException($"Unexpected argument '{args[i]}'");var key=args[i][2..];if(key=="force"){result[key]=null;continue;}if(i+1>=args.Length||args[i+1].StartsWith("--"))throw new ArgumentException($"--{key} requires a value");result[key]=args[++i];}return result;}
    private static string? Get(Dictionary<string,string?> options,string key)=>options.GetValueOrDefault(key);private static string Required(Dictionary<string,string?> options,string key)=>Get(options,key)??throw new ArgumentException($"--{key} is required");
    private static async Task InteractiveAsync(CommandRunner runner){while(true){Console.WriteLine("\n=== Worker Attendance Machines ===\n1. Fetch      - Tarik log dari mesin\n2. Export     - Export data ke CSV\n3. Delete     - Hapus log di mesin\n4. Status     - Status mesin\n5. Sync Users - Sync karyawan ke mesin\n6. Scan       - Cari mesin ZKTeco di jaringan\n7. Update Time - Sinkronkan waktu mesin dengan komputer\n0. Keluar");Console.Write("Pilih menu: ");switch(Console.ReadLine()?.Trim()){case"1":await runner.FetchAsync(Ask("Nama mesin (kosongkan = semua): "));break;case"2":runner.Export(Ask("Nama mesin (kosongkan = semua): "),Ask("Tanggal awal (YYYY-MM-DD, kosongkan = semua): "),Ask("Tanggal akhir (YYYY-MM-DD, kosongkan = semua): "),Ask("File output CSV: ")??"");break;case"3":runner.Delete(Ask("Nama mesin: ")??"",string.Equals(Ask("Force hapus walau ada unsynced? (y/N): "),"y",StringComparison.OrdinalIgnoreCase));break;case"4":runner.Status(Ask("Nama mesin (kosongkan = semua): "));break;case"5":await runner.SyncUsersAsync(Ask("Nama mesin: ")??"");break;case"6":await runner.ScanAsync(Ask("Subnet (kosongkan = auto-detect, format 192.168.1): "),4370);break;case"7":runner.UpdateTime(Ask("Nama mesin (kosongkan = semua): "));break;case"0":return;default:Console.WriteLine("Pilihan tidak valid.");break;}}}
    private static string? Ask(string prompt){Console.Write(prompt);var value=Console.ReadLine()?.Trim();return string.IsNullOrEmpty(value)?null:value;}
    private const string Usage="Commands: fetch [--machine NAME] | export [--machine NAME] [--from DATE] [--to DATE] --out FILE | delete --machine NAME [--force] | status [--machine NAME] | sync-users --machine NAME | scan [--subnet PREFIX] [--port PORT] | update-time [--machine NAME]";
}
