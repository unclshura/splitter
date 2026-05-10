using System;
using System.Collections.Generic;
using System.Text;

namespace splitter;

public class LoggingBase(
    Action<string/*level*/, ConsoleColor /*color*/, string /*message*/> log,
    Action<double /*percent*/, TimeSpan /*duration*/, double /*fps*/> drawProgress
        )
{
    protected Action<string/*level*/, ConsoleColor /*color*/, string /*message*/> Log          = log;
    protected Action<double /*percent*/, TimeSpan /*duration*/, double /*fps*/>   DrawProgress = drawProgress;

    protected void LogInfo(string msg)    => Log("[INFO]", ConsoleColor.Cyan, msg);
    protected void LogSuccess(string msg) => Log("[ OK ]", ConsoleColor.Green, msg);
    protected void LogWarn(string msg)    => Log("[WARN]", ConsoleColor.Yellow, msg);
    protected void LogError(string msg)   => Log("[ERR ]", ConsoleColor.Red, msg);

}
