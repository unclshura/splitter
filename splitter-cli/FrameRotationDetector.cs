using OpenCvSharp;

namespace splitter;

public sealed class FrameRotationDetector
{
    private readonly Mat _gray;
    private readonly Mat _gx;
    private readonly Mat _gy;
    private readonly Mat _mag;
    private readonly Mat _angle;

    private readonly float[] _hist;

    private readonly int _w;
    private readonly int _h;
    private readonly int _bins;

    public FrameRotationDetector(int width = 320, int height = 180, int bins = 36)
    {
        _w = width;
        _h = height;
        _bins = bins;

        _gray = new Mat(height, width, MatType.CV_8UC1);
        _gx = new Mat(height, width, MatType.CV_32F);
        _gy = new Mat(height, width, MatType.CV_32F);
        _mag = new Mat(height, width, MatType.CV_32F);
        _angle = new Mat(height, width, MatType.CV_32F);

        _hist = new float[bins];   // allocated once
    }

    public int GetRotation(Mat frame)
    {
        // 1. Grayscale
        Cv2.CvtColor(frame, _gray, ColorConversionCodes.BGR2GRAY);

        // 2. Sobel
        Cv2.Sobel(_gray, _gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(_gray, _gy, MatType.CV_32F, 0, 1, 3);

        // 3. Magnitude + angle
        Cv2.CartToPolar(_gx, _gy, _mag, _angle, angleInDegrees: true);

        // 4. Clear histogram
        for (int i = 0; i < _bins; i++)
            _hist[i] = 0;

        float binSize = 180f / _bins;

        unsafe
        {
            float* anglePtr = (float*)_angle.Data;
            float* magPtr   = (float*)_mag.Data;

            int total = _w * _h;

            for (int i = 0; i < total; i++)
            {
                float m = magPtr[i];
                if (m < 5f) continue; // ignore weak gradients

                float a = anglePtr[i];
                if (a < 0) a += 360f;
                a = a % 180f;

                int bin = (int)(a / binSize);
                if (bin < 0) bin = 0;
                if (bin >= _bins) bin = _bins - 1;

                _hist[bin] += m;
            }
        }

        // 5. Energy around 0° vs 90°
        float e0 = 0, e90 = 0;
        int window = 3;

        int bin0  = 0;
        int bin90 = _bins / 2;

        for (int i = -window; i <= window; i++)
        {
            e0 += _hist[Wrap(bin0 + i)];
            e90 += _hist[Wrap(bin90 + i)];
        }

        // 6. Decide upright vs sideways
        if (e90 > e0 * 1.6f)
            return 90;   // sideways

        return 0;         // upright (concert default)
    }

    private int Wrap(int b)
    {
        if (b < 0) return b + _bins;
        if (b >= _bins) return b - _bins;
        return b;
    }
}
