namespace splitter.algo;

public interface IObjectDetector : IDisposable
{
    List<(Rect box, Point2f center)> DetectAll(SingleTask job, Mat frameCont);
}