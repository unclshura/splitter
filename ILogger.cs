namespace splitter;

public interface ILogger
{
    void ClearProgress(int progressLevel);
    void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed);
    void Log(string prefix, ConsoleColor color, string msg);
    
    void LogInfo(string msg) => Log("[INFO]", ConsoleColor.Cyan, msg);
    void LogSuccess(string msg) => Log("[ OK ]", ConsoleColor.Green, msg);
    void LogWarn(string msg) => Log("[WARN]", ConsoleColor.Yellow, msg);
    void LogError(string msg) => Log("[ERR ]", ConsoleColor.Red, msg);
}