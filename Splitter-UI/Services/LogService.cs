namespace Splitter_UI.Services;

public sealed class LogService : ILogService
{
    public event Action<string>? MessageLogged;

    public void Write(string message)
    {
        MessageLogged?.Invoke(message);
    }
}
