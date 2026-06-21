using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace splitter;

public sealed class TrackingSplitter : LoggingBase, ISegmentProcessor
{
    private readonly IObjectTracker _tracker;

    // ------------------------------------------------------------
    // Internal state (never exposed)
    // ------------------------------------------------------------

    private sealed class FrameProcessingState : IFrameProcessingState
    {
        public SingleTask Job           { get; }
        public KalmanTracker Kalman     { get; }
        public CameraController Camera  { get; }

        public Mat FrameMat             { get; }
        public Mat OutMat               { get; }
        public byte[] InBuffer          { get; }
        public byte[] OutBuffer         { get; }

        public IVideoEnhancer? Enhancer { get; }

        public int InBytes              { get; }
        public int OutBytes             { get; }

        public Process? DecodeProcess   { get; set; }
        public Stream? DecodeStdout     { get; set; }

        public FrameProcessingState(
            SingleTask job,
            KalmanTracker kalman,
            CameraController camera,
            Mat frameMat,
            Mat outMat,
            byte[] inBuffer,
            byte[] outBuffer,
            IVideoEnhancer? enhancer,
            int inBytes,
            int outBytes)
        {
            Job       = job;
            Kalman    = kalman;
            Camera    = camera;
            FrameMat  = frameMat;
            OutMat    = outMat;
            InBuffer  = inBuffer;
            OutBuffer = outBuffer;
            Enhancer  = enhancer;
            InBytes   = inBytes;
            OutBytes  = outBytes;
        }
    }

    public TrackingSplitter(
        int progressLine,
        IObjectTracker tracker,
        SingleJob cmd,
        ILogger logger)
        : base(logger, progressLine)
    {
        _tracker = tracker;
    }

    // ============================================================
    // PUBLIC PREVIEW API
    // ============================================================

    // ------------------------------------------------------------
    // InitSegment
    // ------------------------------------------------------------

    public IFrameProcessingState InitSegment(SingleTask job, CancellationToken token)
    {
        var state = (FrameProcessingState)CreateFrameState(job);

        if (state.Enhancer != null)
            state.Enhancer.InitializeAsync(
                state.OutMat.Width,
                state.OutMat.Height,
                5,
                token).Wait(token);

        var decode = StartFfmpegDecode(
            job.Job.InputFile,
            job.SegmentStart,
            job.SegmentLength,
            job.Job.Rotate,
            job.Job.PlainText,
            token).Result;

        state.DecodeProcess = decode;
        state.DecodeStdout = decode.StandardOutput.BaseStream;

        return state;
    }

    // ------------------------------------------------------------
    // GetNextProcessedFrame
    // ------------------------------------------------------------

    public Mat? GetNextProcessedFrame(
        IFrameProcessingState processorState,
        CancellationToken token)
    {
        var state = (FrameProcessingState)processorState;

        if (state.DecodeStdout == null)
            return null;

        if (!TryReadNextFrame(state.DecodeStdout, state, token))
            return null;

        return ProcessFrame(state.FrameMat, state, state.Job, token);
    }

    // ------------------------------------------------------------
    // FinishSegment
    // ------------------------------------------------------------

    public void FinishSegment(IFrameProcessingState processorState)
    {
        var state = (FrameProcessingState)processorState;

        try
        {
            if (state.DecodeProcess != null && !state.DecodeProcess.HasExited)
                state.DecodeProcess.Kill(entireProcessTree: true);
        }
        catch { }

        try
        {
            if (state.DecodeProcess != null && !state.DecodeProcess.HasExited)
                state.DecodeProcess.WaitForExit();
        }
        catch { }

        if (state.Enhancer is IAsyncDisposable ad)
            ad.DisposeAsync().AsTask().Wait();
        else if (state.Enhancer is IDisposable d)
            d.Dispose();
    }

    // ============================================================
    // PROCESSSEGMENT (full pipeline)
    // ============================================================

