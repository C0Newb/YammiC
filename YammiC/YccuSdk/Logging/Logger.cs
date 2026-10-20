namespace CNCO.YccuSdk.Logging;

public class Logger
{
    private readonly IReadOnlyList<ILogSink> _sinks;

    public Logger(string tag, IReadOnlyList<ILogSink> sinks)
    {
        Tag = tag;
        _sinks = sinks;
    }

    public string Tag { get; }

    public Logger Section(string name) => new Logger($"{Tag}/{name}", _sinks);

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    public void Error(string message, Exception exception) => Write(LogLevel.Error, $"{message}\n{exception}");

    private void Write(LogLevel level, string message)
    {
        foreach (var sink in _sinks)
        {
            sink.Write(level, Tag, message);
        }
    }
}
