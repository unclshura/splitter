using System;
using System.Collections.Generic;
using System.Text;

namespace splitter;

public static class DebugOverlay
{
    public static void DrawDebug(
        Mat frame,
        List<DetectedPerson> objects,
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

    public static void DrawText(Mat img, string text, int x, int y, Scalar color)
    {
        Cv2.PutText(img, text, new Point(x, y),
            HersheyFonts.HersheySimplex, 0.6, color, 2);
    }
}