    public async Task ProcessSegment(SingleTask job, CancellationToken token)
    {
        var name = Path.GetFileNameWithoutExtension(job.OutputFileName);
        var fps  = job.Info.Fps;

        var state = (FrameProcessingState)InitSegment(job, token);

        var encode = await StartFfmpegEncode(
            job.Job.InputFile,
            job.OutputFileName,
            job.SegmentStart,
            job.SegmentLength,
            state.OutMat.Width,
            state.OutMat.Height,
            job.Info,
            job.Job.Passthrough,
            job.Job.PlainText,
            token);

        using var encodeStdin = encode.StandardInput.BaseStream;

        var totalFrames = (int)Math.Round(job.SegmentLength * fps);
        var frameIndex  = 0;
        var startTime   = DateTime.UtcNow;

        while (frameIndex < totalFrames)
        {
            token.ThrowIfCancellationRequested();

            var frame = GetNextProcessedFrame(state, token);
            if (frame == null)
                break;

            frameIndex++;

            EncodeFrame(frame, state, encodeStdin);

            var elapsed         = DateTime.UtcNow - startTime;
            var progress        = totalFrames > 0 ? (double)frameIndex / totalFrames : 0.0;
            var speed           = elapsed.TotalSeconds > 0 ? (frameIndex / elapsed.TotalSeconds) / fps : 0.0;

            var remainingFrames = Math.Max(totalFrames - frameIndex, 0);
            var etaSeconds      = speed > 0 ? remainingFrames / speed : 0.0;
            var eta             = TimeSpan.FromSeconds(etaSeconds);

            DrawProgress(name, progress, eta, speed);
        }

        encodeStdin.Flush();
        encodeStdin.Close();

        await encode.WaitForExitAsync();

        ClearProgress(name);

        if (encode.ExitCode != 0)
            LogError($"{name}: FFmpeg encoding failed");
        else
            LogInfo($"{name}: Segment processing completed");

        FinishSegment(state);
    }

    // ============================================================
    // INTERNAL HELPERS
    // ============================================================

    private object CreateFrameState(SingleTask job)
    {
        var w  = job.Info.Width;
        var h  = job.Info.Height;
        var cw = job.Job.Debug ? w : job.Job.Crop!.Value.width;
        var ch = job.Job.Debug ? h : job.Job.Crop!.Value.height;

        var kalman = new KalmanTracker();
        var camera = new CameraController(w, h, cw, ch, kalman, job.Job);

        var frameMat = new Mat(h, w, MatType.CV_8UC3);
        var outMat   = new Mat(ch, cw, MatType.CV_8UC3);

        var inBytes  = w * h * 3;
        var outBytes = cw * ch * 3;

        var inBuffer  = new byte[inBytes];
        var outBuffer = new byte[outBytes];

        IVideoEnhancer? enhancer = job.Job.Enhance
            ? new RealBasicVsr2xDmlEnhancer()
            : null;

        return new FrameProcessingState(
            job,
            kalman,
            camera,
            frameMat,
            outMat,
            inBuffer,
            outBuffer,
            enhancer,
            inBytes,
            outBytes);
    }

    private bool TryReadNextFrame(
        Stream decodeStdout,
        FrameProcessingState state,
        CancellationToken token)
    {
        var read = ReadExact(
            decodeStdout,
            state.InBuffer,
            0,
            state.InBytes,
            token).Result;

        if (read != state.InBytes)
            return false;

        Marshal.Copy(state.InBuffer, 0, state.FrameMat.Data, state.InBytes);
        return true;
    }

    private Mat ProcessFrame(
        Mat inputFrame,
        FrameProcessingState state,
        SingleTask job,
        CancellationToken token)
    {
        var (objects, primary) =
            _tracker.SelectTrackedObject(job, inputFrame, state.Kalman.LastMeasurement);

        state.Camera.Update(primary);
        var roi = state.Camera.Roi;

        if (job.Job.Debug)
        {
            DebugOverlay.DrawDebug(inputFrame, objects, state.Camera, state.Kalman);
            inputFrame.CopyTo(state.OutMat);
        }
        else
        {
            using var cropped = new Mat(inputFrame, roi);
            cropped.CopyTo(state.OutMat);
        }

        if (state.Enhancer != null)
        {
            if (state.Enhancer.TryProcessFrame(state.OutMat, out var enhanced, token))
                return enhanced;

            return state.OutMat;
        }

        return state.OutMat;
    }

