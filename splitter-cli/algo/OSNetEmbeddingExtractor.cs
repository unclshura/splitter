using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace splitter.algo;

public sealed class OSNetEmbeddingExtractor : IDisposable, IEmbeddingExtractor
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private const int _batchSize   = 16;
    private const int _inputHeight = 256;
    private const int _inputWidth  = 128;
    private const int _channels    = 3;

    private readonly float[] _inputBuffer;
    private readonly DenseTensor<float> _inputTensor;
    private readonly List<NamedOnnxValue> _inputs = new(1);

    private readonly float[] _embedding;

    private readonly Mat _resizeMat = new();
    private readonly Mat _rgbMat = new();
    private readonly Mat _labMat = new();

    private readonly CLAHE _clahe =
        Cv2.CreateCLAHE(
            clipLimit: 2.0,
            tileGridSize: new Size(8, 8));

    private const float _inv255 = 1f / 255f;

    private const float MeanR = 0.485f;
    private const float MeanG = 0.456f;
    private const float MeanB = 0.406f;

    private const float StdR = 0.229f;
    private const float StdG = 0.224f;
    private const float StdB = 0.225f;

    public OSNetEmbeddingExtractor()
    {
        var opt = new SessionOptions();
        opt.AppendExecutionProvider_DML();

        var modelPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "models",
            "osnet_x0_25_msmt17.onnx");

        _session = new InferenceSession(modelPath, opt);

        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();

        var inputSize =
            _batchSize *
            _channels *
            _inputHeight *
            _inputWidth;

        _inputBuffer = new float[inputSize];

        _inputTensor = new DenseTensor<float>(
            _inputBuffer,
            new[]
            {
                _batchSize,
                _channels,
                _inputHeight,
                _inputWidth
            });

        _inputs.Add(
            NamedOnnxValue.CreateFromTensor(
                _inputName,
                _inputTensor));

        var outDim =
            _session.OutputMetadata[_outputName]
                .Dimensions[1];

        _embedding = new float[outDim];
    }

    public float[] Extract(Mat frame, Rect box)
    {
        Array.Clear(
            _inputBuffer,
            0,
            _inputBuffer.Length);

        Rect expanded =
            ExpandBox(
                box,
                frame.Width,
                frame.Height,
                1.15f);

        using var roi =
            new Mat(frame, expanded);

        Cv2.Resize(
            roi,
            _resizeMat,
            new Size(
                _inputWidth,
                _inputHeight));

        NormalizeLighting(_resizeMat);

        Cv2.CvtColor(
            _resizeMat,
            _rgbMat,
            ColorConversionCodes.BGR2RGB);

        FillBatch0(_rgbMat);

        using var results =
            _session.Run(_inputs);

        var output =
            results.First(
                v => v.Name == _outputName)
                .AsTensor<float>();

        for (var i = 0; i < _embedding.Length; i++)
            _embedding[i] = output[0, i];

        NormalizeL2(_embedding);

        return _embedding;
    }

    private void NormalizeLighting(Mat image)
    {
        Cv2.CvtColor(
            image,
            _labMat,
            ColorConversionCodes.BGR2Lab);

        Mat[] channels = Cv2.Split(_labMat);

        try
        {
            _clahe.Apply(
                channels[0],
                channels[0]);

            Cv2.Merge(
                channels,
                _labMat);

            Cv2.CvtColor(
                _labMat,
                image,
                ColorConversionCodes.Lab2BGR);
        }
        finally
        {
            foreach (var channel in channels)
                channel.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillBatch0(Mat rgb)
    {
        var plane =
            _inputHeight * _inputWidth;

        unsafe
        {
            for (var y = 0; y < _inputHeight; y++)
            {
                var rowPtr =
                    (byte*)rgb.Ptr(y).ToPointer();

                var rowSpan =
                    new Span<byte>(
                        rowPtr,
                        _inputWidth * 3);

                var src = 0;

                for (var x = 0; x < _inputWidth; x++)
                {
                    var off =  y * _inputWidth + x;
                    var r   =  rowSpan[src + 0] * _inv255;
                    var g   =  rowSpan[src + 1] * _inv255;
                    var b   =  rowSpan[src + 2] * _inv255;

                    _inputBuffer[off]             = (r - MeanR) / StdR;
                    _inputBuffer[plane + off]     = (g - MeanG) / StdG;
                    _inputBuffer[2 * plane + off] = (b - MeanB) / StdB;

                    src += 3;
                }
            }
        }
    }

    private static Rect ExpandBox(
        Rect box,
        int frameWidth,
        int frameHeight,
        float scale)
    {
        var cx = box.X + box.Width * 0.5f;
        var cy = box.Y + box.Height * 0.5f;
        var w  = (int)(box.Width * scale);
        var h  = (int)(box.Height * scale);
        var x  = (int)(cx - w * 0.5f);
        var y  = (int)(cy - h * 0.5f);

        x = Math.Max(0, x);
        y = Math.Max(0, y);

        if (x + w > frameWidth)
            w = frameWidth - x;

        if (y + h > frameHeight)
            h = frameHeight - y;

        return new Rect(x, y, w, h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void NormalizeL2(float[] v)
    {
        var sum = 0f;

        for (var i = 0; i < v.Length; i++)
            sum += v[i] * v[i];

        if (sum < 1e-12f)
            return;

        var inv =
            1f / MathF.Sqrt(sum);

        for (var i = 0; i < v.Length; i++)
            v[i] *= inv;
    }

    public void Dispose()
    {
        _session?.Dispose();

        _resizeMat?.Dispose();
        _rgbMat?.Dispose();
        _labMat?.Dispose();

        _clahe?.Dispose();
    }
}