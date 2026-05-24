using NcnnDotNet.Layers;
using OpenCvSharp;
using splitter.tui;

namespace Splitter_UI.Services;

public sealed class AutoDecisionService(IThumbnailService _thumbnails, IFileProbeService _fileProbe, ILogger _log) : IAutoDecisionService
{
    public void ApplyAutoDecisions(JobViewModel job)
    {
        Task.Run(() => Detect(job));
    }

    private async Task Detect(JobViewModel job)
    {
        try
        {
            job.Probe           = await _fileProbe.ProbeAsync(job.InputFile);
            job.Thumbnail       = await _thumbnails.CreateThumbnailAsync(job.InputFile, job.Probe, rotateDegree: job.Rotate);

            var sampler         = new VideoRotationSampler(null);
            job.Rotate          = await sampler.DetectRotationAsync(job.InputFile, job.Probe.Duration);
            job.SuggestedAction = job.Rotate == 0 ? "crop" : "rotate";

            if (job.SuggestedAction == "crop")
                job.Detect = "body";
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating thumbnail for {Path.GetFileName(job.InputFile)}: {ex.Message}");
        }
    }
}
