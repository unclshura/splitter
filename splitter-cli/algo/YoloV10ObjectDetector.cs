using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace splitter.algo;

public sealed class YoloV10ObjectDetector : LoggingBase, IObjectDetector, IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private const int   _inputWidth       = 640;
    private const int   _inputHeight      = 640;
    private const float _scoreThreshold   = 0.35f;
    private const float _nmsThreshold     = 0.45f;
    private const int   _personClassIndex = 0;

    private readonly Mat _resizeMat = new();
    private readonly Mat _rgbMat    = new();

    private readonly float[] _inputBuffer;
    private readonly DenseTensor<float> _inputTensor;

    private readonly List<NamedOnnxValue> _inputs = new(1);

    private readonly List<Detection> _detections = new(256);
    private readonly List<Detection> _nmsBuffer  = new(256);

    private readonly List<DetectedPerson> _results = new(64);

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
            X = x;
            Y = y;
            Width = w;
            Height = h;
            Score = score;
        }
    }

    public YoloV10ObjectDetector(ILogger logger) : base(logger, -1)
    {
        var options = new SessionOptions();
        options.AppendExecutionProvider_DML();

        var basePath  = AppDomain.CurrentDomain.BaseDirectory;
        var modelPath = Path.Combine(basePath, "models", "yolov10m.onnx");

        _session = new InferenceSession(modelPath, options);

        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();

        _inputBuffer = new float[1 * 3 * _inputHeight * _inputWidth];
        _inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, 3, _inputHeight, _inputWidth });

        _inputs.Add(NamedOnnxValue.CreateFromTensor(_inputName, _inputTensor));
    }

    public List<DetectedPerson> DetectAll(SingleTask job, Mat frameCont)
    {
        if (frameCont.Empty())
        {
            _results.Clear();
            return _results;
        }

        Cv2.Resize(frameCont, _resizeMat, new Size(_inputWidth, _inputHeight));
        Cv2.CvtColor(_resizeMat, _rgbMat, ColorConversionCodes.BGR2RGB);

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

        ParseYoloV10(
            output,
            frameCont.Width,
            frameCont.Height,
            job.Job.ScoreThreshold,
            _personClassIndex,
            _detections);

        var final = ApplyNms(_detections, _nmsThreshold, _nmsBuffer);

        _results.Clear();
        for (var i = 0; i < final.Count; i++)
        {
            var d = final[i];

            var x = (int)d.X;
            var y = (int)d.Y;
            var w = (int)d.Width;
            var h = (int)d.Height;

            x = Math.Clamp(x, 0, frameCont.Width - 1);
            y = Math.Clamp(y, 0, frameCont.Height - 1);
            w = Math.Clamp(w, 1, frameCont.Width - x);
            h = Math.Clamp(h, 1, frameCont.Height - y);

            var rect   = new Rect(x, y, w, h);
            var center = new Point2f(x + w / 2f, y + h / 2f);

            _results.Add(new DetectedPerson{ Box = rect, Center = center });
        }

        return _results;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillInputTensor(Mat rgb)
    {
        var height = _inputHeight;
        var width  = _inputWidth;

        var planeSize = height * width;

        Span<float> dst = _inputBuffer.AsSpan();

        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var rowPtr  = (byte*)rgb.Ptr(y).ToPointer();
                var rowSpan = new Span<byte>(rowPtr, width * 3);

                var srcIndex = 0;

                for (var x = 0; x < width; x++)
                {
                    var r = rowSpan[srcIndex + 0];
                    var g = rowSpan[srcIndex + 1];
                    var b = rowSpan[srcIndex + 2];

                    var offset = y * width + x;

                    dst[offset] = r * _inv255;
                    dst[planeSize + offset] = g * _inv255;
                    dst[2 * planeSize + offset] = b * _inv255;

                    srcIndex += 3;
                }
            }
        }
    }

    // YOLOv10 parser: [1, 300, 6] => x1, y1, x2, y2, score, class_id
    private static void ParseYoloV10(
        Tensor<float> output,
        int originalWidth,
        int originalHeight,
        float scoreThreshold,
        int classIndex,
        List<Detection> detections)
    {
        detections.Clear();

        // dims: [1, 300, 6]
        var count = output.Dimensions[1];

        var xScale = (float)originalWidth  / 640f;
        var yScale = (float)originalHeight / 640f;

        for (var i = 0; i < count; i++)
        {
            var x1    = output[0, i, 0];
            var y1    = output[0, i, 1];
            var x2    = output[0, i, 2];
            var y2    = output[0, i, 3];
            var score = output[0, i, 4];
            var cls   = (int)output[0, i, 5];

            if (cls != classIndex)
                continue;

            if (score < scoreThreshold)
                continue;

            var left   = x1 * xScale;
            var top    = y1 * yScale;
            var width  = (x2 - x1) * xScale;
            var height = (y2 - y1) * yScale;

            detections.Add(new Detection(left, top, width, height, score));
        }
    }

    private static List<Detection> ApplyNms(
        List<Detection> detections,
        float nmsThreshold,
        List<Detection> nmsBuffer)
    {
        nmsBuffer.Clear();

        if (detections.Count == 0)
            return nmsBuffer;

        detections.Sort(static (a, b) => b.Score.CompareTo(a.Score));

        for (var i = 0; i < detections.Count; i++)
        {
            var candidate = detections[i];
            var keep = true;

            for (var j = 0; j < nmsBuffer.Count; j++)
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
        var x1 = MathF.Max(a.X, b.X);
        var y1 = MathF.Max(a.Y, b.Y);
        var x2 = MathF.Min(a.X + a.Width,  b.X + b.Width);
        var y2 = MathF.Min(a.Y + a.Height, b.Y + b.Height);

        var interW = x2 - x1;
        if (interW <= 0f) return 0f;

        var interH = y2 - y1;
        if (interH <= 0f) return 0f;

        var interArea = interW * interH;

        var areaA = a.Width * a.Height;
        var areaB = b.Width * b.Height;

        var union = areaA + areaB - interArea;
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
