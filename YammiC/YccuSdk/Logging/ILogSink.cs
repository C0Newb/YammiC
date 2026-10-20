namespace CNCO.YccuSdk.Logging;

public interface ILogSink
{
    void Write(LogLevel level, string tag, string message);
}
