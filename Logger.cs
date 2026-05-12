namespace splitter;

public class Logger(CommandLine cmd) : ILogger
{
    int             _logLines        = Math.Max(1, Environment.ProcessorCount / 2) * 2;
    readonly object _consoleLock     = new();

    public void Log(string prefix, ConsoleColor color, string msg)
    {
        lock (_consoleLock)
        {
            if (cmd.PlainText)
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

    private readonly Dictionary<int, int> _progressTrack = new();

    public void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed)
    {
        if (cmd.PlainText || progressLine < 0)
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
            if (_progressTrack.TryGetValue(progressLine, out var lastFilled) &&
                lastFilled == filled)
            {
                return; // no visual change → skip
            }

            _progressTrack[progressLine] = filled;
            // ------------------------------------------------

            var barLine  = _logLines + 1 + progressLine * 2;
            var infoLine = _logLines + 2 + progressLine * 2;

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


    public void ClearProgress(int progressLevel)
    {
        if (cmd.PlainText || progressLevel < 0)
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
