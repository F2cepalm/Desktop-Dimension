using System.Windows;

namespace InfiniteSpace.Rendering;

/// <summary>
/// Makes a resize wobble like jelly (the Dreamy theme's resizes; see <see cref="Spaces.SpaceWindow.ReshapeLive"/>).
/// The edges a resize moved take their new place at once, recoil a
/// little, then spring back out past it and settle, each swing smaller than the last; it is all over
/// within <see cref="Longest"/>. Moves just carry it along. It keeps its volume while it wobbles:
/// stretched wider than at rest it gets a little shorter and thins in the middle, squashed it bulges.
/// </summary>
/// <remarks>
/// Works on the shape's bounds: <see cref="Rest"/> is where it comes to rest (the bounds it was last
/// given), <see cref="Shown"/> where the wobble has them now, and <see cref="Map"/> takes a point of
/// the shape at rest to where it is shown.
/// </remarks>
internal sealed class Jelly
{
    /// <summary>Angular frequency of the spring (rad/s): about five swings a second.</summary>
    public const double Stiffness = 30;

    /// <summary>Damping ratio: each swing is about a third of the one before, two or three visible in all.</summary>
    public const double Damping = 0.3;

    /// <summary>How far an edge recoils, as a part of how far the resize moved it.</summary>
    public const double Recoil = 0.4;

    /// <summary>
    /// Resizes smaller than this (DIPs, both edges of an axis together) don't wobble yet; they add up
    /// until they do. Also keeps a moving block, whose edges snap to the art-pixel grid, from jittering.
    /// </summary>
    public const double Threshold = 2.5;

    /// <summary>How much the other axis gives when one is stretched (volume kept).</summary>
    public const double Squash = 0.6;

    /// <summary>How much the middle of the sides thins or bulges while stretched or squashed.</summary>
    public const double Bulge = 1.5;

    /// <summary>The longest a wobble lasts after the last resize, in seconds.</summary>
    public const double Longest = 0.5;

    /// <summary>The largest recoil or overshoot of an edge: a part of the size, and no more than this many DIPs.</summary>
    private const double MostSwing = 0.25, MostSwingDips = 40;

    /// <summary>The first (largest) swing of the spring, per unit of speed it is kicked with.</summary>
    private static readonly double SwingPerSpeed = FirstSwing();

    private readonly double[] _offset = new double[4]; // shown minus rest, per edge: left, top, right, bottom
    private readonly double[] _speed = new double[4];
    private Rect _rest;
    private Rect _kicked; // the rest as of the last kick: resizes since then add up until they are worth a wobble
    private bool _hasRest;
    private double _sinceKick;

    /// <summary>Whether it is wobbling (otherwise it is at rest: <see cref="Shown"/> is <see cref="Rest"/>).</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Where it comes to rest: the bounds it was last given.</summary>
    public Rect Rest => _rest;

    /// <summary>Where the wobble has the bounds now.</summary>
    public Rect Shown
    {
        get
        {
            if (!IsMoving)
                return _rest;
            double left = _rest.Left + _offset[0], top = _rest.Top + _offset[1];
            double right = _rest.Right + _offset[2], bottom = _rest.Bottom + _offset[3];
            double width = Math.Max(1, right - left), height = Math.Max(1, bottom - top);
            double stretchX = width / _rest.Width - 1, stretchY = height / _rest.Height - 1;
            // Its volume is kept: wider than at rest, it is a little shorter, and the other way round.
            double shownWidth = Math.Max(1, width * (1 - Squash * stretchY));
            double shownHeight = Math.Max(1, height * (1 - Squash * stretchX));
            double centreX = (left + right) / 2, centreY = (top + bottom) / 2;
            return new Rect(centreX - shownWidth / 2, centreY - shownHeight / 2, shownWidth, shownHeight);
        }
    }

    /// <summary>
    /// The bounds to rest at changed. The edges a resize moved recoil and wobble; edges that moved
    /// together (a move) carry the shape along without a wobble.
    /// </summary>
    /// <param name="wobble">Whether a resize may wobble (false while the system turns animations off).</param>
    public void Retarget(Rect rest, bool wobble = true)
    {
        if (!_hasRest || !wobble)
        {
            Stop();
            _rest = _kicked = rest;
            _hasRest = true;
            return;
        }

        bool alongX = Resized(_kicked.Left, _kicked.Right, rest.Left, rest.Right, out double carriedX, out double leftMoved, out double rightMoved);
        bool alongY = Resized(_kicked.Top, _kicked.Bottom, rest.Top, rest.Bottom, out double carriedY, out double topMoved, out double bottomMoved);
        double left = _kicked.Left + carriedX, right = _kicked.Right + carriedX;
        double top = _kicked.Top + carriedY, bottom = _kicked.Bottom + carriedY;
        if (alongX)
        {
            Kick(0, leftMoved);
            Kick(2, rightMoved);
            (left, right) = (rest.Left, rest.Right);
        }
        if (alongY)
        {
            Kick(1, topMoved);
            Kick(3, bottomMoved);
            (top, bottom) = (rest.Top, rest.Bottom);
        }
        _kicked = new Rect(new Point(left, top), new Point(Math.Max(left, right), Math.Max(top, bottom)));
        _rest = rest;
        if (alongX || alongY)
        {
            _sinceKick = 0;
            IsMoving = true;
        }
        Limit();
    }

