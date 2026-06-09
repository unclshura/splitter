namespace splitter.algo;

public interface IObjectTracker
{
    (List<DetectedPerson> objects, DetectedPerson? primary) SelectTrackedObject(SingleTask job, Mat frameMat, Point2f? lastMeasurement);
}