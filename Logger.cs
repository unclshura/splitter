namespace splitter;

public static class Logger
{
    static int             _logLines        = 0;
    static readonly object _consoleLock     = new();

    public static bool PlainText { get; set; }

    public static void Log(string prefix, ConsoleColor color, string msg)
    {
        lock (_consoleLock)
        {
            if (PlainText)
            {
                Console.WriteLine($"{prefix} {msg}");
            }
            else
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"{prefix} {msg}");
                Console.ResetColor();
                _logLines++;
            }
        }
    }

    public static void LogInfo(string msg) => Log("[INFO]", ConsoleColor.Cyan, msg);
    public static void LogSuccess(string msg) => Log("[ OK ]", ConsoleColor.Green, msg);
    public static void LogWarn(string msg) => Log("[WARN]", ConsoleColor.Yellow, msg);
    public static void LogError(string msg) => Log("[ERR ]", ConsoleColor.Red, msg);

    public static void DrawProgress(int progressLevel, double progress, TimeSpan eta, double speed)
    {
        if (PlainText || progressLevel < 0)
            return;

        lock (_consoleLock)
        {
            var width = Math.Max(20, Console.WindowWidth - 20);
            var filled = (int)(progress * width);
            if (filled < 0) filled = 0;
            if (filled > width) filled = width;

            var barLine  = _logLines + 1 + progressLevel*2;
            var infoLine = _logLines + 2 + progressLevel*2;

            // Progress bar with 24-bit color (green)
            Console.SetCursorPosition(0, barLine);
            Console.Write("\u001b[38;2;0;255;0m[");
            Console.Write(new string('#', filled));
            Console.Write(new string('-', width - filled));
            Console.Write("]\u001b[0m");

            // Info line: percentage, ETA, speed
            Console.SetCursorPosition(0, infoLine);
            var etaStr = eta.TotalSeconds < 0 || double.IsInfinity(eta.TotalSeconds)
                ? "ETA: --:--"
                : $"ETA: {eta:mm\\:ss}";
            var speedStr = double.IsNaN(speed) || double.IsInfinity(speed)
                ? "Speed: -.-x"
                : $"Speed: {speed:F2}x";

            var info = $"{progress * 100:0.0}%  {etaStr}  {speedStr}   ";
            Console.Write("\u001b[38;2;180;180;180m" + info.PadRight(Console.WindowWidth - 1) + "\u001b[0m");
        }
    }
}
