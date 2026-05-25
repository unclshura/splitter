namespace splitter.tui;

public abstract class LoggingBase(ILogger logger, int _progressLine)
{
#pragma warning disable IDE1006 // Naming Styles
    protected ILogger _logger = logger;
#pragma warning restore IDE1006 // Naming Styles

    protected void Log(string level, ConsoleColor color, string message)
        => _logger.Log(level, color, message);

    protected void LogInfo(string message)
        => _logger.LogInfo(message);

    protected void LogWarn(string message)
        => _logger.LogWarn(message);

    protected void LogError(string message)
        => _logger.LogError(message);

    protected void DrawProgress(string name, double percent, TimeSpan eta, double fps)
        => _logger.DrawProgress(name, _progressLine, percent, eta, fps);

    protected void ClearProgress(string name)
        => _logger.ClearProgress(name,_progressLine);
}
