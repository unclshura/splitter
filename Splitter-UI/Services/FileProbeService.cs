namespace Splitter_UI.Services;

public sealed class FileProbeService : IFileProbeService
{
    public async Task<VideoInfo> ProbeAsync(string inputFile, CancellationToken token)
    {
        var res = await Task.Run(() => ProbeVideo.Probe(inputFile, false, token), token);
        return res;
    }
}
