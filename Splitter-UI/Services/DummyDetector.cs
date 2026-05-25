namespace Splitter_UI.Services;

internal class DummyDetector : IObjectDetector
{
    public List<(OpenCvSharp.Rect box, Point2f center)> DetectAll(Mat frameCont) => [];
    public void Dispose() {}
}
