using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Splitter_UI.Services;

public sealed class MatToBitmapConverter(IBufferPool _pool) : IMatToBitmapConverter
{
    private readonly object     _sync = new();

    public Bitmap Convert(Mat mat, Bitmap? existing = null)
    {
        if (mat.Empty())
            throw new ArgumentException("Mat is empty.", nameof(mat));

        var w = mat.Width;
        var h = mat.Height;
        var channels = mat.Channels();

        if (channels != 3 && channels != 4)
            throw new NotSupportedException($"Only 3 or 4 channel Mats are supported. Got {channels}.");

        lock (_sync)
        {
            var entry = _pool.Get(w, h);

            var src = mat;
            if (!src.IsContinuous())
                src = src.Clone();

            unsafe
            {
                var srcPtr = (byte*)src.DataPointer;
                var totalBytes = w * h * channels;

                if (channels == 3)
                {
                    fixed (byte* dstBgr = entry.Bgr)
                    {
                        Buffer.MemoryCopy(srcPtr, dstBgr, entry.Bgr.Length, totalBytes);
                    }

                    ConvertBgrToBgra(entry.Bgr, entry.Bgra, w, h);
                }
                else
                {
                    fixed (byte* dstBgra = entry.Bgra)
                    {
                        Buffer.MemoryCopy(srcPtr, dstBgra, entry.Bgra.Length, totalBytes);
                    }
                }
            }

            if (existing is WriteableBitmap wb &&
                wb.PixelSize.Width == w &&
                wb.PixelSize.Height == h)
            {
                UpdateWriteableBitmap(wb, entry.Bgra, w, h);
                return wb;
            }

            return CreateBitmap(entry.Bgra, w, h);
        }
    }

    public Bitmap Convert(byte[] bgr, int width, int height, Bitmap? existing = null)
    {
        var entry = _pool.Get(width, height);
        ConvertBgrToBgra(bgr, entry.Bgra, width, height);

        if (existing is WriteableBitmap wb &&
            wb.PixelSize.Width == width &&
            wb.PixelSize.Height == height)
        {
            UpdateWriteableBitmap(wb, entry.Bgra, width, height);
            return wb;
        }

        return CreateBitmap(entry.Bgra, width, height);
    }


    private static void ConvertBgrToBgra(byte[] bgr, byte[] bgra, int width, int height)
    {
        var si = 0;
        var di = 0;
        var totalPixels = width * height;

        for (var i = 0; i < totalPixels; i++)
        {
            bgra[di + 0] = bgr[si + 0];
            bgra[di + 1] = bgr[si + 1];
            bgra[di + 2] = bgr[si + 2];
            bgra[di + 3] = 255;

            si += 3;
            di += 4;
        }
    }

    private static unsafe void UpdateWriteableBitmap(WriteableBitmap wb, byte[] bgra, int width, int height)
    {
        using var fb = wb.Lock();

        var dstPtr = (byte*)fb.Address;
        var dstStride = fb.RowBytes;
        var srcStride = width * 4;

        fixed (byte* srcPtr = bgra)
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = srcPtr + y * srcStride;
                var dstRow = dstPtr + y * dstStride;

                Buffer.MemoryCopy(srcRow, dstRow, dstStride, srcStride);
            }
        }
    }

    private static unsafe Bitmap CreateBitmap(byte[] bgra, int width, int height)
    {
        var stride = width * 4;

        fixed (byte* p = bgra)
        {
            return new WriteableBitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                (nint)p,
                new PixelSize(width, height),
                new Vector(96, 96),
                stride);
        }
    }
}
