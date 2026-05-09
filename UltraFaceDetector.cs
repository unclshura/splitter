using NcnnDotNet;
using UltraFaceDotNet;

namespace splitter;

public sealed class UltraFaceDetector : IDisposable
{
    private readonly UltraFace _ultraFace;

    public UltraFaceDetector(string binPath, string paramPath)
    {
        var param = new UltraFaceParameter
        {
            BinFilePath = binPath,
            ParamFilePath = paramPath,
            InputWidth = 320,
            InputLength = 240,
            NumThread = 1,
            ScoreThreshold = 0.7f
        };

        _ultraFace = UltraFace.Create(param);
    }

    public (Rect box, Point2f center)? Detect(byte[] bgr, int width, int height)
    {
        if (bgr == null || bgr.Length == 0)
            return null;

        // bgr is contiguous BGR24: width * height * 3
        unsafe
        {
            fixed (byte* p = bgr)
            {
                using var mat = Mat.FromPixels(
                    (IntPtr)p,
                    PixelType.Bgr,     // BGR24 input
                    width,
                    height);

                var faces = _ultraFace.Detect(mat);
                if (faces == null)
                    return null;

                FaceInfo best = default;
                bool hasBest = false;

                foreach (var f in faces)
                {
                    if (!hasBest || f.Score > best.Score)
                    {
                        best = f;
                        hasBest = true;
                    }
                }

                if (!hasBest)
                    return null;

                int x1 = (int)best.X1;
                int y1 = (int)best.Y1;
                int x2 = (int)best.X2;
                int y2 = (int)best.Y2;

                var rect = new Rect(
                    x1,
                    y1,
                    x2 - x1,
                    y2 - y1);

                if (rect.Width <= 0 || rect.Height <= 0)
                    return null;

                var center = new Point2f(
                    rect.X + rect.Width / 2f,
                    rect.Y + rect.Height / 2f);

                return (rect, center);
            }
        }
    }

    public List<(Rect box, Point2f center)> DetectAll(byte[] bgr, int width, int height)
    {
        var results = new List<(Rect box, Point2f center)>();

        if (bgr == null || bgr.Length == 0)
            return results;

        unsafe
        {
            fixed (byte* p = bgr)
            {
                using var mat = Mat.FromPixels(
                (IntPtr)p,
                PixelType.Bgr,     // BGR24 input
                width,
                height);

                var faces = _ultraFace.Detect(mat);
                if (faces == null)
                    return results;

                foreach (var f in faces)
                {
                    int x1 = (int)f.X1;
                    int y1 = (int)f.Y1;
                    int x2 = (int)f.X2;
                    int y2 = (int)f.Y2;

                    var rect = new Rect(
                    x1,
                    y1,
                    x2 - x1,
                    y2 - y1);

                    if (rect.Width <= 0 || rect.Height <= 0)
                        continue;

                    var center = new Point2f(
                    rect.X + rect.Width / 2f,
                    rect.Y + rect.Height / 2f);

                    results.Add((rect, center));
                }
            }
        }

        return results;
    }

    public void Dispose() => _ultraFace?.Dispose();
}
