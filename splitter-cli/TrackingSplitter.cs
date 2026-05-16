using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace splitter;

public class TrackingSplitter : LoggingBase, ISegmentProcessor, IDisposable
{
    private readonly IObjectDetector _detector;

    public TrackingSplitter(
        int  progressLine,
        IObjectDetector detector,
        SingleJob cmd,
        ILogger logger)
        : base(logger, progressLine)
    {
        _detector     = detector;
    }

    public void Dispose()
    {
        if (_detector is IDisposable d)
            d.Dispose();
    }

    public async Task ProcessSegment(SingleTask job)
    {
        string inputFile                     = job.Job.InputFile;
        string outputFile                    = job.OutputFileName;
        double start                         = job.SegmentStart;
        double length                        = job.SegmentLength;
        int videoWidth                       = job.Info.Width;
        int videoHeight                      = job.Info.Height;
        double fps                           = job.Info.Fps;
        double bitrate                       = job.Info.Bitrate;
        string[] ffmpegPassthroughParameters = job.Job.Passthrough;

        var name = Path.GetFileNameWithoutExtension(outputFile);

        // 1) Probe source video
        if (videoWidth <= 0 || videoHeight <= 0 || fps <= 0)
        {
            LogError($"{name}: ffprobe failed to get metadata");
            return;
        }

        if (job.Job.Crop == null)
        {
            LogError($"{name}: Crop parameters are required");
            return;
        }

        var encWidth  = job.Job.Debug ? videoWidth  : job.Job.Crop.Value.width;
        var encHeight = job.Job.Debug ? videoHeight : job.Job.Crop.Value.height;

        LogInfo($"{name}: src={videoWidth}x{videoHeight} @ {fps:F3}fps, seg=[{start:F3},{length:F3}] enc={encWidth}x{encHeight}");

        // 2) Start FFmpeg decode (video only → raw BGR24 to stdout)
        var decode = StartFfmpegDecode(inputFile, start, length, job.Job.Rotate, job.Job.PlainText);
        using var decodeStdout = decode.StandardOutput.BaseStream;

        // 3) Start FFmpeg encode (video from stdin + audio from original)
        var encode = StartFfmpegEncode(
            inputFile,
            outputFile,
            start,
            length,
            encWidth,
            encHeight,
            fps,
            ffmpegPassthroughParameters,
            job.Job.PlainText);

        using var encodeStdin = encode.StandardInput.BaseStream;

        // Separate input/output sizes and buffers
        var inBytes  = videoWidth * videoHeight * 3;
        var outBytes = encWidth   * encHeight   * 3;

        var inBuffer  = new byte[inBytes];
        var outBuffer = new byte[outBytes];

        using var frameMat = new Mat(videoHeight, videoWidth, MatType.CV_8UC3);
        using var outMat   = new Mat(encHeight, encWidth, MatType.CV_8UC3);

        var kalman = new KalmanTracker();
        var camera = new CameraController(
            videoWidth,
            videoHeight,
            job.Job.Crop.Value.width,
            job.Job.Crop.Value.height,
            kalman,
            job.Job);

        var startTime   = DateTime.UtcNow;
        var totalFrames = (int)Math.Round(length * fps);
        var frameIndex  = 0;

        while (frameIndex < totalFrames)
        {
            frameIndex++;

            var read = ReadExact(decodeStdout, inBuffer, 0, inBytes);
            if (read != inBytes)
                break;

            // input frame → Mat
            Marshal.Copy(inBuffer, 0, frameMat.Data, inBytes);

            var objects = _detector.DetectAll(frameMat);
            var primary = SelectTrackedObject(objects, kalman.LastMeasurement);

            camera.Update(primary);
            var roi = camera.Roi;

            if (job.Job.Debug)
            {
                DrawDebug(frameMat, objects, camera, kalman);
                frameMat.CopyTo(outMat);
            }
            else
            {
                using var cropped = new Mat(frameMat, roi);
                cropped.CopyTo(outMat);
            }

            // output Mat → outBuffer
            Marshal.Copy(outMat.Data, outBuffer, 0, outBytes);
            encodeStdin.Write(outBuffer, 0, outBytes);

            var elapsed         = DateTime.UtcNow - startTime;
            var progress        = totalFrames > 0 ? (double)frameIndex / totalFrames : 0.0;
            var speed           = elapsed.TotalSeconds > 0 ? (frameIndex / elapsed.TotalSeconds) / fps : 0.0;
            var remainingFrames = Math.Max(totalFrames - frameIndex, 0);
            var etaSeconds      = speed > 0 ? remainingFrames / speed : 0.0;
            var eta             = TimeSpan.FromSeconds(etaSeconds);

            DrawProgress(name, progress, eta, speed);
        }

        encodeStdin.Flush();

        // loop finished

        encodeStdin.Flush();
        encodeStdin.Close();          // must happen before waiting encode

        await encode.WaitForExitAsync();

        // belt-and-braces: if decode is still alive, kill it
        try { if (!decode.HasExited) decode.Kill(entireProcessTree: true); } catch { }
        try { if (!decode.HasExited) await decode.WaitForExitAsync(); } catch { }

        ClearProgress();


        if (encode.ExitCode != 0)
            LogError($"{name}: FFmpeg encoding failed");
        else
            LogInfo($"{name}: Segment processing completed");
    }


