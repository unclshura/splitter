using System;
using OpenCvSharp;

namespace splitter;

public enum TrackState
{
    Tracking,
    LostFreeze,
    LostDrift
}

public sealed class CameraController
{
    private readonly int _videoWidth;
    private readonly int _videoHeight;
    private readonly int _cropWidth;
    private readonly int _cropHeight;
    private readonly KalmanTracker _kalman;
    private readonly int _lostFreezeFrames;
    private readonly float _cameraEasing;

    private int _lostFrames;
    private Point2f _cameraCenter;
    private TrackState _state;
    private Point2f _smoothedCenter;
    private Rect? _objectBox;
    private Point2f? _objectCenter;
    private Rect _roi;

    public CameraController(
        int videoWidth,
        int videoHeight,
        int cropWidth,
        int cropHeight,
        KalmanTracker kalman,
        int lostFreezeFrames,
        float cameraEasing)
    {
        _videoWidth = videoWidth;
        _videoHeight = videoHeight;
        _cropWidth = cropWidth;
        _cropHeight = cropHeight;
        _kalman = kalman;
        _lostFreezeFrames = lostFreezeFrames;
        _cameraEasing = cameraEasing;

        _cameraCenter = new Point2f(videoWidth / 2f, videoHeight / 2f);
        _state = TrackState.Tracking;

        _kalman.Reset(_cameraCenter);
    }

    public int LostFrames => _lostFrames;
    public Point2f CameraCenter => _cameraCenter;
    public TrackState State => _state;
    public Point2f SmoothedCenter => _smoothedCenter;
    public Rect? ObjectBox => _objectBox;
    public Point2f? ObjectCenter => _objectCenter;
    public Rect Roi => _roi;

    public void Update((Rect box, Point2f center)? primary)
    {
        Rect?    objectBox    = null;
        Point2f? objectCenter = null;

        if (primary.HasValue)
        {
            objectCenter = primary.Value.center;
            objectBox = primary.Value.box;
        }

        bool isLost = !objectCenter.HasValue;

        // LOST / REACQUIRE STATE MACHINE
        if (isLost)
        {
            _lostFrames++;

            if (_lostFrames <= _lostFreezeFrames)
            {
                // LOST_FREEZE: freeze camera
                _state = TrackState.LostFreeze;
                objectCenter = null; // Kalman predicts but camera won't move
            }
            else
            {
                // LOST_DRIFT: drift camera to center
                _state = TrackState.LostDrift;
                objectCenter = new Point2f(_videoWidth / 2f, _videoHeight / 2f);
            }
        }
        else
        {
            // Object reacquired
            _state = TrackState.Tracking;
            _lostFrames = 0;
        }

        // KALMAN + CAMERA UPDATE
        Point2f smoothedCenter;

        if (_state == TrackState.Tracking)
        {
            smoothedCenter = _kalman.Update(objectCenter);

            // first, faster internal easing (as in your original code)
            float fastEasing = 0.015f;
            _cameraCenter = new Point2f(
                _cameraCenter.X + (smoothedCenter.X - _cameraCenter.X) * fastEasing,
                _cameraCenter.Y + (smoothedCenter.Y - _cameraCenter.Y) * fastEasing);

            // then, external configurable easing
            _cameraCenter = new Point2f(
                _cameraCenter.X + (smoothedCenter.X - _cameraCenter.X) * _cameraEasing,
                _cameraCenter.Y + (smoothedCenter.Y - _cameraCenter.Y) * _cameraEasing);
        }
        else if (_state == TrackState.LostFreeze)
        {
            // Freeze camera — do nothing
            smoothedCenter = _kalman.LastMeasurement ?? _cameraCenter;
        }
        else // LOST_DRIFT
        {
            smoothedCenter = _kalman.Update(objectCenter);

            float driftEasing = 0.01f;
            var fallbackCenter = new Point2f(_videoWidth / 2f, _videoHeight / 2f);

            _cameraCenter = new Point2f(
                _cameraCenter.X + (fallbackCenter.X - _cameraCenter.X) * driftEasing,
                _cameraCenter.Y + (fallbackCenter.Y - _cameraCenter.Y) * driftEasing);
        }

        var halfW = _cropWidth  / 2f;
        var halfH = _cropHeight / 2f;

        smoothedCenter.X = Math.Clamp(smoothedCenter.X, halfW, _videoWidth - halfW);
        smoothedCenter.Y = Math.Clamp(smoothedCenter.Y, halfH, _videoHeight - halfH);

        _cameraCenter.X = Math.Clamp(_cameraCenter.X, halfW, _videoWidth - halfW);
        _cameraCenter.Y = Math.Clamp(_cameraCenter.Y, halfH, _videoHeight - halfH);

        var x = (int)Math.Round(_cameraCenter.X - halfW);
        var y = (int)Math.Round(_cameraCenter.Y - halfH);

        x = Math.Clamp(x, 0, _videoWidth - _cropWidth);
        y = Math.Clamp(y, 0, _videoHeight - _cropHeight);

        _roi = new Rect(x, y, _cropWidth, _cropHeight);
        _smoothedCenter = smoothedCenter;
        _objectBox = objectBox;
        _objectCenter = objectCenter;
    }
}
