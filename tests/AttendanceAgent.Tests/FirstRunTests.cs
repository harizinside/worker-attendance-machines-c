using AttendanceAgent;

namespace AttendanceAgent.Tests;

public sealed class FirstRunTests
{
    [Fact] public void LegacyParserSkipsIncompleteMachines(){var parsed=FirstRun.ParseLegacyConfig("""{"cms_base_url":"https://cms.test","machines":[{"name":"M1","ip":"10.0.0.1","port":4370,"serial_number":"SN1"},{"name":"bad","ip":"10.0.0.2","port":4370}]}""")!;Assert.Equal("https://cms.test",parsed.CmsBaseUrl);Assert.Equal(90,parsed.CapacityWarningPct);Assert.Single(parsed.Machines);}
    [Theory][InlineData("{}")] [InlineData("{\"cms_base_url\":\"  \"}")] public void LegacyParserRejectsMissingCmsUrl(string json)=>Assert.Null(FirstRun.ParseLegacyConfig(json));
}
