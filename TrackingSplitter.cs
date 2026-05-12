using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;

namespace splitter;

public class TrackingSplitter : LoggingBase, ISegmentProcessor, IDisposable
{
    private readonly int  _segmentNo;
    private readonly int  _cropWidth;
    private readonly int  _cropHeight;
    private readonly bool _debugOverlay;
    private readonly bool _plainText;

    private readonly IObjectDetector _detector;
    private readonly CommandLine     _cmd;

    public TrackingSplitter(int segmentNo, int cropWidth, int cropHeight, bool debugOverlay, bool plainText, IObjectDetector detector, CommandLine cmd, ILogger logger) 
        : base(logger, segmentNo)
    {
        _segmentNo    = segmentNo;
        _cropWidth    = cropWidth;
        _cropHeight   = cropHeight;
        _debugOverlay = debugOverlay;
        _plainText    = plainText;
        _detector     = detector;
        _cmd          = cmd;
    }

    public void Dispose()
    {
        if (_detector is IDisposable d)
            d.Dispose();
    }

    public async Task ProcessSegment(string inputFile, string outputFile, double start, double length, string[] ffmpegPassthroughParameters)
    { 
        using var capture = new VideoCapture(inputFile);
        if (!capture.IsOpened())
            throw new Exception("Cannot open video");

        var name     = Path.GetFileNameWithoutExtension(outputFile);
        var skip     = TimeSpan.FromSeconds(start);
        var duration = TimeSpan.FromSeconds(length);

        capture.Set(VideoCaptureProperties.PosMsec, start);

        var videoWidth  = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        var videoHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        var fps         = capture.Get(VideoCaptureProperties.Fps);
        var totalFrames = (int)(length * fps);

        var originalCropWidth  = _cropWidth;
        var originalCropHeight = _cropHeight;

        LogInfo($"[TrackingSplitter] skip={skip}, duration={duration}, fps={fps}, totalFrames={totalFrames}");

        var encWidth  = _debugOverlay ? videoWidth  : originalCropWidth;
        var encHeight = _debugOverlay ? videoHeight : originalCropHeight;

        var ffmpeg = StartFfmpegNvenc(
            inputFile,
            outputFile,
            encWidth,
            encHeight,
            fps,
            skip,
            ffmpegPassthroughParameters);

        using var stdin = ffmpeg.StandardInput.BaseStream;

        using var frame     = new Mat();
        using var outputBgr = new Mat(encHeight, encWidth, MatType.CV_8UC3);

        var frameBytes  = encWidth * encHeight * 3;
        var videoBuffer = new byte[frameBytes];

        var kalman = new KalmanTracker();
        // initial reset is now done inside CameraController

        var camera = new CameraController(
            videoWidth,
            videoHeight,
            originalCropWidth,
            originalCropHeight,
            kalman,
            _cmd
            );

        var startTime = DateTime.UtcNow;

        for (var i = 0; i < totalFrames; i++)
        {
            if (!capture.Read(frame) || frame.Empty())
                break;

            Rect?    objectBox    = null;
            Point2f? objectCenter = null;

            var objects = _detector.DetectAll(frame, videoWidth, videoHeight);
            var primary = SelectTrackedObject(objects, kalman.LastMeasurement);

            camera.Update(primary);

            objectBox = camera.ObjectBox;
            objectCenter = camera.ObjectCenter;

            var smoothedCenter = camera.SmoothedCenter;
            var cameraCenter   = camera.CameraCenter;
            var state          = camera.State;
            var lostFrames     = camera.LostFrames;
            var roi            = camera.Roi;

            if (_debugOverlay)
            {
                if (objectBox.HasValue)
                {
                    var fb = objectBox.Value;
                    Cv2.Rectangle(frame,
                        new Rect(fb.X, fb.Y, fb.Width, fb.Height),
                        Scalar.LimeGreen, 2);
                }

                Cv2.Circle(frame,
                    new Point((int)smoothedCenter.X, (int)smoothedCenter.Y),
                    6, Scalar.LimeGreen, -1);

                Cv2.Rectangle(frame, roi,
                    objectCenter.HasValue ? Scalar.Yellow : Scalar.Red, 3);

                DrawText(frame, $"Faces: {objects.Count}", 20, 40, Scalar.White);
                DrawText(frame, $"LostFrames: {lostFrames}", 20, 70, Scalar.White);
                DrawText(frame, $"Noise: {kalman.CurrentNoise:F3}", 20, 130, Scalar.White);
                DrawText(frame, $"Camera: {cameraCenter.X:F1},{cameraCenter.Y:F1}", 20, 160, Scalar.White);
            }

            if (_debugOverlay)
            {
                frame.CopyTo(outputBgr);
                Marshal.Copy(outputBgr.Data, videoBuffer, 0, frameBytes);
                stdin.Write(videoBuffer, 0, frameBytes);
            }
            else
            {
                using var cropped = new Mat(frame, roi);
                cropped.CopyTo(outputBgr);

                Marshal.Copy(outputBgr.Data, videoBuffer, 0, frameBytes);
                stdin.Write(videoBuffer, 0, frameBytes);
            }

            var elapsed         = DateTime.UtcNow - startTime;
            var progress        = (double)i / totalFrames;
            var speed           = i > 0 ? (i / elapsed.TotalSeconds)/fps : 0.0;
            var remainingFrames = totalFrames - i;
            var etaSeconds      = speed > 0 ? remainingFrames / speed : 0;
            var eta             = TimeSpan.FromSeconds(etaSeconds);

            DrawProgress(name, progress, eta, speed);
        }

        stdin.Flush();
        stdin.Close();

        await ffmpeg.WaitForExitAsync();

        ClearProgress();

        if (ffmpeg.ExitCode != 0)
            LogError($"Segment {name} FFmpeg encoding failed");
        else
            LogInfo($"Segment {name} processing completed");
    }

