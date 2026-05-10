using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace splitter;

public class TrackingSplitter(
    Action<string/*level*/, ConsoleColor /*color*/, string /*message*/> log,
    Action<double /*percent*/, TimeSpan /*duration*/, double /*fps*/> drawProgress
    ) : LoggingBase(log, drawProgress)
{
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
        // ------------------------------
        // OpenCV VideoCapture (stable)
        // ------------------------------
        using var capture = new VideoCapture(srcFileName);
        if (!capture.IsOpened())
            throw new Exception("Cannot open video");

        capture.Set(VideoCaptureProperties.PosMsec, skip.TotalMilliseconds);

        var videoWidth  = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        var videoHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        var fps         = capture.Get(VideoCaptureProperties.Fps);
        var totalFrames = (int)(duration.TotalSeconds * fps);

        if ( debugOverlay )
        {
            cropHeight = videoHeight;
            cropWidth = videoWidth;
        }

        Console.WriteLine($"[FaceTracker] skip={skip}, duration={duration}, fps={fps}, totalFrames={totalFrames}");

        // ------------------------------
        // FFmpeg one-pass encoder
        // ------------------------------
        var ffmpeg = StartFfmpegNvenc(
            srcFileName,
            destFileName,
            cropWidth,
            cropHeight,
            fps,
            skip,
            passthrough);

        using var stdin = ffmpeg.StandardInput.BaseStream;

        // ------------------------------
        // Tracking state
        // ------------------------------
        var frame = new Mat();
        var kalman = new KalmanTracker();
        kalman.Reset(new Point2f(videoWidth / 2f, videoHeight / 2f));

        var lostFrames = 0;
        var wasLost = false;
        var reacquireBoostFrames = 20;
        var reacquireCounter = 0;

        var cameraCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);
        var startTime = DateTime.UtcNow;

        // ------------------------------
        // Main loop
        // ------------------------------
        for (var i = 0; i < totalFrames; i++)
        {
            if (!capture.Read(frame) || frame.Empty())
                break;

            // Ensure continuous memory for detector
            Mat frameCont = frame.IsContinuous() ? frame : frame.Clone();

            Rect? objectBox = null;
            Point2f? objectCenter = null;

            var objects = detector.DetectAll(frameCont, videoWidth, videoHeight); // list of (box, center)

            var primary = SelectTrackedObject(objects, kalman.LastMeasurement);

            if (primary.HasValue)
            {
                objectCenter = primary.Value.center;
                objectBox = primary.Value.box;
            }


            var isLost = !objectCenter.HasValue;

            // LOST OBJECT → drift toward center
            if (isLost)
            {
                lostFrames++;

                var fallbackCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);
                var predicted = kalman.Update(null);

                var t = Math.Min(1f, lostFrames / 60f);
                var ease = 0.02f * t;

                objectCenter = new Point2f(
                    predicted.X * (1 - ease) + fallbackCenter.X * ease,
                    predicted.Y * (1 - ease) + fallbackCenter.Y * ease);
            }
            else
            {
                if (wasLost)
                    reacquireCounter = reacquireBoostFrames;

                lostFrames = 0;
            }

            // SMOOTH REACQUISITION
            if (reacquireCounter > 0)
            {
                var alpha = reacquireCounter / (float)reacquireBoostFrames;
                var noise = 5e-2f + (1e-1f - 5e-2f) * (1 - alpha);
                kalman.SetMeasurementNoise(noise);
                reacquireCounter--;
            }
            else
            {
                kalman.SetMeasurementNoise(1e-1f);
            }

            wasLost = isLost;

            var smoothedCenter = kalman.Update(objectCenter);

            var halfW = cropWidth / 2f;
            var halfH = cropHeight / 2f;

            smoothedCenter.X = Math.Clamp(smoothedCenter.X, halfW, videoWidth - halfW);
            smoothedCenter.Y = Math.Clamp(smoothedCenter.Y, halfH, videoHeight - halfH);

            // CAMERA EASING
            var easing = 0.003f;
            cameraCenter = new Point2f(
                cameraCenter.X + (smoothedCenter.X - cameraCenter.X) * easing,
                cameraCenter.Y + (smoothedCenter.Y - cameraCenter.Y) * easing);

            cameraCenter.X = Math.Clamp(cameraCenter.X, halfW, videoWidth - halfW);
            cameraCenter.Y = Math.Clamp(cameraCenter.Y, halfH, videoHeight - halfH);

            var x = (int)Math.Round(cameraCenter.X - halfW);
            var y = (int)Math.Round(cameraCenter.Y - halfH);

            x = Math.Clamp(x, 0, videoWidth - cropWidth);
            y = Math.Clamp(y, 0, videoHeight - cropHeight);

            var roi = new Rect(x, y, cropWidth, cropHeight);

            if (debugOverlay)
            {
                if (objectBox.HasValue)
                {
                    var fb = objectBox.Value;
                    Cv2.Rectangle(frameCont,
                        new Rect(fb.X, fb.Y, fb.Width, fb.Height),
                        Scalar.LimeGreen, 2);
                }

                Cv2.Circle(frameCont,
                    new Point((int)smoothedCenter.X, (int)smoothedCenter.Y),
                    6, Scalar.LimeGreen, -1);

                Cv2.Rectangle(frameCont, roi,
                    objectCenter.HasValue ? Scalar.Yellow : Scalar.Red, 3);
            }

            // Crop ROI
            using var cropped = new Mat(frameCont, roi);

            // Always clone to ensure contiguous memory
            using var bgr = cropped.Clone();

            // Write to FFmpeg
            var bytes = bgr.Rows * bgr.Cols * bgr.ElemSize();
            var buffer = new byte[bytes];
            Marshal.Copy(bgr.Data, buffer, 0, bytes);
            stdin.Write(buffer, 0, bytes);

            // Dispose frameCont only if it was a clone
            if (!ReferenceEquals(frameCont, frame))
                frameCont.Dispose();

            // Progress
            var elapsed = DateTime.UtcNow - startTime;
            var progress = (double)i / totalFrames;
            var speed = i > 0 ? i / elapsed.TotalSeconds : 0.0;
            var remainingFrames = totalFrames - i;
            var etaSeconds = speed > 0 ? remainingFrames / speed : 0;
            var eta = TimeSpan.FromSeconds(etaSeconds);

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
            // no previous face → pick largest
            return foundObjects
                .OrderByDescending(f => f.box.Width * f.box.Height)
                .First();
        }

        // pick the object closest to previous center
        return foundObjects
            .OrderBy(f =>
            {
                var dx = f.center.X - previousCenter.Value.X;
                var dy = f.center.Y - previousCenter.Value.Y;
                return dx * dx + dy * dy;
            })
            .First();
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
        var pass = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";
        var skipSeconds = skip.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var fpsStr = fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        // One-pass pipeline:
        //   - rawvideo from stdin
        //   - audio from source MP4 (seeked)
        //   - NVENC video encode
        //   - AAC audio copy/encode
        //
        // This is the same structure your original OpenCV pipeline used.
        //
        // IMPORTANT:
        //   Because OpenCV reliably reads the full segment,
        //   FFmpeg will NOT close stdin early anymore.
        //
        var args =
        "-y " +
        // VIDEO INPUT (raw BGR24 from stdin)
        $"-f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fpsStr} -i - " +

        // AUDIO INPUT (seeked)
        $"-ss {skipSeconds} -i \"{srcFileName}\" " +

        // MAP streams
        "-map 0:v:0 -map 1:a:0? -shortest " +

        // VIDEO ENCODE
        "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +

        // AUDIO ENCODE/COPY
        "-c:a aac -b:a 192k " +

        // Extra passthrough flags
        pass + $" \"{destFileName}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi };
        process.Start();

        // async stderr reader
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

}
