using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using splitter.probe;

namespace Splitter_UI.Services;

public sealed class ThumbnailService : IThumbnailService
{
    private const int _thumbWidth  = 160;
    private const int _thumbHeight = 90;

    private readonly byte [] _bgrBuffer  = new byte[_thumbWidth * _thumbHeight * 3];
    private readonly byte [] _bgraBuffer = new byte[_thumbWidth * _thumbHeight * 4];

    public async Task<Bitmap?> CreateThumbnailAsync(
        string file,
        VideoInfo probe,
        TimeSpan? skip = null,
        int? width = null,
        int? height = null,
        int? rotateDegree = null)
    {
        width  ??= _thumbWidth;
        height ??= _thumbHeight;
        skip   ??= TimeSpan.Zero;

        // buffer for BGR24 → 3 bytes per pixel

        var canUseStaticBuffers =
            width.Value == _thumbWidth &&
            height.Value == _thumbHeight;

        var bgrBuffer  = canUseStaticBuffers ? _bgrBuffer  : new byte[width.Value * height.Value * 3];
        var bgraBuffer = canUseStaticBuffers ? _bgraBuffer : new byte[width.Value * height.Value * 4];

        // Decode a single frame using ffmpeg → raw BGR24 into _bgrBuffer
        bool ok = await DecodeFrameAsync(bgrBuffer, file, skip.Value, width.Value, height.Value, rotateDegree);
        if (!ok)
            return null;

        // Convert BGR24 → BGRA32
        ConvertBgrToBgra(bgrBuffer, bgraBuffer, width.Value, height.Value);

        // Create Avalonia Bitmap
        return CreateBitmap(bgraBuffer, width.Value, height.Value, rotateDegree == 90 || rotateDegree == 270);
    }

    private static async Task<bool> DecodeFrameAsync(byte [] bgrBuffer, string file, TimeSpan skip, int width, int height, int? rotateDegree)
    {
        var rotationStr = TrackingSplitter.GetRorationArg(rotateDegree);

        // ffmpeg command: decode one frame, resize, output raw BGR24
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

        int needed = bgrBuffer.Length;
        int read = 0;

        using var stdout = p.StandardOutput.BaseStream;

        while (read < needed)
        {
            int r = await stdout.ReadAsync(bgrBuffer, read, needed - read);
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

    private static void ConvertBgrToBgra(byte[] bgr, byte[] bgra, int width, int height)
    {
        int si = 0;
        int di = 0;

        int totalPixels = width * height;

        for (int i = 0; i < totalPixels; i++)
        {
            bgra[di + 0] = bgr[si + 0]; // B
            bgra[di + 1] = bgr[si + 1]; // G
            bgra[di + 2] = bgr[si + 2]; // R
            bgra[di + 3] = 255;         // A

            si += 3;
            di += 4;
        }
    }

    private static unsafe Bitmap CreateBitmap(byte[] bgra, int width, int height, bool isRotated)
    {
        if (isRotated)
        {
            (height, width) = (width, height);
        }

        int stride = width * 4;

        fixed (byte* p = bgra)
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                (nint)p,
                new PixelSize(width, height),
                new Vector(96, 96),
                stride);
        }
    }

}
