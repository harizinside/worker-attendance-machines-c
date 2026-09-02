using System.Text.Json;
using AttendanceAgent.Commands;

namespace AttendanceAgent;

public sealed record LegacyConfig(string CmsBaseUrl,int CapacityWarningPct,IReadOnlyList<MachineConfig> Machines);

public static class FirstRun
{
    public static LegacyConfig? ParseLegacyConfig(string json)
    {
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(!root.TryGetProperty("cms_base_url",out var urlElement)||urlElement.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(urlElement.GetString()))return null;
            var capacity=90;if(root.TryGetProperty("capacity_warning_pct",out var capacityElement)&&capacityElement.TryGetInt32(out var parsedCapacity))capacity=parsedCapacity;
            var machines=new List<MachineConfig>();
            if(root.TryGetProperty("machines",out var machineElements)&&machineElements.ValueKind==JsonValueKind.Array)foreach(var item in machineElements.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.Object)continue;
                var name=String(item,"name");var ip=String(item,"ip");var serial=String(item,"serial_number");
                if(name is null||ip is null||serial is null||!item.TryGetProperty("port",out var portElement)||!portElement.TryGetInt32(out var port)||port<=0)continue;
                machines.Add(new(name,ip,port,serial));
            }
            return new(urlElement.GetString()!.Trim(),capacity,machines);
        }
        catch(JsonException){return null;}
    }

    public static async Task EnsureConfiguredAsync(Store store,IZkClient zk)
    {
        if(!string.IsNullOrWhiteSpace(store.GetSetting("cms_base_url")))return;
        var legacyPath=FindLegacyConfigPath();
        if(legacyPath is not null)
        {
            LegacyConfig? legacy=null;try{legacy=ParseLegacyConfig(File.ReadAllText(legacyPath));}catch(IOException){}
            if(legacy is not null)
            {
                store.SetCmsBaseUrl(legacy.CmsBaseUrl);store.SetCapacityWarningPct(legacy.CapacityWarningPct);foreach(var machine in legacy.Machines)store.UpsertMachine(machine);
                Console.WriteLine($"Config lama berhasil diimpor: {legacy.Machines.Count} mesin diimpor.");
                try{File.Move(legacyPath,Path.Combine(Path.GetDirectoryName(legacyPath)!,"config.json.imported"),true);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Console.WriteLine($"Peringatan: config.json tidak dapat di-rename: {ex.Message}");}
                return;
            }
        }
        while(true)
        {
            Console.Write("URL CMS: ");var raw=Console.ReadLine();
            if(raw is null)throw new ConfigException("Error: cms_base_url belum diset — input dihentikan sebelum setup awal selesai.");
            var url=raw.Trim();if(string.IsNullOrWhiteSpace(url)){Console.WriteLine("URL CMS tidak boleh kosong.");continue;}
            Console.Write($"URL CMS: {url}\nBenar? (Y/n): ");var confirmRaw=Console.ReadLine();
            if(confirmRaw is null)throw new ConfigException("Error: cms_base_url belum diset — input dihentikan sebelum setup awal selesai.");
            var confirmation=confirmRaw.Trim();if(string.IsNullOrEmpty(confirmation)||confirmation.Equals("y",StringComparison.OrdinalIgnoreCase)){store.SetCmsBaseUrl(url);store.SetCapacityWarningPct(90);break;}
        }
        await MachineDiscovery.RunInteractiveAsync(store,zk,null,4370);
    }

    private static string? FindLegacyConfigPath()
    {
        var candidates=new[]{Path.GetFullPath("config.json"),Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"config.json"))};
        return candidates.Distinct().FirstOrDefault(File.Exists);
    }

    private static string? String(JsonElement element,string name)=>element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString())?value.GetString()!.Trim():null;
}
