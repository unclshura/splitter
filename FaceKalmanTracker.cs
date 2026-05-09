namespace splitter;

internal sealed class FaceKalmanTracker
{
    // State vector: [x, y, vx, vy]
    private float[] _state = new float[4];

    // Covariance matrix 4x4
    private float[,] _p = new float[4, 4];

    // Process noise (constant)
    private const float _q = 1e-3f;

    // Measurement noise (dynamic)
    private float _r = 1e-1f;

    // Identity matrix
    private static readonly float[,] _i =
    {
        {1,0,0,0},
        {0,1,0,0},
        {0,0,1,0},
        {0,0,0,1}
    };

    public Point2f? LastMeasurement { get; private set; }

    public void Reset(Point2f initial)
    {
        _state[0] = initial.X;
        _state[1] = initial.Y;
        _state[2] = 0;
        _state[3] = 0;

        // Large initial uncertainty
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                _p[i, j] = (i == j) ? 1f : 0f;
    }

    public void SetMeasurementNoise(float r)
    {
        _r = r;
    }

    public Point2f Update(Point2f? measurement)
    {
        // --- PREDICTION ---
        // State transition:
        // x' = x + vx
        // y' = y + vy
        _state[0] += _state[2];
        _state[1] += _state[3];

        // Update covariance
        AddProcessNoise();

        if (measurement.HasValue)
        {
            // --- MEASUREMENT UPDATE ---
            var z = measurement.Value;

            // Innovation y = z - Hx
            float yx = z.X - _state[0];
            float yy = z.Y - _state[1];

            // Innovation covariance S = P + R
            float Sx = _p[0, 0] + _r;
            float Sy = _p[1, 1] + _r;

            // Kalman gain K = P / S
            float Kx0 = _p[0, 0] / Sx;
            float Kx1 = _p[1, 1] / Sy;

            // Update state
            _state[0] += Kx0 * yx;
            _state[1] += Kx1 * yy;

            // Velocity correction (helps reduce jitter)
            _state[2] += 0.1f * Kx0 * yx;
            _state[3] += 0.1f * Kx1 * yy;

            // Update covariance: P = (I - K)P
            _p[0, 0] *= (1 - Kx0);
            _p[1, 1] *= (1 - Kx1);
        }

        LastMeasurement = measurement;

        return new Point2f(_state[0], _state[1]);
    }

    private void AddProcessNoise()
    {
        // Add small noise to diagonal of covariance
        _p[0, 0] += _q;
        _p[1, 1] += _q;
        _p[2, 2] += _q;
        _p[3, 3] += _q;
    }
}
