using OpenCvSharp;

namespace splitter;

public interface IObjectDetector : IDisposable
{
    List<(Rect box, Point2f center)> DetectAll(Mat frameCont, int width, int height);
}