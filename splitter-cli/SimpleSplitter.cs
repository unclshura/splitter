using System.Diagnostics;
using System.Globalization;

namespace splitter;

public sealed class SimpleSplitter : LoggingBase, ISegmentProcessor
{
    // ------------------------------------------------------------
    // Internal state (opaque to caller)
    // ------------------------------------------------------------

    private sealed class State : IFrameProcessingState
    {
        public Process? DecodeProcess { get; set; }
        public Stream? DecodeStdout   { get; set; }

        public string InputFile       { get; }
        public double Start           { get; }
        public double Length          { get; }
        public int? Rotate            { get; }
        public string[] Passthrough   { get; }
        public VideoInfo Info         { get; }
        public bool PlainText         { get; }

        public State(SingleTask job)
        {
            InputFile   = job.Job.InputFile;
            Start       = job.SegmentStart;
            Length      = job.SegmentLength;
            Rotate      = job.Job.Rotate;
            Passthrough = job.Job.Passthrough;
            Info        = job.Info;
            PlainText   = job.Job.PlainText;
        }
    }

    public SimpleSplitter(int segmentNo, ILogger logger)
        : base(logger, segmentNo)
    {
    }

    // ============================================================
    // InitSegment
    // ============================================================

    public IFrameProcessingState InitSegment(SingleTask job, CancellationToken token)
    {
        var state = new State(job);

        var decode          = StartDecode(job, token);
        state.DecodeProcess = decode;
        state.DecodeStdout  = decode.StandardOutput.BaseStream;

        return state;
    }

    // ============================================================
    // GetNextProcessedFrame
    // ============================================================

    public FrameProcessingResult GetNextProcessedFrame(IFrameProcessingState processorState, CancellationToken token)
    {
        var state = (State)processorState;

        if (state.DecodeStdout == null)
            return new FrameProcessingResult(null, [], null);

        // SimpleSplitter does not modify frames; it only copies or rotates.
        // For preview, we decode raw frames and return them as-is.

        // Determine expected frame size
        var w = state.Info.Width;
        var h = state.Info.Height;
        var bytes = w * h * 3;

        var buffer = new byte[bytes];
        var read = state.DecodeStdout.Read(buffer, 0, bytes);
        if (read != bytes)
            return new FrameProcessingResult(null, [], null);

        var mat = new Mat(h, w, MatType.CV_8UC3);
        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, mat.Data, bytes);

        return new FrameProcessingResult(mat, [], null);
    }

    // ============================================================
    // FinishSegment
    // ============================================================

    public void FinishSegment(IFrameProcessingState processorState)
    {
        var state = (State)processorState;

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
    }

    // ============================================================
    // ProcessSegment (now uses preview API)
    // ============================================================

    public async Task ProcessSegment(SingleTask job, Action<FrameProcessingResult>? onFrameProcessed, CancellationToken token)
    {
        var state = (State)InitSegment(job, token);

        var encode = StartEncode(job);
        using var encodeStdin = encode.StandardInput.BaseStream;

        var name = Path.GetFileNameWithoutExtension(job.OutputFileName);
        var sw   = Stopwatch.StartNew();

        while (true)
        {
            token.ThrowIfCancellationRequested();

            var res = GetNextProcessedFrame(state, token);
            var frame = res.Image;
            if (frame == null)
                break;

            // Write raw frame to encoder
            var bytes = frame.Width * frame.Height * 3;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(frame.Data, buffer, 0, bytes);
            encodeStdin.Write(buffer, 0, bytes);
            
            onFrameProcessed?.Invoke(res);

            frame.Dispose();
        }

        encodeStdin.Flush();
        encodeStdin.Close();

        await encode.WaitForExitAsync(token);

        FinishSegment(state);

        ClearProgress(name);

        if (encode.ExitCode != 0)
            LogError($"Segment {name} FFmpeg encoding failed");
        else
            LogInfo($"Segment {name} processing completed");
    }

    // ============================================================
    // FFmpeg helpers
    // ============================================================

    private Process StartDecode(SingleTask job, CancellationToken token)
    {
        var ss = job.SegmentStart.ToString("0.###", CultureInfo.InvariantCulture);
        var t  = job.SegmentLength.ToString("0.###", CultureInfo.InvariantCulture);

        var rotate = GetRotationFilter(job.Job.Rotate);
        var vf = rotate != null ? $"-vf format=bgr24,{rotate}" : "-vf format=bgr24";

        var args =
            $"-i \"{job.Job.InputFile}\" -ss {ss} -t {t} " +
            "-an -sn " +
            $"{vf} " +
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

        var p = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg decode.");
        return p;
    }

    private Process StartEncode(SingleTask job)
    {
        var inputFile  = job.Job.InputFile;
        var outputFile = job.OutputFileName;
        var start      = job.SegmentStart;
        var length     = job.SegmentLength;

        var rotation = GetRotationFilter(job.Job.Rotate);

        string args;

        if (rotation == null)
        {
            args =
                $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFile}\" " +
                $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
                $"-c copy {string.Join(" ", job.Job.Passthrough)} " +
                $"\"{outputFile}\" -y";
        }
        else
        {
            var sarArg = "";
            var darArg = "";

            var sar = job.Info.SampleAspectRatio;
            if (sar != null)
            {
                var sarNum = Convert.ToInt64(job.Info.Sar.X);
                var sarDen = Convert.ToInt64(job.Info.Sar.Y);

                var w = job.Info.Width;
                var h = job.Info.Height;

                if (job.Job.Rotate == 90 || job.Job.Rotate == 270)
                    (w, h) = (h, w);

                var darNum = w * sarNum;
                var darDen = h * sarDen;

                long Gcd(long a, long b)
                {
                    while (b != 0) (a, b) = (b, a % b);
                    return a;
                }

                var g = Gcd(darNum, darDen);
                darNum /= g;
                darDen /= g;

                sarArg = $"-vf \"{rotation},setsar={sarNum}:{sarDen}\" ";
                darArg = $"-aspect {darNum}:{darDen} ";
            }
            else
                sarArg = $"-vf \"{rotation}\" ";

            args =
                $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFile}\" " +
                $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
                sarArg + darArg +
                "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
                "-c:a copy " +
                $"{string.Join(" ", job.Job.Passthrough)} " +
                $"\"{outputFile}\" -y";
        }

        var psi = new ProcessStartInfo
        {
            FileName              = "ffmpeg",
            Arguments             = args,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        return Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg encode.");
    }

    private string? GetRotationFilter(int? degrees) =>
        degrees switch
        {
            90 => "transpose=1",
            180 => "rotate=PI",
            270 => "transpose=2",
            _ => null
        };
}
