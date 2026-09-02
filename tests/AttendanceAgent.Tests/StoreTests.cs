using AttendanceAgent;
using Microsoft.Data.Sqlite;

namespace AttendanceAgent.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly Store _store;
    public StoreTests(){var connection=new SqliteConnection("Data Source=:memory:");connection.Open();_store=new Store(connection);_store.Init();}
    [Fact] public void UpsertDeduplicatesAndMarksPushed(){var at=new DateTimeOffset(2025,1,15,8,0,0,TimeSpan.FromHours(7));var logs=new[]{new AttendanceRecord("EMP001",at,0)};Assert.Equal(1,_store.UpsertLogs("SN1",logs));Assert.Equal(0,_store.UpsertLogs("SN1",logs));Assert.Equal(1,_store.UnsyncedCount("SN1"));var row=Assert.Single(_store.UnsyncedLogs("SN1"));_store.MarkPushed("SN1",[(row.FingerId,row.PunchTime)]);Assert.Equal(0,_store.UnsyncedCount("SN1"));}
    [Fact] public void QueryFiltersInclusiveDates(){_store.UpsertLogs("SN1",[new("1",new(2025,1,1,8,0,0,TimeSpan.FromHours(7)),0),new("2",new(2025,2,1,8,0,0,TimeSpan.FromHours(7)),1)]);Assert.Single(_store.QueryForExport("SN1","2025-01-01","2025-01-31"));}
    [Fact] public void FetchStateResetsFailures(){_store.RecordFetchResult("SN1",false);_store.RecordFetchResult("SN1",false);Assert.Equal(2,_store.GetMachineState("SN1")!.ConsecutiveFailCount);_store.RecordFetchResult("SN1",true);var state=_store.GetMachineState("SN1")!;Assert.Equal(0,state.ConsecutiveFailCount);Assert.NotNull(state.LastFetchOkAt);}
    [Fact] public void SettingsRoundTrip(){_store.SetCmsBaseUrl("https://cms.test");_store.SetCapacityWarningPct(80);Assert.Equal(new AppSettings("https://cms.test",80),_store.GetAppSettings());}
    [Fact] public void SettingsDefaultCapacityWhenUnset(){Assert.Equal(90,_store.GetAppSettings().CapacityWarningPct);_store.SetSetting("capacity_warning_pct","invalid");Assert.Equal(90,_store.GetAppSettings().CapacityWarningPct);}
    [Fact] public void MachineCrudRoundTrip(){var machine=new MachineConfig("M1","10.0.0.1",4370,"SN1");_store.UpsertMachine(machine);Assert.Equal(machine,_store.FindMachine("M1"));_store.UpsertMachine(machine with{Ip="10.0.0.2"});Assert.Equal("10.0.0.2",Assert.Single(_store.GetMachines()).Ip);Assert.True(_store.RemoveMachine("M1"));Assert.Null(_store.FindMachine("M1"));}
    public void Dispose()=>_store.Dispose();
}
