using splitter.probe;

namespace Splitter_UI.Services;

public interface IFrameStream : IDisposable
{
    VideoInfo Probe { get; }

    Task<bool> ReadNextFrameAsync(byte[] buffer, CancellationToken cancellationToken);
}
