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

        var args =
            $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
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
}
