using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

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

    public async Task ProcessSegment(SingleTask job, CancellationToken token)
    {
        var inputFile   = job.Job.InputFile;
        var outputFile  = job.OutputFileName;
        var start       = job.SegmentStart;
        var length      = job.SegmentLength;
        var videoWidth  = job.Info.Width;
        var videoHeight = job.Info.Height;
        var fps         = job.Info.Fps;
        var bitrate     = job.Info.Bitrate;
        var ffmpegPassthroughParameters = job.Job.Passthrough;

        var name = Path.GetFileNameWithoutExtension(outputFile);

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

        // Processing size (what you crop / feed into enhancer)
        var procWidth  = job.Job.Debug ? videoWidth  : job.Job.Crop.Value.width;
        var procHeight = job.Job.Debug ? videoHeight : job.Job.Crop.Value.height;

        IVideoEnhancer? enhancer = null;

        const int window = 5;

        if (job.Job.Enhance)
        {
            enhancer = new RealBasicVsr2xDmlEnhancer();
            await enhancer.InitializeAsync(procWidth, procHeight, window, token);
        }

        // Encoding size (what FFmpeg encoder expects)
        var encWidth  = enhancer != null ? procWidth  * enhancer.ResolutionMultiplier : procWidth;
        var encHeight = enhancer != null ? procHeight * enhancer.ResolutionMultiplier : procHeight;

        LogInfo($"{name}: src={videoWidth}x{videoHeight} @ {fps:F3}fps, seg=[{start:F3},{length:F3}] proc={procWidth}x{procHeight} enc={encWidth}x{encHeight}");

        var decode = await StartFfmpegDecode(inputFile, start, length, job.Job.Rotate, job.Job.PlainText, token);
        using var decodeStdout = decode.StandardOutput.BaseStream;

        var encode = await StartFfmpegEncode(
            inputFile,
            outputFile,
            start,
            length,
            encWidth,
            encHeight,
            job.Info,
            ffmpegPassthroughParameters,
            job.Job.PlainText,
            token);

        using var encodeStdin = encode.StandardInput.BaseStream;

        // Input: always full frame
        var inBytes  = videoWidth * videoHeight * 3;

        // Output: encoded frame size (may be 4x if enhancement enabled)
        var outBytes = encWidth * encHeight * 3;

        var inBuffer  = new byte[inBytes];
        var outBuffer = new byte[outBytes];

        using var frameMat = new Mat(videoHeight, videoWidth, MatType.CV_8UC3);

        // outMat is processing size (crop), not necessarily encoding size
        using var outMat = new Mat(procHeight, procWidth, MatType.CV_8UC3);

        var kalman = new KalmanTracker();
        var camera = new CameraController(
        videoWidth,
        videoHeight,
        job.Job.Crop.Value.width,
        job.Job.Crop.Value.height,
        kalman,
        job.Job);

        try
        {
            var startTime   = DateTime.UtcNow;
            var totalFrames = (int)Math.Round(length * fps);
            var frameIndex  = 0;
            
            var enhancedOutput = new Mat[window];
            //totalFrames = 10;
            while (frameIndex < totalFrames)
            {
                token.ThrowIfCancellationRequested();

                frameIndex++;

                var read = await ReadExact(decodeStdout, inBuffer, 0, inBytes, token);
                if (read != inBytes)
                    break;

                Marshal.Copy(inBuffer, 0, frameMat.Data, inBytes);

                var objects = _detector.DetectAll(job, frameMat);
                var primary = SelectTrackedObject(objects, kalman.LastMeasurement);

                camera.Update(primary);
                var roi = camera.Roi;

                if (job.Job.Debug)
                {
                    DrawDebug(frameMat, objects, camera, kalman);
                    frameMat.CopyTo(outMat); // outMat: procWidth x procHeight == full frame in debug
                }
                else
                {
                    using var cropped = new Mat(frameMat, roi);
                    cropped.CopyTo(outMat); // outMat: procWidth x procHeight == crop
                }

                Mat frameToWrite = outMat;

                if (enhancer != null)
                {
                    if (enhancer.TryProcessFrame(outMat, out var enhanced, token))
                        frameToWrite = enhanced; // enhanced: encWidth x encHeight
                    else
                        continue;
                }

                Marshal.Copy(frameToWrite.Data, outBuffer, 0, outBytes);
                encodeStdin.Write(outBuffer, 0, outBytes);

                var elapsed         = DateTime.UtcNow - startTime;
                var progress        = totalFrames > 0 ? (double)frameIndex / totalFrames : 0.0;
                var speed           = elapsed.TotalSeconds > 0 ? (frameIndex / elapsed.TotalSeconds) / fps : 0.0;
                var remainingFrames = Math.Max(totalFrames - frameIndex, 0);
                var etaSeconds      = speed > 0 ? remainingFrames / speed : 0.0;
                var eta             = TimeSpan.FromSeconds(etaSeconds);

                DrawProgress(name, progress, eta, speed);
            }

            if (enhancer != null)
            {
                int count = enhancer.Flush(enhancedOutput, token);
                for (int i = 0; i < count; i++)
                {
                    var mat = enhancedOutput[i]; // encWidth x encHeight
                    Marshal.Copy(mat.Data, outBuffer, 0, outBytes);
                    encodeStdin.Write(outBuffer, 0, outBytes);
                }
            }

            encodeStdin.Flush();
            encodeStdin.Close();

            await encode.WaitForExitAsync();
        }
        finally
        {
            if (enhancer is IAsyncDisposable asyncDisp)
                await asyncDisp.DisposeAsync();
            else if (enhancer is IDisposable disp)
                disp?.Dispose();
        }

        try { if (!decode.HasExited) decode.Kill(entireProcessTree: true); } catch { }
        try { if (!decode.HasExited) await decode.WaitForExitAsync(); } catch { }

        ClearProgress(name);

        if (encode.ExitCode != 0)
            LogError($"{name}: FFmpeg encoding failed");
        else
            LogInfo($"{name}: Segment processing completed");
    }


    // ---------- FFmpeg decode / encode ----------

    private async Task<Process> StartFfmpegDecode(string inputFile, double start, double length, int? rotate, bool plainText, CancellationToken token)
    {
        var ss = start .ToString("0.###", CultureInfo.InvariantCulture);
        var t  = length.ToString("0.###", CultureInfo.InvariantCulture);

        var rotateStr = GetRorationArg(rotate);

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

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync(token)) != null)
                    if (plainText)
                        LogInfo($"[ffmpeg-decode] {fileName}: {line}");
            }
            catch { }
        });

        return p;
    }

    public static string GetRorationArg(int? rotate)
    {
        var rotateStr = "";
        if (rotate != null)
        {
            switch (rotate.Value)
            {
                case 90: rotateStr = ",transpose=1"; break;
                case 180: rotateStr = ",transpose=PI"; break;
                case 270: rotateStr = ",transpose=2"; break;
            }
        }

        return rotateStr;
    }

    private async Task<Process> StartFfmpegEncode(
        string inputFile,
        string outputFile,
        double start,
        double length,
        int width, int height,
        VideoInfo info,
        string[] passthrough,
        bool plainText,
        CancellationToken token)
    {
        var pass   = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";
        var fpsStr = info.Fps.ToString("0.###", CultureInfo.InvariantCulture);
        var ss     = start.ToString("0.###", CultureInfo.InvariantCulture);
        var t      = length.ToString("0.###", CultureInfo.InvariantCulture);
        var sarArg = !string.IsNullOrWhiteSpace(info.SampleAspectRatio)
            ? $"-vf setsar={info.SampleAspectRatio} "
            : "";

        var darArg    = "";

        if (info.Sar is { } s)
        {
            // compute DAR from output size and SAR
            var darNum = width * s.X;
            var darDen = height * s.Y;

            // clamp to int and reduce
            var dn = (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, darNum));
            var dd = (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, darDen));
            ReduceFraction(ref dn, ref dd);

            if (dn > 0 && dd > 0)
                darArg = $"-aspect {dn}:{dd} ";
        }

        var args =
            "-y " +
            $"-f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fpsStr} -i - " +
            $"-ss {ss} -i \"{inputFile}\" " +
            "-map 0:v:0 -map 1:a:0? -shortest " +
            "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
            sarArg + darArg +
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

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync(token)) != null)
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

    private static void ReduceFraction(ref int num, ref int den)
    {
        int Gcd(int a, int b)
        {
            while (b != 0)
            {
                var t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        var g = Gcd(Math.Abs(num), Math.Abs(den));
        if (g > 1)
        {
            num /= g;
            den /= g;
        }
    }
    private static async Task<int> ReadExact(Stream s, byte[] buffer, int offset, int count, CancellationToken token)
    {
        var total = 0;
        while (total < count)
        {
            var read = await s.ReadAsync(buffer, offset + total, count - total, token);
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

            for (var i = 0; i < foundObjects.Count; i++)
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

            for (var i = 0; i < foundObjects.Count; i++)
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
