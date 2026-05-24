using splitter.probe;

namespace Splitter_UI.Services;

public interface IFileProbeService
{
    Task<VideoInfo> ProbeAsync(string inputFile);
}
