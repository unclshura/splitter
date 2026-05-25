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
            job.GravitateTo            = new(0.5f, 0.5f);
            job.OverrideTargetDuration = 58.0;
            job.Mask                   = "[NAME]_seg[NN].[EXT]";
            job.OutputFolder           = Path.Combine(Path.GetDirectoryName(job.InputFile)!, "splitter");

            job.Probe           = await _fileProbe.ProbeAsync(job.InputFile);
            job.Thumbnail       = await _thumbnails.CreateThumbnailAsync(job.InputFile, job.Probe, rotateDegree: job.Rotate);

            if (job.Probe.Width > job.Probe.Height)
            {
                job.Detect = "body";
                job.Rotate = 0;

                CalculateCrop(job);
            }
            else
            {
                var sampler         = new VideoRotationSampler(null);
                job.Rotate = await sampler.DetectRotationAsync(job.InputFile, job.Probe.Duration);
                job.Detect = job.Rotate == 0 ? null : "body";
            }

            _log.LogInfo(job.ToString());
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating thumbnail for {Path.GetFileName(job.InputFile)}: {ex.Message}");
        }
    }

    private static void CalculateCrop(JobViewModel job)
    {
        var targetAR = (float)CommandLine.DefaultW / CommandLine.DefaultH;
        var pixelAspect = job.Probe!.Sar.X / job.Probe.Sar.Y;

        float srcW = job.Probe.Width  * pixelAspect;
        float srcH = job.Probe.Height;
        var srcAR = srcW / srcH;

        float cropH = srcH;
        float cropW = cropH * targetAR;

        if (cropW > srcW)
        {
            cropW = srcW;
            cropH = cropW / targetAR;
        }

        float x = (srcW - cropW) * 0.5f;
        float y = (srcH - cropH) * 0.5f;

        float invPixelAspect = 1f / pixelAspect;

        float cropW_px = cropW * invPixelAspect;
        float cropH_px = cropH;

        float x_px = x * invPixelAspect;
        float y_px = y;

        job.CropText = $"{(int)MathF.Round(cropW_px)},{(int)MathF.Round(cropH_px)}";
    }
}
