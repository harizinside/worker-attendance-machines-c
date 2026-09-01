using AttendanceAgent;
using AttendanceAgent.Commands;

namespace AttendanceAgent.Tests;

public sealed class LogicTests
{
    [Fact] public void ScanMatchingHandlesRegisteredChangedAndUnknown(){var machines=new[]{new MachineConfig("M1","192.168.1.10",4370,"SN1")};var rows=CommandRunner.MatchScanResults([new("192.168.1.10",4370,"SN1","ZK"),new("192.168.1.20",4370,"SN1","ZK"),new("192.168.1.30",4370,"SN9","ZK")],machines);Assert.StartsWith("Terdaftar",rows[0].Status);Assert.Contains("IP BERUBAH",rows[1].Status);Assert.Equal("Belum terdaftar",rows[2].Status);}
    [Fact] public void LoggingWritesFile(){var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));try{var path=Logging.Setup(dir);Serilog.Log.Error("contoh error windows");Serilog.Log.CloseAndFlush();Assert.Contains("contoh error windows",File.ReadAllText(path));}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}}
}