    private (Rect box, Point2f center)? SelectTrackedObject(
        List<(Rect box, Point2f center)> foundObjects,
        Point2f? previousCenter)
    {
        if (foundObjects == null || foundObjects.Count == 0)
            return null;

        if (!previousCenter.HasValue)
        {
            // Largest area
            var bestIndex = 0;
            var bestArea  = float.MinValue;

            for (int i = 0; i < foundObjects.Count; i++)
            {
                var f    = foundObjects[i];
                var area = f.box.Width * f.box.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            return foundObjects[bestIndex];
        }
        else
        {
            // Closest to previous center
            var prev = previousCenter.Value;
            var bestIndex = 0;
            var bestDist2 = float.MaxValue;

            for (int i = 0; i < foundObjects.Count; i++)
            {
                var f  = foundObjects[i];
                var dx = f.center.X - prev.X;
                var dy = f.center.Y - prev.Y;
                var d2 = dx * dx + dy * dy;

                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestIndex = i;
                }
            }

            return foundObjects[bestIndex];
        }
    }

    private Process StartFfmpegNvenc(
        string srcFileName,
        string destFileName,
        int width,
        int height,
        double fps,
        TimeSpan skip,
        string[] passthrough)
    {
        var pass        = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";
        var skipSeconds = skip.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var fpsStr      = fps.ToString("0.###", CultureInfo.InvariantCulture);

        var args =
            "-y " +
            $"-f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fpsStr} -i - " +
            $"-ss {skipSeconds} -i \"{srcFileName}\" " +
            "-map 0:v:0 -map 1:a:0? -shortest " +
            "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
            "-c:a aac -b:a 192k " +
            pass + $" \"{destFileName}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = "ffmpeg",
            Arguments              = args,
            RedirectStandardInput  = true,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var process = new Process { StartInfo = psi };
        process.Start();

        _ = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = process.StandardError.ReadLine()) != null)
                {
                    if (_plainText)
                        Console.WriteLine($"[ffmpeg] {line}");
                }
            }
            catch { }
        });

        return process;
    }

    private static void DrawText(Mat img, string text, int x, int y, Scalar color)
    {
        Cv2.PutText(img, text, new Point(x, y),
            HersheyFonts.HersheySimplex, 0.6, color, 2);
    }

}
