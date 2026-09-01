using Microsoft.Data.Sqlite;

namespace AttendanceAgent;

public sealed class Store : IDisposable
{
    public SqliteConnection Connection { get; }
    public Store(string path) : this(new SqliteConnection($"Data Source={path};Default Timeout=30")) { }
    public Store(SqliteConnection connection)
    {
        Connection = connection;
        if (Connection.State != System.Data.ConnectionState.Open) Connection.Open();
        using var pragma = Connection.CreateCommand(); pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;"; pragma.ExecuteNonQuery();
    }
    public void Init()
    {
        using var cmd = Connection.CreateCommand(); cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS attendance_logs (
              machine_serial TEXT NOT NULL, finger_id TEXT NOT NULL, punch_time TEXT NOT NULL,
              status INTEGER NOT NULL, pushed_to_cms INTEGER NOT NULL DEFAULT 0, fetched_at TEXT NOT NULL,
              UNIQUE(machine_serial, finger_id, punch_time));
            CREATE TABLE IF NOT EXISTS machine_state (
              machine_serial TEXT PRIMARY KEY, last_fetch_ok_at TEXT,
              consecutive_fail_count INTEGER NOT NULL DEFAULT 0);
            """; cmd.ExecuteNonQuery();
    }
    public int UpsertLogs(string serial, IEnumerable<AttendanceRecord> logs)
    {
        var inserted = 0; var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var log in logs) { using var cmd = Command("INSERT OR IGNORE INTO attendance_logs(machine_serial,finger_id,punch_time,status,pushed_to_cms,fetched_at) VALUES($s,$f,$t,$c,0,$a)"); Add(cmd,"$s",serial); Add(cmd,"$f",log.FingerId); Add(cmd,"$t",log.PunchTime.ToString("O")); Add(cmd,"$c",log.Status); Add(cmd,"$a",now); inserted += cmd.ExecuteNonQuery(); }
        return inserted;
    }
    public IReadOnlyList<StoredLog> UnsyncedLogs(string serial) => QueryLogs("SELECT finger_id,punch_time,status FROM attendance_logs WHERE machine_serial=$s AND pushed_to_cms=0 ORDER BY punch_time", serial);
    public int UnsyncedCount(string serial) { using var cmd=Command("SELECT COUNT(*) FROM attendance_logs WHERE machine_serial=$s AND pushed_to_cms=0"); Add(cmd,"$s",serial); return Convert.ToInt32(cmd.ExecuteScalar()); }
    public void MarkPushed(string serial, IEnumerable<(string FingerId,string PunchTime)> ids) { foreach(var id in ids){ using var cmd=Command("UPDATE attendance_logs SET pushed_to_cms=1 WHERE machine_serial=$s AND finger_id=$f AND punch_time=$t"); Add(cmd,"$s",serial);Add(cmd,"$f",id.FingerId);Add(cmd,"$t",id.PunchTime);cmd.ExecuteNonQuery(); } }
    public IReadOnlyList<StoredLog> QueryForExport(string serial,string? from=null,string? to=null)
    {
        var sql="SELECT finger_id,punch_time,status FROM attendance_logs WHERE machine_serial=$s"; if(from is not null)sql+=" AND punch_time >= $from"; if(to is not null)sql+=" AND punch_time < $to || 'T23:59:59.999999+00:00'"; sql+=" ORDER BY punch_time";
        using var cmd=Command(sql);Add(cmd,"$s",serial);if(from is not null)Add(cmd,"$from",from);if(to is not null)Add(cmd,"$to",to);return ReadLogs(cmd);
    }
    public void RecordFetchResult(string serial,bool ok)
    {
        using var cmd=Command(ok ? "INSERT INTO machine_state(machine_serial,last_fetch_ok_at,consecutive_fail_count) VALUES($s,$n,0) ON CONFLICT(machine_serial) DO UPDATE SET last_fetch_ok_at=excluded.last_fetch_ok_at,consecutive_fail_count=0" : "INSERT INTO machine_state(machine_serial,consecutive_fail_count) VALUES($s,1) ON CONFLICT(machine_serial) DO UPDATE SET consecutive_fail_count=consecutive_fail_count+1");Add(cmd,"$s",serial);if(ok)Add(cmd,"$n",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
    }
    public MachineState? GetMachineState(string serial){using var cmd=Command("SELECT machine_serial,last_fetch_ok_at,consecutive_fail_count FROM machine_state WHERE machine_serial=$s");Add(cmd,"$s",serial);using var r=cmd.ExecuteReader();return r.Read()?new(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.GetInt32(2)):null;}
    private IReadOnlyList<StoredLog> QueryLogs(string sql,string serial){using var cmd=Command(sql);Add(cmd,"$s",serial);return ReadLogs(cmd);}
    private static IReadOnlyList<StoredLog> ReadLogs(SqliteCommand cmd){var rows=new List<StoredLog>();using var r=cmd.ExecuteReader();while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2)));return rows;}
    private SqliteCommand Command(string text){var cmd=Connection.CreateCommand();cmd.CommandText=text;return cmd;}
    private static void Add(SqliteCommand cmd,string name,object value)=>cmd.Parameters.AddWithValue(name,value);
    public void Dispose()=>Connection.Dispose();
}
