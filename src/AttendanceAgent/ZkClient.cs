using System.Globalization;
using System.Reflection;
using Serilog;

namespace AttendanceAgent;

public sealed class ZkClient : IZkClient
{
    public static readonly TimeSpan DeviceTimeZone = TimeSpan.FromHours(7);
    private const int MachineNumber = 1;

    public OperationResult PullLogs(string ip, int port = 4370, int timeoutSeconds = 10) => Run(ip, port, com =>
    {
        if (!CallBool(com, "ReadGeneralLogData", MachineNumber)) throw LastError(com, "ReadGeneralLogData failed");
        var records = new List<AttendanceRecord>();
        while (true)
        {
            object?[] args = [MachineNumber, "", 0, 0, 0, 0, 0, 0, 0, 0, 0];
            if (!CallBool(com, "SSR_GetGeneralLogData", args)) break;
            try
            {
                var timestamp = new DateTime(Convert.ToInt32(args[4]), Convert.ToInt32(args[5]), Convert.ToInt32(args[6]), Convert.ToInt32(args[7]), Convert.ToInt32(args[8]), Convert.ToInt32(args[9]), DateTimeKind.Unspecified);
                // SDK dwInOutMode is the punch/check type expected by CMS, not dwVerifyMode.
                records.Add(new(Convert.ToString(args[1], CultureInfo.InvariantCulture) ?? "", new DateTimeOffset(timestamp, DeviceTimeZone), Convert.ToInt32(args[3])));
            }
            catch (Exception ex) { Log.Warning(ex, "Skipping malformed attendance record"); }
        }
        return OperationResult.Ok(records);
    }, "pull_logs");

    public OperationResult ClearLogs(string ip, int port = 4370, int timeoutSeconds = 10) => Run(ip, port, com =>
    {
        if (!CallBool(com, "ClearGLog", MachineNumber)) throw LastError(com, "ClearGLog failed");
        return OperationResult.Ok(message: "All attendance logs cleared");
    }, "clear_logs");

    public OperationResult GetDeviceInfo(string ip, int port = 4370, int timeoutSeconds = 10) => Run(ip, port, com =>
    {
        // Standalone SDK status types: 2=user count, 6=attendance count, 7=record capacity.
        // Cross-check these constants against the type-library/docs shipped with the deployed SDK version.
        var users = GetStatus(com, 2); var attendance = GetStatus(com, 6); var capacity = GetStatus(com, 7);
        return OperationResult.Ok(new DeviceInfo(capacity, attendance, users));
    }, "get_device_info");

    public OperationResult SetDeviceTime(string ip, DateTime timestamp, int port = 4370, int timeoutSeconds = 10) => Run(ip, port, com =>
    {
        if (!CallBool(com, "SetDeviceTime2", MachineNumber, timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, timestamp.Second)) throw LastError(com, "SetDeviceTime2 failed");
        return OperationResult.Ok(timestamp, $"Device time updated to {timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
    }, "set_device_time");

    public OperationResult IdentifyDevice(string ip, int port = 4370, int timeoutSeconds = 3) => Run(ip, port, com =>
    {
        var serial = GetOutString(com, "GetSerialNumber");
        string? name;
        try { name = GetOutString(com, "GetProductCode"); }
        catch { name = com.GetType().InvokeMember("DeviceName", BindingFlags.GetProperty, null, com, null)?.ToString(); }
        return OperationResult.Ok(new IdentifiedDevice(ip, port, serial, name));
    }, "identify_device");

    public OperationResult PushUsers(string ip, IReadOnlyList<Employee> users, int port = 4370, int timeoutSeconds = 10) => Run(ip, port, com =>
    {
        // SSR_SetUserInfo provisions by EnrollNumber (= fingerId) directly — the device
        // has no separate numeric uid-slot parameter to assign here (unlike pyzk's set_user).
        var pushed=0;
        foreach(var user in users)
        {
            try { if(CallBool(com,"SSR_SetUserInfo",MachineNumber,user.FingerId,user.Name,"",0,true))pushed++; else Log.Warning("Failed to push user {FingerId}: SDK error {Error}",user.FingerId,GetLastError(com)); }
            catch(Exception ex){Log.Warning(ex,"Failed to push user {FingerId}",user.FingerId);}
        }
        return OperationResult.Ok(pushed,$"Pushed {pushed}/{users.Count} users");
    }, "push_users");

    private static OperationResult Run(string ip,int port,Func<object,OperationResult> action,string operation)
    {
        object? com=null;
        try
        {
            if(!OperatingSystem.IsWindows())return OperationResult.Fail("ZKTeco Standalone SDK is only available on Windows");
            var type=Type.GetTypeFromProgID("zkemkeeper.CZKEM",throwOnError:false); if(type is null)return OperationResult.Fail("zkemkeeper.CZKEM is not registered. Install/register the official ZKTeco Standalone SDK.");
            com=Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not create zkemkeeper.CZKEM");
            if(!CallBool(com,"Connect_Net",ip,port))throw LastError(com,$"Failed to connect to {ip}:{port}");
            return action(com);
        }
        catch(Exception ex){Log.Warning(ex,"{Operation} failed for {Ip}:{Port}",operation,ip,port);return OperationResult.Fail(Unwrap(ex).Message);}
        finally{if(com is not null){try{com.GetType().InvokeMember("Disconnect",BindingFlags.InvokeMethod,null,com,null);}catch(Exception ex){Log.Warning(ex,"Exception during disconnect");}if(OperatingSystem.IsWindows()&&System.Runtime.InteropServices.Marshal.IsComObject(com))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(com);}}
    }
    private static bool CallBool(object target,string method,params object?[] args)=>Convert.ToBoolean(target.GetType().InvokeMember(method,BindingFlags.InvokeMethod,null,target,args));
    private static int GetStatus(object com,int type){object?[] args=[MachineNumber,type,0];if(!CallBool(com,"GetDeviceStatus",args))throw LastError(com,$"GetDeviceStatus({type}) failed");return Convert.ToInt32(args[2]);}
    private static string GetOutString(object com,string method){object?[] args=[MachineNumber,""];if(!CallBool(com,method,args))throw LastError(com,$"{method} failed");return Convert.ToString(args[1])??"";}
    private static int GetLastError(object com){object?[] args=[0];try{com.GetType().InvokeMember("GetLastError",BindingFlags.InvokeMethod,null,com,args);return Convert.ToInt32(args[0]);}catch{return -1;}}
    private static Exception LastError(object com,string message)=>new InvalidOperationException($"{message} (SDK error {GetLastError(com)})");
    private static Exception Unwrap(Exception ex)=>ex is TargetInvocationException { InnerException: not null } tie?tie.InnerException!:ex;
}
