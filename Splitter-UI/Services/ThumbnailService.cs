using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Splitter_UI.Services;

public sealed class ThumbnailService : IThumbnailService
{
    private readonly int _thumbWidth  = 160;
    private readonly int _thumbHeight = 90;

    // Reusable buffer for BGR24 → 3 bytes per pixel
    private readonly byte[] _bgrBuffer;
    private readonly byte[] _bgraBuffer;

    public ThumbnailService()
    {
        _bgrBuffer = new byte[_thumbWidth * _thumbHeight * 3];
        _bgraBuffer = new byte[_thumbWidth * _thumbHeight * 4];
    }

    public async Task<Bitmap?> CreateThumbnailAsync(string file, VideoInfo probe)
    {
        // Decode a single frame using ffmpeg → raw BGR24 into _bgrBuffer
        bool ok = await DecodeFrameAsync(file);
        if (!ok)
            return null;

        // Convert BGR24 → BGRA32
        ConvertBgrToBgra(_bgrBuffer, _bgraBuffer, _thumbWidth, _thumbHeight);

        // Create Avalonia Bitmap
        return CreateBitmap(_bgraBuffer, _thumbWidth, _thumbHeight);
    }

    private async Task<bool> DecodeFrameAsync(string file)
    {
        // ffmpeg command: decode one frame, resize, output raw BGR24
        var args =
            $"-ss 0 -t 0.1 -i \"{file}\" " +
            "-an -sn " +
            $"-vf \"scale={_thumbWidth}:{_thumbHeight}:force_original_aspect_ratio=decrease," +
            $"pad={_thumbWidth}:{_thumbHeight}:(ow-iw)/2:(oh-ih)/2,format=bgr24\" " +
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

        int needed = _bgrBuffer.Length;
        int read = 0;

        using var stdout = p.StandardOutput.BaseStream;

        while (read < needed)
        {
            int r = await stdout.ReadAsync(_bgrBuffer, read, needed - read);
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

    private static unsafe Bitmap CreateBitmap(byte[] bgra, int width, int height)
    {
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
