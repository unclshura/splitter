using System;
using System.Collections.Generic;
using System.Text;

namespace Splitter_UI.Services;

internal class GlobalLogger(ILogService _logService) : ILogger
{
    public void ClearProgress(int progressLevel) { }
    public void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed) { }
    public void Log(string prefix, ConsoleColor color, string msg)
    {
        _logService.Write($"[{prefix}] {msg}");
    }
}
