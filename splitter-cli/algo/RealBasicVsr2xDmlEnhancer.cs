using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace splitter.algo;

public sealed unsafe class RealBasicVsr2xDmlEnhancer : IVideoEnhancer
{
    public int ResolutionMultiplier => 2;

    private InferenceSession    _session      = null!;
    private SessionOptions      _options      = null!;

    private int                 _inW;
    private int                 _inH;
    private int                 _window;

    private readonly Queue<Mat> _frames       = new Queue<Mat>(32);

    private float[]             _inputBuffer  = null!;
    private float[]             _outputBuffer = null!;

    private DenseTensor<float>  _inputTensor  = null!;
    private DenseTensor<float>  _outputTensor = null!;

    private Mat                 _outputMat    = null!;

    private readonly List<NamedOnnxValue> _inputList = new List<NamedOnnxValue>(1);

    public Task InitializeAsync(int width, int height, int window, CancellationToken token)
    {
        _inW = width;
        _inH = height;
        _window = window;

        var basePath  = AppDomain.CurrentDomain.BaseDirectory;
        var modelPath = System.IO.Path.Combine(basePath, "models", "realbasicvsr_x2.onnx");

        _options = new SessionOptions();
        _options.AppendExecutionProvider_DML();

        _session = new InferenceSession(modelPath, _options);

        int inputSize  = window * 3 * width * height;
        int outW       = width * 2;
        int outH       = height * 2;
        int outputSize = 3 * outW * outH;

        _inputBuffer = new float[inputSize];
        _outputBuffer = new float[outputSize];

        _inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, window, 3, height, width });
        _outputTensor = new DenseTensor<float>(_outputBuffer, new[] { 1, 3, outH, outW });

        _outputMat = new Mat(outH, outW, MatType.CV_8UC3);

        return Task.CompletedTask;
    }

    public unsafe bool TryProcessFrame(Mat input, out Mat output, CancellationToken token)
    {
        output = null!;

        if (token.IsCancellationRequested)
            return false;

        if (_frames.Count == _window)
        {
            var old = _frames.Dequeue();
            old.Dispose();
        }

        _frames.Enqueue(input.Clone());

        if (_frames.Count < _window)
            return false;

        int T = _window;
        int H = _inH;
        int W = _inW;

        // ------------------------------------------------------------
        // INPUT: CV_8UC3 BGR -> normalized RGB, channels-first [1,T,3,H,W]
        // ------------------------------------------------------------

        int t = 0;

        foreach (var f in _frames)
        {
            byte* src = (byte*)f.Data;
            int stride = (int)f.Step();

            for (int y = 0; y < H; y++)
            {
                byte* row = src + y * stride;

                for (int x = 0; x < W; x++)
                {
                    int p = x * 3;

                    byte b = row[p + 0];
                    byte g = row[p + 1];
                    byte r = row[p + 2];

                    float rN = r * (1.0f / 255.0f);
                    float gN = g * (1.0f / 255.0f);
                    float bN = b * (1.0f / 255.0f);

                    int idxR = ((((0 * T) + t) * 3 + 0) * H + y) * W + x;
                    int idxG = ((((0 * T) + t) * 3 + 1) * H + y) * W + x;
                    int idxB = ((((0 * T) + t) * 3 + 2) * H + y) * W + x;

                    _inputBuffer[idxR] = rN;
                    _inputBuffer[idxG] = gN;
                    _inputBuffer[idxB] = bN;
                }
            }

            t++;
        }

        _inputList.Clear();
        _inputList.Add(NamedOnnxValue.CreateFromTensor("input", _inputTensor));

        using var results = _session.Run(_inputList);

        var outTensor = results[0].AsTensor<float>();
        var dims = outTensor.Dimensions; // [1, T, 3, H2, W2]

        int outT = dims[1];
        int outH = dims[3];
        int outW = dims[4];

        int last = outT - 1;

        // ------------------------------------------------------------
        // STEP 1: Bicubic upscale input to x2
        // ------------------------------------------------------------

        using var upBgr = new Mat();
        Cv2.Resize(input, upBgr, new Size(outW, outH), 0, 0, InterpolationFlags.Cubic);

        using var upRgb = new Mat();
        Cv2.CvtColor(upBgr, upRgb, ColorConversionCodes.BGR2RGB);

        using var baseFloat = new Mat();
        upRgb.ConvertTo(baseFloat, MatType.CV_32FC3, 1.0 / 255.0);

        // ------------------------------------------------------------
        // STEP 2: Add residual from model output
        // ------------------------------------------------------------

        unsafe
        {
            float* basePtr = (float*)baseFloat.Data;
            int baseStride = (int)(baseFloat.Step() / sizeof(float));

            for (int y = 0; y < outH; y++)
            {
                float* row = basePtr + y * baseStride;

                for (int x = 0; x < outW; x++)
                {
                    int p = x * 3;

                    float rBase = row[p + 0];
                    float gBase = row[p + 1];
                    float bBase = row[p + 2];

                    float rRes = outTensor[0, last, 0, y, x];
                    float gRes = outTensor[0, last, 1, y, x];
                    float bRes = outTensor[0, last, 2, y, x];

                    float r = Math.Clamp(rBase + rRes, 0f, 1f);
                    float g = Math.Clamp(gBase + gRes, 0f, 1f);
                    float b = Math.Clamp(bBase + bRes, 0f, 1f);

                    row[p + 0] = r;
                    row[p + 1] = g;
                    row[p + 2] = b;
                }
            }
        }

        // ------------------------------------------------------------
        // STEP 3: Convert back to BGR 8-bit for FFmpeg
        // ------------------------------------------------------------

        using var outRgb8 = new Mat();
        baseFloat.ConvertTo(outRgb8, MatType.CV_8UC3, 255.0);

        Cv2.CvtColor(outRgb8, _outputMat, ColorConversionCodes.RGB2BGR);

        output = _outputMat;
        return true;
    }

    public int Flush(Span<Mat> outputFrames, CancellationToken token)
    {
        return 0;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var f in _frames)
            f.Dispose();

        _frames.Clear();

        _session?.Dispose();
        _options?.Dispose();
        _outputMat?.Dispose();

        return ValueTask.CompletedTask;
    }
}
