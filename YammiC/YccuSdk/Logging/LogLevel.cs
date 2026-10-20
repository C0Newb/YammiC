namespace CNCO.YccuSdk.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public static class LogLevelExtensions
{
    public static string Prefix(this LogLevel level) => level switch
    {
        LogLevel.Debug => "D",
        LogLevel.Info => "I",
        LogLevel.Warn => "W",
        _ => "E",
    };
}
