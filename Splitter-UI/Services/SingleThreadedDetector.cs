namespace Splitter_UI.Services;

public class SingleThreadedDetector<T>(IObjectDetector _detector) : IObjectDetector
    where T : IObjectDetector
{
    private Lock _lock = new();

    public List<DetectedPerson> DetectAll(SingleTask job, Mat frameCont)
    {
        lock (_lock)
        {
            return _detector.DetectAll(job, frameCont);
        }
    }
    
    public void Dispose()
    {
        if ( _detector is IDisposable d )
            d.Dispose();
    }
}

public class SingleThreadedEmbeddingExtractor<T>(IEmbeddingExtractor _extractor) : IEmbeddingExtractor
    where T : IEmbeddingExtractor
{
    private Lock _lock = new();

    public float[] Extract(Mat frame, OpenCvSharp.Rect box)
    {
        lock (_lock)
        {
            return _extractor.Extract(frame, box);
        }
    }

    public void Dispose()
    {
        if (_extractor is IDisposable d)
            d.Dispose();
    }

}