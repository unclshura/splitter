namespace Splitter_UI.Services;

internal class GlobalLogger(ILogService _logService, StatusBarViewModel _statusBar, ProgressViewModel _progress) : ILogger
{
    public void ClearProgress(string name, int progressLine) 
    {
        if (progressLine == 0)
            _statusBar.Percent = 0;
        else
            _progress.ClearProgress(name, progressLine-1);
    }
    public void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed) 
    {
        if (progressLine == 0)
            _statusBar.Percent = progress;
        else
            _progress.DrawProgress(name, progressLine - 1, progress, eta, speed);
    }

    public void Log(string prefix, ConsoleColor color, string msg)
    {
        _logService.Log(prefix, color, msg);
    }
}
