namespace splitter;

public abstract class LoggingBase(ILogger _logger, int _progressLine)
{
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

    protected void ClearProgress()
        => _logger.ClearProgress(_progressLine);
}
