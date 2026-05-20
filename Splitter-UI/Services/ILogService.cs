
namespace Splitter_UI.Services;

public interface ILogService
{
    event Action<string>? MessageLogged;

    void Write(string message);
}