    private void EncodeFrame(
        Mat frame,
        FrameProcessingState state,
        Stream encodeStdin)
    {
        Marshal.Copy(frame.Data, state.OutBuffer, 0, state.OutBytes);
        encodeStdin.Write(state.OutBuffer, 0, state.OutBytes);
    }
    
    // ------------------------------------------------------------
    // FFmpeg helpers
    // ------------------------------------------------------------

    private async Task<Process> StartFfmpegDecode(
        string inputFile,
        double start,
        double length,
        int? rotate,
        bool plainText,
        CancellationToken token)
    {
        var ss = start .ToString("0.###", CultureInfo.InvariantCulture);
        var t  = length.ToString("0.###", CultureInfo.InvariantCulture);

        var rotateStr = GetRorationArg(rotate);

        var args =
            $"-i \"{inputFile}\" -ss {ss} -t {t} " +
            "-an -sn " +
            $"-vf format=bgr24{rotateStr} " +
            "-f rawvideo -";

        var psi = new ProcessStartInfo
        {
            FileName               = "ffmpeg",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var p = new Process { StartInfo = psi };
        p.Start();

        var fileName = Path.GetFileName(inputFile);

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync(token)) != null)
                    if (plainText)
                        LogInfo($"[ffmpeg-decode] {fileName}: {line}");
            }
            catch { }
        });

        return p;
    }

    public static string GetRorationArg(int? rotate)
    {
        var rotateStr = "";
        if (rotate != null)
        {
            switch (rotate.Value)
            {
                case 90: rotateStr = ",transpose=1"; break;
                case 180: rotateStr = ",transpose=PI"; break;
                case 270: rotateStr = ",transpose=2"; break;
            }
        }
        return rotateStr;
    }

    private async Task<Process> StartFfmpegEncode(
        string inputFile,
        string outputFile,
        double start,
        double length,
        int width,
        int height,
        VideoInfo info,
        string[] passthrough,
        bool plainText,
        CancellationToken token)
    {
        var pass   = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";
        var fpsStr = info.Fps.ToString("0.###", CultureInfo.InvariantCulture);
        var ss     = start.ToString("0.###", CultureInfo.InvariantCulture);
        var t      = length.ToString("0.###", CultureInfo.InvariantCulture);

        var sarArg = !string.IsNullOrWhiteSpace(info.SampleAspectRatio)
            ? $"-vf setsar={info.SampleAspectRatio} "
            : "";

        var darArg = "";
        if (info.Sar is { } s)
        {
            var darNum = width  * s.X;
            var darDen = height * s.Y;

            var dn = (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, darNum));
            var dd = (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, darDen));
            ReduceFraction(ref dn, ref dd);

            if (dn > 0 && dd > 0)
                darArg = $"-aspect {dn}:{dd} ";
        }

        var args =
            "-y " +
            $"-f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fpsStr} -i - " +
            $"-ss {ss} -i \"{inputFile}\" " +
            "-map 0:v:0 -map 1:a:0? -shortest " +
            "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
            sarArg + darArg +
            "-c:a copy " +
            pass + $" \"{outputFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName              = "ffmpeg",
            Arguments             = args,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        var p = new Process { StartInfo = psi };
        p.Start();

        var fileName = Path.GetFileName(outputFile);

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync(token)) != null)
                    if (plainText)
                        LogInfo($"[ffmpeg-encode] {fileName}: {line}");
            }
            catch { }
        });

        return p;
    }

    private static void ReduceFraction(ref int num, ref int den)
    {
        int Gcd(int a, int b)
        {
            while (b != 0)
            {
                var t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        var g = Gcd(Math.Abs(num), Math.Abs(den));
        if (g > 1)
        {
            num /= g;
            den /= g;
        }
    }

    private static async Task<int> ReadExact(
        Stream s,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken token)
    {
        var total = 0;
        while (total < count)
        {
            var read = await s.ReadAsync(buffer, offset + total, count - total, token);
            if (read <= 0)
                break;
            total += read;
        }
        return total;
    }


}
