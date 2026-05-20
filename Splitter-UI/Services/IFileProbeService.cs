using System.Threading.Tasks;

namespace Splitter_UI.Services;

public interface IFileProbeService
{
    Task<VideoInfo> ProbeAsync(SingleJob job);
}
