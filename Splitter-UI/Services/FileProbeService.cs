using splitter.probe;

namespace Splitter_UI.Services;

public sealed class FileProbeService : IFileProbeService
{
    public async Task<VideoInfo> ProbeAsync(string inputFile)
    {
        var res = await Task.Run(() => ProbeVideo.Probe(inputFile, false));
        return res;
    }
}
