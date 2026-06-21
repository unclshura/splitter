using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace Splitter_UI.Services;

public sealed class ThumbnailService : IThumbnailService
{
    public const int ThumbWidth  = 160;
    public const int ThumbHeight = 90;

    private readonly IMatToBitmapConverter _converter;
    private readonly IBufferPool _pool;

    private SemaphoreSlim _lock = new(1,1);

    public ThumbnailService(
        IMatToBitmapConverter converter,
        IBufferPool pool)
    {
        _converter = converter;
        _pool = pool;
    }

    public async Task<Bitmap?> CreateThumbnailAsync(
        string file,
        VideoInfo probe,
        TimeSpan? skip = null,
        int? width = null,
        int? height = null,
        int? rotateDegree = null)
    {
        await _lock.WaitAsync();
        try
        {
            return await CreateThumbnailInternal(
                file,
                probe,
                skip,
                width,
                height,
                rotateDegree
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Bitmap?> CreateThumbnailInternal(
        string file,
        VideoInfo probe,
        TimeSpan? skip = null,
        int? width = null,
        int? height = null,
        int? rotateDegree = null)
    {
        width ??= ThumbWidth;
        height ??= ThumbHeight;
        skip ??= TimeSpan.Zero;

        var entry = _pool.Get(width.Value, height.Value);

        var ok = await DecodeFrameAsync(
            entry.Bgr,
            file,
            skip.Value,
            width.Value,
            height.Value,
            rotateDegree
        );

        if (!ok)
            return null;

        return _converter.Convert(entry.Bgr, width.Value, height.Value);
    }

    private static async Task<bool> DecodeFrameAsync(
        byte[] bgrBuffer,
        string file,
        TimeSpan skip,
        int width,
        int height,
        int? rotateDegree)
    {
        var rotationStr = TrackingSplitter.GetRorationArg(rotateDegree);

        var args =
            $"-ss {skip.TotalSeconds} -t 0.1 -i \"{file}\" " +
            "-an -sn " +
            $"-vf \"scale={width}:{height}:force_original_aspect_ratio=decrease," +
            $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=bgr24{rotationStr}\" " +
            "-f rawvideo -";

        var psi = new ProcessStartInfo
        {
            FileName               = "ffmpeg",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var p = new Process { StartInfo = psi };
        p.Start();

        var needed = bgrBuffer.Length;
        var read = 0;

        using var stdout = p.StandardOutput.BaseStream;

        while (read < needed)
        {
            var r = await stdout.ReadAsync(bgrBuffer, read, needed - read);
            if (r == 0)
            {
                TryKill(p);
                return false;
            }
            read += r;
        }

        TryKill(p);
        return true;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(); } catch { }
    }
}
