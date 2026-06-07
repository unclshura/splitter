namespace splitter.algo;

public sealed class DummyDetector : IObjectDetector
{
    public List<(Rect box, Point2f center)> DetectAll(SingleTask job, Mat frameCont)
    {
        var h   = job.Info.Height;
        var w   = job.Info.Width;

        var c = job.Job.GravitateTo ?? new Point2f(0.5f, 0.5f);
        var x = (int)(c.X * w);
        var y = (int)(c.Y * h);

        var center = new Point2f(x, y);
        var rect = new Rect(x - 1, y - 1, 2, 2);

        return [(rect, center)];
    }

    public void Dispose() {}
}
