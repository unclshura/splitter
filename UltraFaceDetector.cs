using System.Runtime.InteropServices;
using NcnnDotNet.Layers;
using OpenCvSharp;
using UltraFaceDotNet;

namespace splitter;

public sealed class UltraFaceDetector: LoggingBase, IDisposable, IObjectDetector
{
    private readonly UltraFace _ultraFace;

    public UltraFaceDetector(
        Action<string/*level*/, ConsoleColor /*color*/, string /*message*/> log,
        Action<double /*percent*/, TimeSpan /*duration*/, double /*fps*/> drawProgress
        ) : base(log, drawProgress)
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var param = new UltraFaceParameter
        {
            BinFilePath    = Path.Combine(basePath, "models", "slim_320.bin"),
            ParamFilePath  = Path.Combine(basePath, "models", "slim_320.param"),
            InputWidth     = 320,
            InputLength    = 240,
            NumThread      = 1,
            ScoreThreshold = 0.7f
        };

        _ultraFace = UltraFace.Create(param);
    }

    public List<(Rect box, Point2f center)> DetectAll(Mat frameCont, int width, int height)
    {
        // Convert to byte[] for UltraFace
        var bytesFull = frameCont.Rows * frameCont.Cols * frameCont.ElemSize();
        var bgr = new byte[bytesFull];
        Marshal.Copy(frameCont.Data, bgr, 0, bytesFull);

        var results = new List<(Rect box, Point2f center)>();

        if (bgr == null || bgr.Length == 0)
            return results;

        unsafe
        {
            fixed (byte* p = bgr)
            {
                using var mat = NcnnDotNet.Mat.FromPixels(
                (IntPtr)p,
                NcnnDotNet.PixelType.Bgr,     // BGR24 input
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
