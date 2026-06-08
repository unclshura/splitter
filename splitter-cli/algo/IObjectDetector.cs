namespace splitter.algo;

public interface IObjectDetector : IDisposable
{
    List<DetectedPerson> DetectAll(SingleTask job, Mat frameCont);
}