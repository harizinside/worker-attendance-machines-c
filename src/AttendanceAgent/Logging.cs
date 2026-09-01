using Serilog;

namespace AttendanceAgent;

public static class Logging
{
    public const string FileName="attendance-agent.log";
    public static string Setup(string? directory=null)
    {
        var dir=directory??DefaultDirectory();Directory.CreateDirectory(dir);var path=Path.Combine(dir,FileName);
        Log.Logger=new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console(outputTemplate:"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}").WriteTo.File(path,rollingInterval:RollingInterval.Infinite,fileSizeLimitBytes:5*1024*1024,rollOnFileSizeLimit:true,retainedFileCountLimit:3,shared:true,outputTemplate:"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}").CreateLogger();return path;
    }
    public static string DefaultDirectory()
    {
        var preferred=AppContext.BaseDirectory;
        try{Directory.CreateDirectory(preferred);var probe=Path.Combine(preferred,".attendance-agent-write-test");File.WriteAllText(probe,"");File.Delete(probe);return preferred;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){var baseDir=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);if(string.IsNullOrWhiteSpace(baseDir))baseDir=Directory.GetCurrentDirectory();return Path.Combine(baseDir,"AttendanceAgent");}
    }
}
