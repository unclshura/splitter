using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;

namespace splitter;

public class TrackingSplitter(
    Action<string/*level*/, ConsoleColor /*color*/, string /*message*/> log,
    Action<double /*percent*/, TimeSpan /*duration*/, double /*fps*/> drawProgress
    ) : LoggingBase(log, drawProgress)
{
    private const int   LostFreezeFrames = 60; // 2 seconds at 30 FPS
    private const float CameraEasing     = 0.03f;

    private enum TrackState
    {
        Tracking,
        LostFreeze,
        LostDrift
    }

    public async Task TrackAndExtract(
        string srcFileName,
        string destFileName,
        IObjectDetector detector,
        TimeSpan skip,
        TimeSpan duration,
        int cropWidth,
        int cropHeight,
        string[] passthrough,
        bool debugOverlay)
    {
        using var capture = new VideoCapture(srcFileName);
        if (!capture.IsOpened())
            throw new Exception("Cannot open video");

        capture.Set(VideoCaptureProperties.PosMsec, skip.TotalMilliseconds);

        var videoWidth  = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        var videoHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        var fps         = capture.Get(VideoCaptureProperties.Fps);
        var totalFrames = (int)(duration.TotalSeconds * fps);

        var originalCropWidth  = cropWidth;
        var originalCropHeight = cropHeight;

        Console.WriteLine($"[TrackingSplitter] skip={skip}, duration={duration}, fps={fps}, totalFrames={totalFrames}");

        // encoder size depends on mode
        var encWidth  = debugOverlay ? videoWidth  : originalCropWidth;
        var encHeight = debugOverlay ? videoHeight : originalCropHeight;

        var ffmpeg = StartFfmpegNvenc(
            srcFileName,
            destFileName,
            encWidth,
            encHeight,
            fps,
            skip,
            passthrough);

        using var stdin = ffmpeg.StandardInput.BaseStream;

        // Reusable frame and output mat
        using var frame     = new Mat();
        using var outputBgr = new Mat(encHeight, encWidth, MatType.CV_8UC3);

        // Reusable raw video buffer
        var frameBytes = encWidth * encHeight * 3;
        var videoBuffer = new byte[frameBytes];

        var kalman = new KalmanTracker();
        kalman.Reset(new Point2f(videoWidth / 2f, videoHeight / 2f));

        var lostFrames       = 0;
        var reacquireCounter = 0; // kept for overlay display

        var cameraCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);
        var startTime    = DateTime.UtcNow;
        var state        = TrackState.Tracking;

        for (var i = 0; i < totalFrames; i++)
        {
            if (!capture.Read(frame) || frame.Empty())
                break;

            Rect?    objectBox    = null;
            Point2f? objectCenter = null;

            var objects = detector.DetectAll(frame, videoWidth, videoHeight);
            var primary = SelectTrackedObject(objects, kalman.LastMeasurement);

            if (primary.HasValue)
            {
                objectCenter = primary.Value.center;
                objectBox = primary.Value.box;
            }

            bool isLost = !objectCenter.HasValue;

            // LOST / REACQUIRE STATE MACHINE
            if (isLost)
            {
                lostFrames++;

                if (lostFrames <= LostFreezeFrames)
                {
                    // LOST_FREEZE: freeze camera
                    state = TrackState.LostFreeze;
                    objectCenter = null; // Kalman predicts but camera won't move
                }
                else
                {
                    // LOST_DRIFT: drift camera to center
                    state = TrackState.LostDrift;
                    objectCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);
                }
            }
            else
            {
                // Object reacquired
                state = TrackState.Tracking;
                lostFrames = 0;
            }

            // KALMAN + CAMERA UPDATE
            Point2f smoothedCenter;

            if (state == TrackState.Tracking)
            {
                smoothedCenter = kalman.Update(objectCenter);

                float easing = 0.015f; // faster tracking
                cameraCenter = new Point2f(
                    cameraCenter.X + (smoothedCenter.X - cameraCenter.X) * easing,
                    cameraCenter.Y + (smoothedCenter.Y - cameraCenter.Y) * easing);
            }
            else if (state == TrackState.LostFreeze)
            {
                // Freeze camera — do nothing
                smoothedCenter = kalman.LastMeasurement ?? new Point2f(0, 0);
            }
            else // LOST_DRIFT
            {
                smoothedCenter = kalman.Update(objectCenter);

                float driftEasing = 0.01f;
                var fallbackCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);

                cameraCenter = new Point2f(
                    cameraCenter.X + (fallbackCenter.X - cameraCenter.X) * driftEasing,
                    cameraCenter.Y + (fallbackCenter.Y - cameraCenter.Y) * driftEasing);
            }

            var halfW = originalCropWidth  / 2f;
            var halfH = originalCropHeight / 2f;

            smoothedCenter.X = Math.Clamp(smoothedCenter.X, halfW, videoWidth - halfW);
            smoothedCenter.Y = Math.Clamp(smoothedCenter.Y, halfH, videoHeight - halfH);

            if (state == TrackState.Tracking)
            {
                smoothedCenter = kalman.Update(objectCenter);

                cameraCenter = new Point2f(
                    cameraCenter.X + (smoothedCenter.X - cameraCenter.X) * CameraEasing,
                    cameraCenter.Y + (smoothedCenter.Y - cameraCenter.Y) * CameraEasing);
            }
            else if (state == TrackState.LostFreeze)
            {
                // Freeze camera — do nothing
            }
            else if (state == TrackState.LostDrift)
            {
                var fallbackCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);

                cameraCenter = new Point2f(
                    cameraCenter.X + (fallbackCenter.X - cameraCenter.X) * 0.01f,
                    cameraCenter.Y + (fallbackCenter.Y - cameraCenter.Y) * 0.01f);
            }

            cameraCenter.X = Math.Clamp(cameraCenter.X, halfW, videoWidth - halfW);
            cameraCenter.Y = Math.Clamp(cameraCenter.Y, halfH, videoHeight - halfH);

            var x = (int)Math.Round(cameraCenter.X - halfW);
            var y = (int)Math.Round(cameraCenter.Y - halfH);

            x = Math.Clamp(x, 0, videoWidth - originalCropWidth);
            y = Math.Clamp(y, 0, videoHeight - originalCropHeight);

            var roi = new Rect(x, y, originalCropWidth, originalCropHeight);

            if (debugOverlay)
            {
                // overlays always drawn on frame
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
                DrawText(frame, $"Reacquire: {reacquireCounter}", 20, 100, Scalar.White);
                DrawText(frame, $"Noise: {kalman.CurrentNoise:F3}", 20, 130, Scalar.White);
                DrawText(frame, $"Camera: {cameraCenter.X:F1},{cameraCenter.Y:F1}", 20, 160, Scalar.White);
            }

            if (debugOverlay)
            {
                // DEBUG MODE: write FULL FRAME with overlays
                // Ensure contiguous buffer by copying into preallocated outputBgr
                frame.CopyTo(outputBgr);

                Marshal.Copy(outputBgr.Data, videoBuffer, 0, frameBytes);
                stdin.Write(videoBuffer, 0, frameBytes);
            }
            else
            {
                // PRODUCTION MODE: actual crop
                using var cropped = new Mat(frame, roi);
                cropped.CopyTo(outputBgr);

                Marshal.Copy(outputBgr.Data, videoBuffer, 0, frameBytes);
                stdin.Write(videoBuffer, 0, frameBytes);
            }

            var elapsed         = DateTime.UtcNow - startTime;
            var progress        = (double)i / totalFrames;
            var speed           = i > 0 ? i / elapsed.TotalSeconds : 0.0;
            var remainingFrames = totalFrames - i;
            var etaSeconds      = speed > 0 ? remainingFrames / speed : 0;
            var eta             = TimeSpan.FromSeconds(etaSeconds);

            DrawProgress(progress, eta, speed);
        }

        stdin.Flush();
        stdin.Close();

        await ffmpeg.WaitForExitAsync();
        if (ffmpeg.ExitCode != 0)
            throw new Exception("FFmpeg NVENC encoding failed");
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
                    Console.WriteLine($"[ffmpeg] {line}");
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
