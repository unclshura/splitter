using OpenCvSharp;

namespace Splitter_UI.Services;

public class SingleThreadedDetector<T>(IObjectDetector _detector) : IObjectDetector
    where T : IObjectDetector
{
    private Lock _lock = new();

    public List<(Rect box, splitter.Point2f center)> DetectAll(Mat frameCont)
    {
        lock (_lock)
        {
            return _detector.DetectAll(frameCont);
        }
    }
    
    public void Dispose()
    {
        if ( _detector is IDisposable d )
            d.Dispose();
    }
}
