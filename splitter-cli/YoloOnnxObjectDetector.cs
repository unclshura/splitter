using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace splitter;

public sealed class YoloOnnxObjectDetector : LoggingBase, IObjectDetector, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private const int   _inputWidth       = 640;
    private const int   _inputHeight      = 640;
    private const float _scoreThreshold   = 0.35f;
    private const float _nmsThreshold     = 0.45f;
    private const int   _personClassIndex = 0;

    // Reusable Mats (no per-frame Mat allocations)
    private readonly Mat _resizeMat = new();
    private readonly Mat _rgbMat    = new();

    // Reusable input tensor buffer
    private readonly float[] _inputBuffer;
    private readonly DenseTensor<float> _inputTensor;

    // Reusable ONNX input list
    private readonly List<NamedOnnxValue> _inputs = new(1);

    // Reusable detection buffers
    private readonly List<Detection> _detections = new(256);
    private readonly List<Detection> _nmsBuffer  = new(256);

    // Reusable result list
    private readonly List<(Rect box, Point2f center)> _results = new(64);

    private readonly float _inv255 = 1f / 255f;

    private readonly struct Detection
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;
        public readonly float Score;

        public Detection(float x, float y, float w, float h, float score)
        {
            X      = x;
            Y      = y;
            Width  = w;
            Height = h;
            Score  = score;
        }
    }

    public YoloOnnxObjectDetector(ILogger logger) : base(logger, -1)
    {
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML();

        var basePath  = AppDomain.CurrentDomain.BaseDirectory;
        var modelPath = Path.Combine(basePath, "models", "yolov8s.onnx");

        _session = new InferenceSession(modelPath, options);

        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();

        //foreach (var kv in _session.OutputMetadata)
        //    LogInfo($"[YoloOnnx] {kv.Key}: {string.Join(",", kv.Value.Dimensions)} {kv.Value.ElementType}");

        // Preallocate tensor buffer (fixed size for lifetime)
        _inputBuffer = new float[1 * 3 * _inputHeight * _inputWidth];
        _inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, 3, _inputHeight, _inputWidth });

        // Pre-create NamedOnnxValue and reuse
        _inputs.Add(NamedOnnxValue.CreateFromTensor(_inputName, _inputTensor));
    }

    public List<(Rect box, Point2f center)> DetectAll(Mat frameCont)
    {
        if (frameCont.Empty())
        {
            _results.Clear();
            return _results;
        }

        // Reuse Mats: no new Mat per frame
        Cv2.Resize(frameCont, _resizeMat, new Size(_inputWidth, _inputHeight));
        Cv2.CvtColor(_resizeMat, _rgbMat, ColorConversionCodes.BGR2RGB);

        // Fill preallocated tensor buffer
        FillInputTensor(_rgbMat);

        using var results = _session.Run(_inputs);

        Tensor<float>? output = null;
        foreach (var r in results)
        {
            if (r.Name == _outputName)
            {
                output = r.AsTensor<float>();
                break;
            }
        }

        if (output is null)
        {
            _results.Clear();
            return _results;
        }

        // Parse detections into reusable list
        ParseYoloV8(
            output,
            frameCont.Width,
            frameCont.Height,
            _scoreThreshold,
            _personClassIndex,
            _detections);

        // Apply NMS into reusable buffer
        var final = ApplyNms(_detections, _nmsThreshold, _nmsBuffer);

        // Build reusable result list
        _results.Clear();
        for (int i = 0; i < final.Count; i++)
        {
            var d = final[i];

            int x = (int)d.X;
            int y = (int)d.Y;
            int w = (int)d.Width;
            int h = (int)d.Height;

            x = Math.Clamp(x, 0, frameCont.Width - 1);
            y = Math.Clamp(y, 0, frameCont.Height - 1);
            w = Math.Clamp(w, 1, frameCont.Width - x);
            h = Math.Clamp(h, 1, frameCont.Height - y);

            // Ignore detections starting in the lower 1/2 of the frame
            if (y > frameCont.Height * 0.5f)
                continue;

            var rect   = new Rect(x, y, w, h);
            var center = new Point2f(x + w / 2f, y + h / 2f);

            _results.Add((rect, center));
        }

        return _results;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillInputTensor(Mat rgb)
    {
        int height = _inputHeight;
        int width  = _inputWidth;

        // NCHW: [1, 3, H, W]
        int planeSize = height * width;

        Span<float> dst = _inputBuffer.AsSpan();

        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                byte* rowPtr = (byte*)rgb.Ptr(y).ToPointer();
                var rowSpan = new Span<byte>(rowPtr, width * 3);

                int srcIndex = 0;

                for (int x = 0; x < width; x++)
                {
                    byte r = rowSpan[srcIndex + 0];
                    byte g = rowSpan[srcIndex + 1];
                    byte b = rowSpan[srcIndex + 2];

                    int offset = y * width + x;

                    // channel 0: R
                    dst[offset] = r * _inv255;
                    // channel 1: G
                    dst[planeSize + offset] = g * _inv255;
                    // channel 2: B
                    dst[2 * planeSize + offset] = b * _inv255;

                    srcIndex += 3;
                }
            }
        }
    }

    // YOLOv8 parser: writes into reusable detections list
    private static void ParseYoloV8(
        Tensor<float> output,
        int originalWidth,
        int originalHeight,
        float scoreThreshold,
        int classIndex,
        List<Detection> detections)
    {
        detections.Clear();

        // YOLOv8 output: [1, 84, 8400]
        int channels = output.Dimensions[1]; // 84
        int count    = output.Dimensions[2]; // 8400

        float xScale = (float)originalWidth  / 640f;
        float yScale = (float)originalHeight / 640f;

        for (int i = 0; i < count; i++)
        {
            float x = output[0, 0, i];
            float y = output[0, 1, i];
            float w = output[0, 2, i];
            float h = output[0, 3, i];

            float classScore = output[0, 4 + classIndex, i];
            if (classScore < scoreThreshold)
                continue;

            float left   = (x - w / 2f) * xScale;
            float top    = (y - h / 2f) * yScale;
            float width  = w * xScale;
            float height = h * yScale;

            detections.Add(new Detection
            (
                x: left,
                y: top,
                w: width,
                h: height,
                score: classScore
            ));
        }
    }

    // In-place NMS using reusable buffers, no LINQ
    private static List<Detection> ApplyNms(
        List<Detection> detections,
        float nmsThreshold,
        List<Detection> nmsBuffer)
    {
        nmsBuffer.Clear();

        if (detections.Count == 0)
            return nmsBuffer;

        // Sort in-place by score descending
        detections.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        for (int i = 0; i < detections.Count; i++)
        {
            var candidate = detections[i];
            bool keep = true;

            for (int j = 0; j < nmsBuffer.Count; j++)
            {
                if (IoU(candidate, nmsBuffer[j]) >= nmsThreshold)
                {
                    keep = false;
                    break;
                }
            }

            if (keep)
                nmsBuffer.Add(candidate);
        }

        return nmsBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float IoU(in Detection a, in Detection b)
    {
        float x1 = MathF.Max(a.X, b.X);
        float y1 = MathF.Max(a.Y, b.Y);
        float x2 = MathF.Min(a.X + a.Width,  b.X + b.Width);
        float y2 = MathF.Min(a.Y + a.Height, b.Y + b.Height);

        float interW = x2 - x1;
        if (interW <= 0f) return 0f;

        float interH = y2 - y1;
        if (interH <= 0f) return 0f;

        float interArea = interW * interH;

        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;

        float union = areaA + areaB - interArea;
        if (union <= 0f) return 0f;

        return interArea / union;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _resizeMat?.Dispose();
        _rgbMat?.Dispose();
    }
}
