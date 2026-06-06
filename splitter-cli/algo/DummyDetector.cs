namespace splitter.algo;

public class DummyDetector : IObjectDetector
{
    public List<(Rect box, Point2f center)> DetectAll(Mat frameCont) => [];
    public void Dispose() {}
}
