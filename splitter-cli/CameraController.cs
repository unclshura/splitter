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
    private readonly SingleJob _cmd;
    private int _dropoutCounter;

    // --- Dropout tolerance ---
    private int   _dropoutToleranceFrames = 20;
    private float _emaFactor              = 0.65f; // smoother but responsive
    private float _cameraEasing           = 0.03f; // stronger follow-through
    private int   _lostFreezeFrames       = 60;    // 2 seconds at 30 FPS


    private int        _lostFrames;
    private Point2f    _cameraCenter;
    private TrackState _state;
    private Point2f    _smoothedCenter;
    private Rect?      _objectBox;
    private Point2f?   _objectCenter;
    private Rect       _roi;

    public CameraController(
        int videoWidth,
        int videoHeight,
        int cropWidth,
        int cropHeight,
        KalmanTracker kalman,
        SingleJob cmd
        )
    {
        _videoWidth   = videoWidth;
        _videoHeight  = videoHeight;
        _cropWidth    = cropWidth;
        _cropHeight   = cropHeight;
        _kalman       = kalman;
        _cmd          = cmd;
        _cameraCenter = DefaultCenter;
        _state        = TrackState.Tracking;

    cmd.Override(ref _dropoutToleranceFrames, "DropoutToleranceFrames");
    cmd.Override(ref _emaFactor,              "EmaFactor");
    cmd.Override(ref _cameraEasing,           "CameraEasing");
    cmd.Override(ref _lostFreezeFrames,       "LostFreezeFrames");

        _kalman.Reset(_cameraCenter);
    }

    private Point2f DefaultCenter => _cmd.GravitateTo ?? new Point2f(_videoWidth / 2f, _videoHeight / 2f);

    public int LostFrames         => _lostFrames;
    public Point2f CameraCenter   => _cameraCenter;
    public TrackState State       => _state;
    public Point2f SmoothedCenter => _smoothedCenter;
    public Rect? ObjectBox        => _objectBox;
    public Point2f? ObjectCenter  => _objectCenter;
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

        // ---------------------------------------------------------
        // Dropout tolerance
        // ---------------------------------------------------------
        if (!objectCenter.HasValue)
        {
            if (_dropoutCounter < _dropoutToleranceFrames)
            {
                objectCenter = _kalman.LastMeasurement;
                _dropoutCounter++;
            }
        }
        else
        {
            _dropoutCounter = 0;
        }

        bool isLost = !objectCenter.HasValue;

        // LOST / REACQUIRE STATE MACHINE
        if (isLost)
        {
            _lostFrames++;

            if (_lostFrames <= _lostFreezeFrames)
            {
                _state = TrackState.LostFreeze;
                objectCenter = null;
            }
            else
            {
                _state = TrackState.LostDrift;
                objectCenter = DefaultCenter;
            }
        }
        else
        {
            _state = TrackState.Tracking;
            _lostFrames = 0;
        }

        // KALMAN + CAMERA UPDATE
        Point2f smoothedCenter;

        if (_state == TrackState.Tracking)
        {
            smoothedCenter = _kalman.Update(objectCenter);

            // ---------------------------------------------------------
            // NEW: EMA smoothing
            // ---------------------------------------------------------
            smoothedCenter = new Point2f(
                smoothedCenter.X * (1 - _emaFactor) + _cameraCenter.X * _emaFactor,
                smoothedCenter.Y * (1 - _emaFactor) + _cameraCenter.Y * _emaFactor
            );

            _cameraCenter = new Point2f(
                _cameraCenter.X + (smoothedCenter.X - _cameraCenter.X) * _cameraEasing,
                _cameraCenter.Y + (smoothedCenter.Y - _cameraCenter.Y) * _cameraEasing);

        }
        else if (_state == TrackState.LostFreeze)
        {
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
        _objectBox      = objectBox;
        _objectCenter   = objectCenter;
    }
}
