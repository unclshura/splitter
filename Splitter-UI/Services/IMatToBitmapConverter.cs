using Avalonia.Media.Imaging;

namespace Splitter_UI.Services;

public interface IMatToBitmapConverter
{
    Bitmap Convert(Mat mat, Bitmap? existing = null);
    Bitmap Convert(byte[] bgr, int width, int height, Bitmap? existing = null);
}