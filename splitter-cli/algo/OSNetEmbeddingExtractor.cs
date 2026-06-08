using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace splitter.algo;

public sealed class OSNetEmbeddingExtractor : IDisposable, IEmbeddingExtractor
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    private const int _batchSize    = 16;
    private const int _inputHeight  = 256;
    private const int _inputWidth   = 128;
    private const int _channels     = 3;

    private readonly float[] _inputBuffer;
    private readonly DenseTensor<float> _inputTensor;
    private readonly List<NamedOnnxValue> _inputs = new(1);

    private readonly float[] _embedding;

    private readonly Mat _resizeMat = new();
    private readonly Mat _rgbMat    = new();

    private readonly float _inv255 = 1f / 255f;

    public OSNetEmbeddingExtractor()
    {
        var opt = new SessionOptions();
        opt.AppendExecutionProvider_DML();

        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "osnet_x0_25_msmt17.onnx");
        _session = new InferenceSession(modelPath, opt);

        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();

        int inputSize = _batchSize * _channels * _inputHeight * _inputWidth;
        _inputBuffer = new float[inputSize];

        _inputTensor = new DenseTensor<float>(
            _inputBuffer,
            new[] { _batchSize, _channels, _inputHeight, _inputWidth }
        );

        _inputs.Add(NamedOnnxValue.CreateFromTensor(_inputName, _inputTensor));

        int outDim = _session.OutputMetadata[_outputName].Dimensions[1];
        _embedding = new float[outDim];
    }

    public float[] Extract(Mat frame, Rect box)
    {
        // Clear all batches
        Array.Clear(_inputBuffer, 0, _inputBuffer.Length);

        // Extract ROI
        var roi = new Mat(frame, box);

        Cv2.Resize(roi, _resizeMat, new Size(_inputWidth, _inputHeight));
        Cv2.CvtColor(_resizeMat, _rgbMat, ColorConversionCodes.BGR2RGB);

        FillBatch0(_rgbMat);

        using var results = _session.Run(_inputs);

        var output = results.First(v => v.Name == _outputName).AsTensor<float>();

        // Read embedding from batch 0
        for (int i = 0; i < _embedding.Length; i++)
            _embedding[i] = output[0, i];

        NormalizeL2(_embedding);

        return _embedding;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FillBatch0(Mat rgb)
    {
        int plane = _inputHeight * _inputWidth;

        unsafe
        {
            for (int y = 0; y < _inputHeight; y++)
            {
                var rowPtr  = (byte*)rgb.Ptr(y).ToPointer();
                var rowSpan = new Span<byte>(rowPtr, _inputWidth * 3);

                int src = 0;

                for (int x = 0; x < _inputWidth; x++)
                {
                    int off = y * _inputWidth + x;

                    _inputBuffer[off] = rowSpan[src + 0] * _inv255; // R
                    _inputBuffer[plane + off] = rowSpan[src + 1] * _inv255; // G
                    _inputBuffer[2 * plane + off] = rowSpan[src + 2] * _inv255; // B

                    src += 3;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void NormalizeL2(float[] v)
    {
        float sum = 0f;
        for (int i = 0; i < v.Length; i++)
            sum += v[i] * v[i];

        float inv = 1f / MathF.Sqrt(sum);

        for (int i = 0; i < v.Length; i++)
            v[i] *= inv;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _resizeMat?.Dispose();
        _rgbMat?.Dispose();
    }
}
