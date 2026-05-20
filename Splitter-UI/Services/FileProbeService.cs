namespace Splitter_UI.Services;

public sealed class FileProbeService : IFileProbeService
{
    public async Task<VideoInfo> ProbeAsync(SingleJob job)
    {
        var res = await Task.Run(() =>ProbeVideo.Probe(job));
        return res;
    }
}
