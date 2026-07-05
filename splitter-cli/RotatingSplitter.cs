using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Spectre.Console;

namespace splitter;

public sealed class RotatingSplitter : LoggingBase, ISegmentProcessor
{
    private sealed class State : IFrameProcessingState
    {
        public Process? EncodeProcess { get; set; }

        public string InputFile { get; }
        public string OutputFile { get; }
        public double Start { get; }
        public double Length { get; }
        public int Rotate { get; }
        public VideoInfo Info { get; }
        public string[] Passthrough { get; }

        public State(SingleTask job)
        {
            InputFile = job.Job.InputFile;
            OutputFile = job.OutputFileName;
            Start = job.SegmentStart;
            Length = job.SegmentLength;
            Rotate = job.Job.Rotate ?? 0;
            Info = job.Info;
            Passthrough = job.Job.Passthrough;
        }
    }

    public RotatingSplitter(int segmentNo, ILogger logger)
        : base(logger, segmentNo)
    {
    }

    public IFrameProcessingState InitSegment(SingleTask job, CancellationToken token)
    {
        var state = new State(job);
        state.EncodeProcess = StartEncode(job, state.Info, state.Rotate);
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
        DrawProgress(Path.GetFileName(job.OutputFileName), 0, TimeSpan.FromSeconds(10), 60);

        var state = (State)InitSegment(job, token);

        var p = state.EncodeProcess;
        if (p != null)
            await p.WaitForExitAsync(token);

        FinishSegment(state);

        ClearProgress(job.OutputFileName);

        if (p != null && p.ExitCode != 0)
            LogError($"Segment {job.OutputFileName} FFmpeg rotation failed");
        else
            LogInfo($"Segment {job.OutputFileName} rotation completed");
    }

    private Process StartEncode(SingleTask job, VideoInfo info, int rotate)
    {
        var inputFile  = job.Job.InputFile;
        var outputFile = job.OutputFileName;
        var start      = job.SegmentStart;
        var length     = job.SegmentLength;

        var rotation = GetRotationFilter(rotate);

        // Rotation only. No SAR/DAR manipulation.
        var vfArg = $"-vf \"{rotation}\" ";

        var args =
        $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
        $"-i \"{inputFile}\" " +
        $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
        vfArg +
        "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
        "-c:a copy " +
        $"{string.Join(" ", job.Job.Passthrough)} " +
        $"\"{outputFile}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName              = "ffmpeg",
            Arguments             = args,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        var p = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg encode.");

        // Drain stderr and log
        _ = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = p.StandardError.ReadLine()) != null)
                    LogInfo(line);
            }
            catch { }
        });

        return p;
    }

    private static string GetRotationFilter(int degrees) =>
        degrees switch
        {
            90 => "transpose=1",
            180 => "rotate=PI",
            270 => "transpose=2",
            _ => "transpose=1"
        };
}
