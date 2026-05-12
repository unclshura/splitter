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
                Console.SetCursorPosition(0, _logLines);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{prefix} ");
                
                Console.ForegroundColor = color;
                Console.WriteLine(msg);

                Console.ResetColor();
                
                _logLines++;
            }
        }
    }

    public static void LogInfo(string msg) => Log("[INFO]", ConsoleColor.Cyan, msg);
    public static void LogSuccess(string msg) => Log("[ OK ]", ConsoleColor.Green, msg);
    public static void LogWarn(string msg) => Log("[WARN]", ConsoleColor.Yellow, msg);
    public static void LogError(string msg) => Log("[ERR ]", ConsoleColor.Red, msg);

    private static readonly Dictionary<int, int> _progressTrack = new();

    public static void DrawProgress(string name, int progressLevel, double progress, TimeSpan eta, double speed)
    {
        if (PlainText || progressLevel < 0)
            return;

        // Crop name to max 20 chars
        name = name.Length > 20 ? name[..20] : name;

        lock (_consoleLock)
        {
            var width = Math.Max(20, Console.WindowWidth - 20);

            // Reserve space for name + space
            var namePrefix = name + " ";
            var barWidth = Math.Max(10, width - namePrefix.Length);

            var filled = (int)(progress * barWidth);
            if (filled < 0) filled = 0;
            if (filled > barWidth) filled = barWidth;

            // --- NEW: skip drawing if visually unchanged ---
            if (_progressTrack.TryGetValue(progressLevel, out var lastFilled) &&
                lastFilled == filled)
            {
                return; // no visual change → skip
            }

            _progressTrack[progressLevel] = filled;
            // ------------------------------------------------

            var barLine  = _logLines + 1 + progressLevel * 2;
            var infoLine = _logLines + 2 + progressLevel * 2;

            // Draw progress bar
            Console.SetCursorPosition(0, barLine);
            Console.Write("\u001b[38;2;0;255;0m"); // green
            Console.Write(namePrefix);
            Console.Write("[");
            Console.Write(new string('#', filled));
            Console.Write(new string('-', barWidth - filled));
            Console.Write("]\u001b[0m");

            // Info line
            Console.SetCursorPosition(0, infoLine);

            var etaStr = eta.TotalSeconds < 0 || double.IsInfinity(eta.TotalSeconds)
            ? "ETA: --:--"
            : $"ETA: {eta:mm\\:ss}";

            var speedStr = double.IsNaN(speed) || double.IsInfinity(speed)
            ? "Speed: -.-x"
            : $"Speed: {speed:F2}x";

            var info = $"{progress * 100:0.0}%  {etaStr}  {speedStr}   ";

            Console.Write("\u001b[38;2;180;180;180m" +
                          info.PadRight(Console.WindowWidth - 1) +
                          "\u001b[0m");
        }
    }


    public static void ClearProgress(int progressLevel)
    {
        if (PlainText || progressLevel < 0)
            return;

        lock (_consoleLock)
        {
            var barLine  = _logLines + 1 + progressLevel * 2;
            var infoLine = _logLines + 2 + progressLevel * 2;

            // Clear bar line
            Console.SetCursorPosition(0, barLine);
            Console.Write(new string(' ', Console.WindowWidth - 1));

            // Clear info line
            Console.SetCursorPosition(0, infoLine);
            Console.Write(new string(' ', Console.WindowWidth - 1));
        }
    }

}
