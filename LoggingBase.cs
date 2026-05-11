using System;
namespace splitter;

public abstract class LoggingBase(int progressLine)
{
    protected void Log(string level, ConsoleColor color, string message)
        => Logger.Log(level, color, message);

    protected void LogInfo(string message)
        => Logger.LogInfo(message);

    protected void LogWarn(string message)
        => Logger.LogWarn(message);

    protected void LogError(string message)
        => Logger.LogError(message);

    protected void DrawProgress(double percent, TimeSpan eta, double fps)
        => Logger.DrawProgress(progressLine, percent, eta, fps);
}