    /// <summary>
    /// Every edge is pushed out by about <paramref name="dips"/> and springs back (something landed in
    /// the shape), within <see cref="Longest"/>; the rest stays where it is.
    /// </summary>
    public void Poke(double dips)
    {
        if (!_hasRest)
            return;
        double speed = dips / SwingPerSpeed;
        _speed[0] -= speed; // left and top outwards: towards smaller values
        _speed[1] -= speed;
        _speed[2] += speed;
        _speed[3] += speed;
        _sinceKick = 0;
        IsMoving = true;
        Limit();
    }

    /// <summary>The shape moved without the wobble noticing (everything shifted as it is): the rest goes along.</summary>
    public void Shift(Vector by)
    {
        _rest.Offset(by);
        _kicked.Offset(by);
    }

    /// <summary>Moves the wobble on by <paramref name="seconds"/>; it comes to rest within <see cref="Longest"/> of the last resize.</summary>
    public void Step(double seconds)
    {
        if (!IsMoving)
            return;
        seconds = Math.Clamp(seconds, 0, 0.05);
        _sinceKick += seconds;
        if (_sinceKick >= Longest)
        {
            Stop();
            return;
        }

        const int substeps = 4;
        double h = seconds / substeps;
        for (int step = 0; step < substeps; step++)
        {
            for (int i = 0; i < 4; i++)
            {
                _speed[i] += (-Stiffness * Stiffness * _offset[i] - 2 * Damping * Stiffness * _speed[i]) * h;
                _offset[i] += _speed[i] * h;
            }
        }

        bool resting = true;
        for (int i = 0; i < 4; i++)
            resting &= Math.Abs(_offset[i]) < 0.05 && Math.Abs(_speed[i]) < 1;
        if (resting)
            Stop();
    }

    /// <summary>At rest now, wherever the wobble was.</summary>
    public void Stop()
    {
        Array.Clear(_offset);
        Array.Clear(_speed);
        IsMoving = false;
    }

    /// <summary>Forgets the shape: the next bounds it is given start it afresh, without a wobble.</summary>
    public void Reset()
    {
        Stop();
        _hasRest = false;
    }

    /// <summary>
    /// Where a point of the shape at rest is shown now: stretched with the bounds, and pulled in (or
    /// pushed out) at the middle of the sides while the shape is stretched (or squashed); the corners
    /// of the bounds stay on them.
    /// </summary>
    /// <param name="shown">The bounds as shown now (<see cref="Shown"/>, worked out once for all the points).</param>
    public Point Map(Point atRest, Rect shown)
    {
        double u = (atRest.X - _rest.X) / _rest.Width, v = (atRest.Y - _rest.Y) / _rest.Height;
        double x = shown.X + u * shown.Width, y = shown.Y + v * shown.Height;
        double stretchX = shown.Width / _rest.Width - 1, stretchY = shown.Height / _rest.Height - 1;
        double a = Math.Clamp(2 * u - 1, -1, 1), b = Math.Clamp(2 * v - 1, -1, 1);
        x -= Bulge * stretchY * shown.Width / 2 * a * Math.Cos(Math.PI * b / 2);
        y -= Bulge * stretchX * shown.Height / 2 * b * Math.Cos(Math.PI * a / 2);
        return new Point(x, y);
    }

    /// <summary>
    /// Along one axis: how far its two edges moved since the last kick, beyond what they moved
    /// together (which only carries the shape along).
    /// </summary>
    /// <returns>Whether that adds up to a resize worth a wobble.</returns>
    private static bool Resized(double low, double high, double newLow, double newHigh, out double carried, out double lowMoved, out double highMoved)
    {
        double dLow = newLow - low, dHigh = newHigh - high;
        carried = Math.Sign(dLow) == Math.Sign(dHigh) ? (Math.Abs(dLow) < Math.Abs(dHigh) ? dLow : dHigh) : 0;
        lowMoved = dLow - carried;
        highMoved = dHigh - carried;
        return Math.Abs(lowMoved) + Math.Abs(highMoved) > Threshold;
    }

    /// <summary>An edge moved <paramref name="moved"/> DIPs: it is sent back the other way, to recoil a part of that.</summary>
    private void Kick(int edge, double moved) => _speed[edge] -= moved * Recoil / SwingPerSpeed;

    /// <summary>No swing bigger than a part of the size (a big jump wobbles, but stays a wobble).</summary>
    private void Limit()
    {
        double mostX = Math.Min(MostSwingDips, _rest.Width * MostSwing), mostY = Math.Min(MostSwingDips, _rest.Height * MostSwing);
        for (int i = 0; i < 4; i++)
        {
            double most = i % 2 == 0 ? mostX : mostY;
            _offset[i] = Math.Clamp(_offset[i], -most, most);
            _speed[i] = Math.Clamp(_speed[i], -most / SwingPerSpeed, most / SwingPerSpeed);
        }
    }

    /// <summary>The first swing of a spring kicked from rest with unit speed: (1/ω_d)·e^(−ζωt*)·sin(ω_d t*) at its turning point t*.</summary>
    private static double FirstSwing()
    {
        double root = Math.Sqrt(1 - Damping * Damping);
        double phase = Math.Atan2(root, Damping); // ω_d t* at the turning point
        return Math.Exp(-Damping * phase / root) * Math.Sin(phase) / (Stiffness * root);
    }
}
