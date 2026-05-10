using System;
using System.Collections.Generic;
using System.Linq;
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

    public YoloOnnxObjectDetector(
        Action<string, ConsoleColor, string> log,
        Action<double, TimeSpan, double> drawProgress
    ) : base(log, drawProgress)
    {
        var options = new SessionOptions();
//        options.AppendExecutionProvider_CPU();
        options.AppendExecutionProvider_DML();

        var basePath  = AppDomain.CurrentDomain.BaseDirectory;
        var modelPath = System.IO.Path.Combine(basePath, "models", "yolov8n.onnx");

        _session = new InferenceSession(modelPath, options);

        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();

        foreach (var kv in _session.OutputMetadata)
            LogInfo($"[YoloOnnx] {kv.Key}: {string.Join(",", kv.Value.Dimensions)} {kv.Value.ElementType}");
    }

    public List<(Rect box, Point2f center)> DetectAll(Mat frameCont, int width, int height)
    {
        if (frameCont.Empty())
            return new List<(Rect, Point2f)>();

        using var resized = frameCont.Resize(new Size(_inputWidth, _inputHeight));
        using var rgb     = resized.CvtColor(ColorConversionCodes.BGR2RGB);

        var inputTensor = CreateInputTensor(rgb);

        using var results = _session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
        });

        var output = results.First(r => r.Name == _outputName).AsTensor<float>();

        var detections = ParseYoloV8(
            output,
            frameCont.Width,
            frameCont.Height,
            _scoreThreshold,
            _personClassIndex);

        var final = ApplyNms(detections, _nmsThreshold);

        var list = new List<(Rect, Point2f)>(final.Count);

        foreach (var d in final)
        {
            int x = (int)d.X;
            int y = (int)d.Y;
            int w = (int)d.Width;
            int h = (int)d.Height;

            x = Math.Clamp(x, 0, frameCont.Width - 1);
            y = Math.Clamp(y, 0, frameCont.Height - 1);
            w = Math.Clamp(w, 1, frameCont.Width - x);
            h = Math.Clamp(h, 1, frameCont.Height - y);

            // Ignore detections starting in the lower 1/3 of the frame
            if (y > frameCont.Height * (2f / 3f))
                continue;

            var rect   = new Rect(x, y, w, h);
            var center = new Point2f(x + w / 2f, y + h / 2f);

            list.Add((rect, center));
        }

        return list;
    }

    private static DenseTensor<float> CreateInputTensor(Mat rgb)
    {
        int height = rgb.Rows;
        int width  = rgb.Cols;

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        unsafe
        {
            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)rgb.Ptr(y).ToPointer();

                for (int x = 0; x < width; x++)
                {
                    int idx = x * 3;

                    tensor[0, 0, y, x] = row[idx + 0] / 255f;
                    tensor[0, 1, y, x] = row[idx + 1] / 255f;
                    tensor[0, 2, y, x] = row[idx + 2] / 255f;
                }
            }
        }

        return tensor;
    }

    private sealed class Detection
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public float Score;
    }

    // -----------------------------
    // CORRECT YOLOv8 PARSER
    // -----------------------------
    private static List<Detection> ParseYoloV8(
        Tensor<float> output,
        int originalWidth,
        int originalHeight,
        float scoreThreshold,
        int classIndex)
    {
        // YOLOv8 output: [1, 84, 8400]
        int channels = output.Dimensions[1]; // 84
        int count    = output.Dimensions[2]; // 8400

        float xScale = (float)originalWidth  / 640f;
        float yScale = (float)originalHeight / 640f;

        var detections = new List<Detection>();

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
            {
                X = left,
                Y = top,
                Width = width,
                Height = height,
                Score = classScore
            });
        }

        return detections;
    }

    private static List<Detection> ApplyNms(List<Detection> detections, float nmsThreshold)
    {
        if (detections.Count == 0)
            return detections;

        var ordered = detections.OrderByDescending(d => d.Score).ToList();
        var result  = new List<Detection>();

        while (ordered.Count > 0)
        {
            var best = ordered[0];
            result.Add(best);
            ordered.RemoveAt(0);

            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                if (IoU(best, ordered[i]) >= nmsThreshold)
                    ordered.RemoveAt(i);
            }
        }

        return result;
    }

    private static float IoU(Detection a, Detection b)
    {
        float x1 = MathF.Max(a.X, b.X);
        float y1 = MathF.Max(a.Y, b.Y);
        float x2 = MathF.Min(a.X + a.Width,  b.X + b.Width);
        float y2 = MathF.Min(a.Y + a.Height, b.Y + b.Height);

        float interW = MathF.Max(0, x2 - x1);
        float interH = MathF.Max(0, y2 - y1);
        float interArea = interW * interH;

        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;

        float union = areaA + areaB - interArea;
        if (union <= 0) return 0f;

        return interArea / union;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
