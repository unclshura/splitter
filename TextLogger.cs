namespace splitter;

public class TextLogger() : ILogger
{
    readonly object _consoleLock     = new();

    public void Log(string prefix, ConsoleColor color, string msg)
    {
        lock (_consoleLock)
        {
            Console.WriteLine($"{prefix} {msg}");
        }
    }

    public void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed) {}
    public void ClearProgress(int progressLevel){}

}
