using System.Diagnostics;
using splitter.probe;

namespace Splitter_UI.Services;

public sealed class FrameStreamService : IFrameStream
{
    private readonly Process _process;
    private readonly Stream _output;
    private readonly int _frameLength;
    private bool _disposed;

    public VideoInfo Probe { get; }

    public FrameStreamService(
        string file,
        VideoInfo probe,
        TimeSpan start,
        int width,
        int height,
        int? rotate)
    {
        Probe = probe;
        _frameLength = checked(width * height * 3);

        var rotation = TrackingSplitter.GetRorationArg(rotate);
        var filter = $"scale={width}:{height}:force_original_aspect_ratio=decrease," +
                     $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=bgr24{rotation}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(start.TotalSeconds.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(file);
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("bgr24");
        startInfo.ArgumentList.Add("pipe:1");

        _process = new Process { StartInfo = startInfo };
        if (!_process.Start())
            throw new InvalidOperationException("Could not start ffmpeg for preview playback.");

        _output = _process.StandardOutput.BaseStream;
        _ = _process.StandardError.ReadToEndAsync();
    }

    public async Task<bool> ReadNextFrameAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length < _frameLength)
            throw new ArgumentException($"Frame buffer must contain at least {_frameLength} bytes.", nameof(buffer));

        var read = 0;
        while (read < _frameLength)
        {
            var count = await _output.ReadAsync(buffer.AsMemory(read, _frameLength - read), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
                return false;
            read += count;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        _output.Dispose();
        _process.Dispose();
    }
}
