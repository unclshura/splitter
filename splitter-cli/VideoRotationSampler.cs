using OpenCvSharp;
using System.Diagnostics;

namespace splitter;

public sealed class VideoRotationSampler
{
    private readonly FrameRotationDetector _detector = new FrameRotationDetector();

    public static int    RotationDetectorSampleCount  = 20;
    public static double RotationDetectorSampleLength = 0.15;  // seconds to decode per probe
    public static int    RotationDetectorFrameWidth   = 320;
    public static int    RotationDetectorFrameHeight  = 180;

    // --- Zero-allocation buffers ---
    private readonly byte[] _buffer;
    private readonly Mat _frameMat;

    public VideoRotationSampler(SingleJob _master)
    {
        if (_master.Parameters.TryGetValue("RotationDetectorSampleCount", out var s))
            RotationDetectorSampleCount  = int.Parse(s);
        if (_master.Parameters.TryGetValue("RotationDetectorSampleLength", out s))
            RotationDetectorSampleLength = double.Parse(s);
        if (_master.Parameters.TryGetValue("RotationDetectorFrameWidth", out s))
            RotationDetectorFrameWidth   = int.Parse(s);
        if (_master.Parameters.TryGetValue("RotationDetectorFrameHeight", out s))
            RotationDetectorFrameHeight  = int.Parse(s);

        int w = RotationDetectorFrameWidth;
        int h = RotationDetectorFrameHeight;

        _buffer    = new byte[w * h * 3];                  // raw BGR24 buffer
        _frameMat  = new Mat(h, w, MatType.CV_8UC3);       // wraps buffer
    }

    public async Task<int> DetectRotationAsync(
         string inputFile,
         double videoLengthSeconds)
    {
        if (videoLengthSeconds <= 0)
            return 0;

        var rotations = new List<int>();

        for (int i = 0; i < RotationDetectorSampleCount; i++)
        {
            double t = videoLengthSeconds * (i + 1) / (RotationDetectorSampleCount + 1);

            var frame = await DecodeSingleFrameAsync(
                inputFile,
                t,
                RotationDetectorSampleLength,
                RotationDetectorFrameWidth,
                RotationDetectorFrameHeight);

            if (frame != null && !frame.Empty())
            {
                int rot = _detector.GetRotation(frame);
                rotations.Add(rot);
            }
        }

        if (rotations.Count == 0)
            return 0;

        return Majority(rotations);
    }

    private static int Majority(List<int> values)
    {
        var counts = new Dictionary<int, int>();
        foreach (var v in values)
        {
            if (!counts.ContainsKey(v)) counts[v] = 0;
            counts[v]++;
        }

        int best = 0;
        int bestCount = 0;

        foreach (var kv in counts)
        {
            if (kv.Value > bestCount)
            {
                best = kv.Key;
                bestCount = kv.Value;
            }
        }

        return best;
    }

    private async Task<Mat?> DecodeSingleFrameAsync(
        string inputFile,
        double start,
        double length,
        int width,
        int height)
    {
        var p = StartFfmpegDecode(inputFile, start, length, rotate: null, plainText: false);

        int needed = _buffer.Length;
        int read = 0;

        using var stdout = p.StandardOutput.BaseStream;

        while (read < needed)
        {
            int r = await stdout.ReadAsync(_buffer, read, needed - read);
            if (r == 0)
                return null;
            read += r;
        }

        try { p.Kill(); } catch { }

        // Copy buffer → Mat (no new Mat)
        System.Runtime.InteropServices.Marshal.Copy(_buffer, 0, _frameMat.Data, _buffer.Length);

        return _frameMat;
    }

    private Process StartFfmpegDecode(
        string inputFile,
        double start,
        double length,
        int? rotate,
        bool plainText)
    {
        var ss = start.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var t  = length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        // FFmpeg does the resize + format conversion
        var args =
            $"-ss {ss} -t {t} -i \"{inputFile}\" " +
            "-an -sn " +
            $"-vf scale={RotationDetectorFrameWidth}:{RotationDetectorFrameHeight},format=bgr24 " +
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

        // Optional stderr logging
        _ = Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = p.StandardError.ReadLine()) != null)
                    if (plainText)
                        Console.WriteLine($"[ffmpeg-decode] {line}");
            }
            catch { }
        });

        return p;
    }
}
