namespace AttendanceAgent;

public sealed record MachineConfig(string Name, string Ip, int Port, string SerialNumber);
public sealed record AppSettings(string CmsBaseUrl, int CapacityWarningPct);
public sealed record AttendanceRecord(string FingerId, DateTimeOffset PunchTime, int Status);
public sealed record DeviceInfo(int RecCapacity, int AttendanceCount, int UsersCount);
public sealed record Employee(string FingerId, string Name);
public sealed record StoredLog(string FingerId, string PunchTime, int Status);
public sealed record MachineState(string MachineSerial, string? LastFetchOkAt, int ConsecutiveFailCount);
public sealed record IdentifiedDevice(string Ip, int Port, string? SerialNumber, string? DeviceName, string Status = "");

public sealed record OperationResult(bool Success, string Message = "", object? Data = null)
{
    public static OperationResult Ok(object? data = null, string message = "OK") => new(true, message, data);
    public static OperationResult Fail(string message) => new(false, message);
}

public interface IZkClient
{
    OperationResult PullLogs(string ip, int port = 4370, int timeoutSeconds = 10);
    OperationResult ClearLogs(string ip, int port = 4370, int timeoutSeconds = 10);
    OperationResult GetDeviceInfo(string ip, int port = 4370, int timeoutSeconds = 10);
    OperationResult SetDeviceTime(string ip, DateTime timestamp, int port = 4370, int timeoutSeconds = 10);
    OperationResult IdentifyDevice(string ip, int port = 4370, int timeoutSeconds = 3);
    OperationResult PushUsers(string ip, IReadOnlyList<Employee> users, int port = 4370, int timeoutSeconds = 10);
}

public interface ICmsClient
{
    Task<(bool Success, string Message)> PushAttlogAsync(string baseUrl, string serial, IReadOnlyList<StoredLog> logs, CancellationToken cancellationToken = default);
    Task<(bool Success, IReadOnlyList<Employee> Employees, string Message)> GetProvisionableEmployeesAsync(string baseUrl, string serial, CancellationToken cancellationToken = default);
}

public sealed class ConfigException(string message) : Exception(message);
