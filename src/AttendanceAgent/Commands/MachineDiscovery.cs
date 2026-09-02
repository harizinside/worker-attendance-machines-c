namespace AttendanceAgent.Commands;

public sealed record DiscoveryResult(string? Subnet,IReadOnlyList<IdentifiedDevice> Found,string? Error);

public static class MachineDiscovery
{
    public static async Task<DiscoveryResult> DiscoverAsync(IZkClient zk,string? subnet,int port)
    {
        subnet=string.IsNullOrWhiteSpace(subnet)?NetScan.GetLocalSubnetPrefix():subnet.Trim().TrimEnd('.');
        if(subnet is null)return new(null,[],"Gagal mendeteksi subnet lokal otomatis.");
        var ips=await NetScan.ScanPortAsync(subnet,port);var found=new List<IdentifiedDevice>();
        foreach(var ip in ips){var result=zk.IdentifyDevice(ip,port);found.Add(result.Success&&result.Data is IdentifiedDevice device?device:new(ip,port,null,null));}
        return new(subnet,found,null);
    }

    public static async Task RunInteractiveAsync(Store store,IZkClient zk,string? subnet,int port)
    {
        var result=await DiscoverAsync(zk,subnet,port);
        if(result.Error is not null){Console.WriteLine($"{result.Error} Anda bisa melanjutkan nanti lewat menu Settings.");return;}
        Console.WriteLine($"Scanning {result.Subnet}.0/24 port {port}...");
        if(result.Found.Count==0){Console.WriteLine("Tidak ada mesin ditemukan. Anda bisa melanjutkan nanti lewat menu Settings.");return;}
        var rows=CommandRunner.MatchScanResults(result.Found,store.GetMachines());for(var i=0;i<rows.Count;i++){var row=rows[i];Console.WriteLine($"{i+1}. {row.Ip}:{row.Port} | {row.SerialNumber??"?"} | {row.DeviceName??"?"} | {row.Status}");}
        Console.Write("Pilih mesin (contoh 1,3 / semua / kosong untuk lewati): ");var selection=Console.ReadLine()?.Trim();if(string.IsNullOrEmpty(selection))return;
        IEnumerable<int> indexes=selection.Equals("semua",StringComparison.OrdinalIgnoreCase)?Enumerable.Range(0,rows.Count):selection.Split(',').Select(x=>int.TryParse(x.Trim(),out var n)?n-1:-1).Where(x=>x>=0&&x<rows.Count).Distinct();
        foreach(var index in indexes){var row=rows[index];Console.Write($"Nama untuk {row.Ip}: ");var name=Console.ReadLine()?.Trim();if(string.IsNullOrWhiteSpace(name)){Console.WriteLine("Dilewati: nama tidak boleh kosong.");continue;}if(string.IsNullOrWhiteSpace(row.SerialNumber)){Console.WriteLine("Dilewati: serial number tidak berhasil dibaca.");continue;}store.UpsertMachine(new(name,row.Ip,row.Port,row.SerialNumber));Console.WriteLine($"Mesin '{name}' didaftarkan.");}
    }
}
