namespace splitter.algo;

public interface IObjectTracker
{
    (List<DetectedPerson>, DetectedPerson?) SelectTrackedObject(SingleTask job, Mat frameMat, Point2f? lastMeasurement);
}