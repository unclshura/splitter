using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Splitter_UI.Services;

public static class AvaloniaBitmapExtensions
{
    public static Mat ToMatContinuous(this Bitmap bmp)
    {
        var w = bmp.PixelSize.Width;
        var h = bmp.PixelSize.Height;
        var stride = w * 4;
        var size = h * stride;

        var buffer = new byte[size];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            bmp.CopyPixels(
                new PixelRect(0, 0, w, h),
                handle.AddrOfPinnedObject(),
                size,
                stride);

            return Mat.FromPixelData(h, w, MatType.CV_8UC4, buffer);
        }
        finally
        {
            handle.Free();
        }
    }

    public static Mat ToMatBgrContinuous(this Bitmap bmp)
    {
        using var bgra = bmp.ToMatContinuous();
        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }
}