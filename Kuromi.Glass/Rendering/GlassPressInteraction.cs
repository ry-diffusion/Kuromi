using System;
using Avalonia;

namespace Kuromi.Glass.Rendering;

/// <summary>
/// Critically-ish damped spring driven press feedback: a press grows the surface slightly and a drag
/// squishes/translates it toward the finger, then it springs back on release. Mirrors the deformation
/// math from AndroidLiquidGlass' LiquidButton.
/// </summary>
internal sealed class GlassPressInteraction
{
    private const double Stiffness = 300.0;
    private const double DampingRatio = 0.5;
    private const double InitialDerivative = 0.05;

    private readonly SpringDouble _progress = new(Stiffness, DampingRatio, 0.001);
    private readonly SpringPoint _position = new(Stiffness, DampingRatio, 0.5);
    private Point _start;

    public double Progress => Math.Clamp(_progress.Value, 0.0, 1.0);
    public Point Position => _position.Value;
    public bool IsPressed { get; private set; }

    public void Press(Point local)
    {
        IsPressed = true;
        _start = local;
        _position.SnapTo(local);
        _progress.Target = 1.0;
    }

    public void MoveTo(Point local)
    {
        _position.SnapTo(local); // position tracks the finger directly during a drag
    }

    public void Release()
    {
        IsPressed = false;
        _progress.Target = 0.0;
        _position.Target = _start;
    }

    /// <summary>Advance the springs. Returns true while still animating.</summary>
    public bool Step(double dt)
    {
        bool moving = _progress.Step(dt);
        if (!IsPressed)
            moving |= _position.Step(dt);
        return moving;
    }

    public Matrix? GetDeformation(double width, double height, double maxScaleDip)
    {
        if (width <= 0 || height <= 0)
            return null;

        double progress = Progress;
        double maxScale = Math.Max(0.0, maxScaleDip);
        double scale = Lerp(1.0, 1.0 + maxScale / height, progress);

        Point offset = _position.Value - _start;
        double minDim = Math.Min(width, height);
        double maxDim = Math.Max(width, height);

        double tx = minDim * Math.Tanh(InitialDerivative * offset.X / minDim);
        double ty = minDim * Math.Tanh(InitialDerivative * offset.Y / minDim);

        double maxDragScale = maxScale / height;
        double angle = Math.Atan2(offset.Y, offset.X);
        double aspectX = Math.Min(width / height, 1.0);
        double aspectY = Math.Min(height / width, 1.0);

        double sx = scale + maxDragScale * Math.Abs(Math.Cos(angle) * offset.X / maxDim) * aspectX;
        double sy = scale + maxDragScale * Math.Abs(Math.Sin(angle) * offset.Y / maxDim) * aspectY;

        if (Math.Abs(tx) < 0.01 && Math.Abs(ty) < 0.01 && Math.Abs(sx - 1.0) < 0.0005 && Math.Abs(sy - 1.0) < 0.0005)
            return null;

        return Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(tx, ty);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0.0, 1.0);

    private sealed class SpringDouble
    {
        private readonly double _stiffness;
        private readonly double _damping;
        private readonly double _threshold;

        public SpringDouble(double stiffness, double dampingRatio, double threshold)
        {
            _stiffness = stiffness;
            _damping = dampingRatio;
            _threshold = threshold;
        }

        public double Value { get; private set; }
        public double Velocity { get; private set; }
        public double Target { get; set; }

        public void SnapTo(double value)
        {
            Value = value;
            Velocity = 0.0;
        }

        public bool Step(double dt)
        {
            double k = _stiffness;
            double c = 2.0 * _damping * Math.Sqrt(k);
            double a = -k * (Value - Target) - c * Velocity;
            Velocity += a * dt;
            Value += Velocity * dt;

            bool done = Math.Abs(Value - Target) <= _threshold && Math.Abs(Velocity) <= _threshold;
            if (done)
            {
                Value = Target;
                Velocity = 0.0;
            }
            return !done;
        }
    }

    private sealed class SpringPoint
    {
        private readonly SpringDouble _x;
        private readonly SpringDouble _y;

        public SpringPoint(double stiffness, double dampingRatio, double threshold)
        {
            _x = new SpringDouble(stiffness, dampingRatio, threshold);
            _y = new SpringDouble(stiffness, dampingRatio, threshold);
        }

        public Point Value => new(_x.Value, _y.Value);
        public Point Target
        {
            get => new(_x.Target, _y.Target);
            set { _x.Target = value.X; _y.Target = value.Y; }
        }

        public void SnapTo(Point value)
        {
            _x.SnapTo(value.X);
            _y.SnapTo(value.Y);
        }

        public bool Step(double dt)
        {
            bool a = _x.Step(dt);
            bool b = _y.Step(dt);
            return a || b;
        }
    }
}
