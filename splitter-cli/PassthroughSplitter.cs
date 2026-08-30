using System.Diagnostics;
using System.Globalization;

namespace splitter;

public sealed class PassthroughSplitter : LoggingBase, ISegmentProcessor
{
    private sealed class State : IFrameProcessingState
    {
        public Process? EncodeProcess { get; set; }

        public string InputFile { get; }
        public string OutputFile { get; }
        public double Start { get; }
        public double Length { get; }
        public string[] Passthrough { get; }

        public State(SingleTask job)
        {
            InputFile = job.Job.InputFile;
            OutputFile = job.OutputFileName;
            Start = job.SegmentStart;
            Length = job.SegmentLength;
            Passthrough = job.Job.Passthrough;
        }
    }

    public PassthroughSplitter(int segmentNo, ILogger logger)
        : base(logger, segmentNo)
    {
    }

    public IFrameProcessingState InitSegment(SingleTask job, CancellationToken token)
    {
        var state = new State(job);
        state.EncodeProcess = StartEncode(job);
        return state;
    }

    public FrameProcessingResult GetNextProcessedFrame(IFrameProcessingState processorState, CancellationToken token)
    {
        return new FrameProcessingResult(null, [], null);
    }

    public void FinishSegment(IFrameProcessingState processorState)
    {
        var state = (State)processorState;

        try
        {
            if (state.EncodeProcess != null && !state.EncodeProcess.HasExited)
                state.EncodeProcess.WaitForExit();
        }
        catch { }
    }

    public async Task ProcessSegment(
        SingleTask job,
        Action<FrameProcessingResult>? onFrameProcessed,
        CancellationToken token)
    {
        var state = (State)InitSegment(job, token);

        var p = state.EncodeProcess;
        if (p != null)
            await p.WaitForExitAsync(token);

        FinishSegment(state);

        ClearProgress(job.OutputFileName);

        if (p != null && p.ExitCode != 0)
            LogError($"Segment {job.OutputFileName} FFmpeg passthrough failed");
        else
            LogInfo($"Segment {job.OutputFileName} passthrough completed");
    }

    private Process StartEncode(SingleTask job)
    {
        var inputFile  = job.Job.InputFile;
        var outputFile = job.OutputFileName;
        var start      = job.SegmentStart;
        var length     = job.SegmentLength;

        // Probe next keyframe packet within a 5-second window
        var nextKey = ProbeNextKeyframePacket(inputFile, start);

        // Adjust by subtracting X.XX seconds
        const double shiftBack = 0.1;
        var seekPos = nextKey > shiftBack ? nextKey - shiftBack : 0.0;

        var args =
            $"-ss {seekPos.ToString(CultureInfo.InvariantCulture)} " +
            $"-i \"{inputFile}\" " +
            $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
            $"-c copy {string.Join(" ", job.Job.Passthrough)} " +
            $"\"{outputFile}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName              = "ffmpeg",
            Arguments             = args,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        return Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg passthrough.");
    }

    public static double ProbeNextKeyframePacket(string inputFile, double segmentStart, double window = 11.0)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments =
            "-select_streams v " +
            $"-read_intervals \"{(segmentStart-0.1).ToString(CultureInfo.InvariantCulture)}%+{window.ToString(CultureInfo.InvariantCulture)}\" " +
            "-skip_frame nokey " +
            "-show_packets " +
            "-show_entries packet=pts_time,flags " +
            "-of compact=p=0 " +
            "-v quiet " +
            $"\"{inputFile}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        var output = p!.StandardOutput.ReadToEnd();
        p.WaitForExit();

        double best = double.MaxValue;

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Example line:
            // pts_time=23.520000|flags=K__
            var parts = line.Split('|');
            if (parts.Length < 2)
                continue;

            var ptsPart = parts[0].Trim();   // pts_time=23.520000
            var flagsPart = parts[1].Trim(); // flags=K__

            if (!flagsPart.Contains("K"))
                continue;

            var ptsStr = ptsPart.Replace("pts_time=", "");
            if (!double.TryParse(ptsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts))
                continue;

            if ( best == double.MaxValue)
                best = pts;
            if (pts > segmentStart)
                break;
        }

        return best == double.MaxValue ? segmentStart : best;
    }

}