    // ---------- FFmpeg decode / encode ----------

    private Process StartFfmpegDecode(string inputFile, double start, double length, int? rotate, bool plainText)
    {
        var ss = start .ToString("0.###", CultureInfo.InvariantCulture);
        var t  = length.ToString("0.###", CultureInfo.InvariantCulture);

        var rotateStr = "";
        if (rotate != null)
        {
            switch (rotate.Value)
            {
                case 90:  rotateStr = ",transpose=1";  break;
                case 180: rotateStr = ",transpose=PI"; break;
                case 270: rotateStr = ",transpose=2";  break;
            }
        }

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

        _ = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = p.StandardError.ReadLine()) != null)
                    if (plainText)
                        LogInfo($"[ffmpeg-decode] {fileName}: {line}");
            }
            catch { }
        });

        return p;
    }

    private Process StartFfmpegEncode(
        string inputFile,
        string outputFile,
        double start,
        double length,
        int width,
        int height,
        double fps,
        string[] passthrough,
        bool plainText)
    {
        var pass   = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";
        var fpsStr = fps.ToString("0.###", CultureInfo.InvariantCulture);
        var ss     = start.ToString("0.###", CultureInfo.InvariantCulture);
        var t      = length.ToString("0.###", CultureInfo.InvariantCulture);

        var args =
            "-y " +
            $"-f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fpsStr} -i - " +
            $"-ss {ss} -i \"{inputFile}\" " +
            "-map 0:v:0 -map 1:a:0? -shortest " +
            "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
            "-c:a copy " +
            pass + $" \"{outputFile}\"";

        // "-c:a aac -b:a 192k " +


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

        _ = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = p.StandardError.ReadLine()) != null)
                {
                    if (plainText)
                        LogInfo($"[ffmpeg-encode] {fileName}: {line}");
                }
            }
            catch { }
        });

        return p;
    }

    // ---------- helpers ----------

    private static int ReadExact(Stream s, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = s.Read(buffer, offset + total, count - total);
            if (read <= 0)
                break;
            total += read;
        }
        return total;
    }

    private void DrawDebug(
        Mat frame,
        System.Collections.Generic.List<(Rect box, Point2f center)> objects,
        CameraController camera,
        KalmanTracker kalman)
    {
        if (camera.ObjectBox.HasValue)
        {
            var fb = camera.ObjectBox.Value;
            Cv2.Rectangle(frame, fb, Scalar.LimeGreen, 2);
        }

        Cv2.Circle(frame,
            new Point((int)camera.SmoothedCenter.X, (int)camera.SmoothedCenter.Y),
            6, Scalar.LimeGreen, -1);

        Cv2.Rectangle(frame, camera.Roi,
            camera.ObjectCenter.HasValue ? Scalar.Yellow : Scalar.Red, 3);

        DrawText(frame, $"Faces: {objects.Count}", 20, 40, Scalar.White);
        DrawText(frame, $"LostFrames: {camera.LostFrames}", 20, 70, Scalar.White);
        DrawText(frame, $"Noise: {kalman.CurrentNoise:F3}", 20, 130, Scalar.White);
        DrawText(frame, $"Camera: {camera.CameraCenter.X:F1},{camera.CameraCenter.Y:F1}", 20, 160, Scalar.White);
    }

    private static void DrawText(Mat img, string text, int x, int y, Scalar color)
    {
        Cv2.PutText(img, text, new Point(x, y),
            HersheyFonts.HersheySimplex, 0.6, color, 2);
    }

    private (Rect box, Point2f center)? SelectTrackedObject(
        List<(Rect box, Point2f center)> foundObjects,
        Point2f? previousCenter)
    {
        if (foundObjects == null || foundObjects.Count == 0)
            return null;

        if (!previousCenter.HasValue)
        {
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
            var prev      = previousCenter.Value;
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
}
