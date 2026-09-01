using System.Net.Sockets;

namespace AttendanceAgent;

public static class NetScan
{
    public static string? GetLocalSubnetPrefix()
    {
        try{using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);socket.Connect("8.8.8.8",80);var ip=((System.Net.IPEndPoint)socket.LocalEndPoint!).Address.ToString();var parts=ip.Split('.');return parts.Length==4?string.Join('.',parts[..3]):null;}catch(SocketException){return null;}
    }
    public static async Task<IReadOnlyList<string>> ScanPortAsync(string prefix,int port=4370,int timeoutMs=300,int maxDegreeOfParallelism=100)
    {
        var found=new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(Enumerable.Range(1,254),new ParallelOptions{MaxDegreeOfParallelism=maxDegreeOfParallelism},async(i,ct)=>{var ip=$"{prefix}.{i}";using var client=new TcpClient();try{await client.ConnectAsync(ip,port,ct).AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs),ct);if(client.Connected)found.Add(ip);}catch(Exception ex) when(ex is SocketException or TimeoutException or OperationCanceledException){}});
        return found.OrderBy(x=>int.Parse(x.Split('.')[3])).ToList();
    }
}
