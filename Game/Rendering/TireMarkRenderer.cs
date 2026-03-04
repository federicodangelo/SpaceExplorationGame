using System.Numerics;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Records and renders tire-mark trails left by the player vehicle on the planet surface.
/// Marks are captured at the four wheel contact patches whenever the vehicle is moving,
/// and fade to transparent over <see cref="MarkLife"/> seconds.
/// Call <see cref="Update"/> once per frame and <see cref="Render"/> after the terrain
/// layer but before the vehicle layer.
/// </summary>
public class TireMarkRenderer
{
    // ── Config ────────────────────────────────────────────────────────────────

    /// Scale factor that matches VehicleRenderer (VehicleSize=40 / 25).
    private const float VehicleScale = VehicleRenderer.VehicleScale;

    /// Width of each tire-mark strip in world units.
    private const float MarkWidth = 2.0f;

    /// Seconds before a mark segment is fully transparent and removed.
    private const float MarkLife = 4f;

    /// Minimum distance (world units) a wheel must travel before a new segment is recorded.
    private const float MinSegmentLen = 3.5f;

    /// Hard cap on stored segments; oldest are dropped first.
    private const int MaxSegments = 2000;

    /// Vehicle speed (world units/s) below which no marks are produced.
    private const float SpeedThreshold = 5f;

    /// Peak alpha of a freshly placed mark.
    private const byte AlphaMax = 60;

    // ── Per-segment data ──────────────────────────────────────────────────────

    private struct Segment
    {
        /// Pre-computed world-space quad corners (avoids redundant math at render time).
        public Vector2 A, B, C, D;
        public float Age;
        public float MaxAge;
    }

    private readonly List<Segment> _segs = [];

    // Last recorded contact-patch world position per wheel (null = gap / not tracking).
    private Vector2? _lastFL, _lastFR, _lastRL, _lastRR;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances mark ages, removes fully-faded segments, and appends new segments from
    /// the current wheel contact-patch positions.
    /// </summary>
    /// <param name="dt">Frame delta-time in seconds.</param>
    /// <param name="vehicleWorldPos">Vehicle centre in world space.</param>
    /// <param name="vehicleRotation">Vehicle heading in degrees (same convention as Transform.Rotation).</param>
    /// <param name="isActive">True while the player is actually driving (mounted and not in a menu).</param>
    /// <param name="speed">Current linear speed of the vehicle (world units / second).</param>
    public void Update(float dt, Vector2 vehicleWorldPos, float vehicleRotation,
                       bool isActive, float speed)
    {
        // Age existing segments and cull fully-faded ones.
        for (int i = _segs.Count - 1; i >= 0; i--)
        {
            var seg = _segs[i];
            seg.Age += dt;
            if (seg.Age >= seg.MaxAge)
                _segs.RemoveAt(i);
            else
                _segs[i] = seg;
        }

        if (!isActive || speed < SpeedThreshold)
        {
            // Break tracking continuity so the next movement starts fresh segments.
            _lastFL = _lastFR = _lastRL = _lastRR = null;
            return;
        }

        // Compute the four wheel contact-patch world positions using the canonical
        // offsets from VehicleRenderer. VehicleRenderer applies (rotation + 90°) so we do the same.
        float s = VehicleScale;
        float rotDeg = vehicleRotation + 90f;
        var fl = VehicleRenderer.WheelLocalFrontLeft;
        var fr = VehicleRenderer.WheelLocalFrontRight;
        var rl = VehicleRenderer.WheelLocalRearLeft;
        var rr = VehicleRenderer.WheelLocalRearRight;

        Vector2 wFL = vehicleWorldPos + WheelOffset(fl.X, fl.Y, s, rotDeg);
        Vector2 wFR = vehicleWorldPos + WheelOffset(fr.X, fr.Y, s, rotDeg);
        Vector2 wRL = vehicleWorldPos + WheelOffset(rl.X, rl.Y, s, rotDeg);
        Vector2 wRR = vehicleWorldPos + WheelOffset(rr.X, rr.Y, s, rotDeg);

        TryAddSegment(ref _lastFL, wFL);
        TryAddSegment(ref _lastFR, wFR);
        TryAddSegment(ref _lastRL, wRL);
        TryAddSegment(ref _lastRR, wRR);
    }

    /// <summary>
    /// Draws all active mark segments; alpha is proportional to remaining lifetime.
    /// Must be called after terrain but before the vehicle is drawn so marks appear
    /// under the vehicle.
    /// </summary>
    public void Render(ISpriteRenderer renderer, Camera camera)
    {
        foreach (var seg in _segs)
        {
            float progress = seg.Age / seg.MaxAge;           // 0 = fresh, 1 = gone
            byte alpha = (byte)((1f - progress) * AlphaMax);
            if (alpha == 0) continue;

            var col = new Color4(20, 16, 10, alpha);         // dark rubber/dirt tint

            var sa = camera.WorldToScreen(seg.A);
            var sb = camera.WorldToScreen(seg.B);
            var sc = camera.WorldToScreen(seg.C);
            var sd = camera.WorldToScreen(seg.D);

            renderer.DrawFilledTriangleScreen(sa.X, sa.Y, sb.X, sb.Y, sc.X, sc.Y, col);
            renderer.DrawFilledTriangleScreen(sa.X, sa.Y, sc.X, sc.Y, sd.X, sd.Y, col);
        }
    }

    /// <summary>Removes all stored segments (e.g. when leaving the planet surface).</summary>
    public void Clear()
    {
        _segs.Clear();
        _lastFL = _lastFR = _lastRL = _lastRR = null;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void TryAddSegment(ref Vector2? last, Vector2 current)
    {
        if (!last.HasValue)
        {
            last = current;
            return;
        }

        Vector2 dir = current - last.Value;
        float len = dir.Length();
        if (len < MinSegmentLen) return;    // too short; accumulate more movement first

        Vector2 normDir = dir / len;
        Vector2 perp = new Vector2(-normDir.Y, normDir.X) * (MarkWidth * 0.5f);

        var seg = new Segment
        {
            A = last.Value - perp,
            B = last.Value + perp,
            C = current + perp,
            D = current - perp,
            Age = 0f,
            MaxAge = MarkLife,
        };

        if (_segs.Count >= MaxSegments)
            _segs.RemoveAt(0);

        _segs.Add(seg);
        last = current;
    }

    /// <summary>
    /// Returns the world-space displacement from the vehicle centre to a wheel contact patch.
    /// <paramref name="lx"/> and <paramref name="ly"/> are local vehicle coordinates
    /// (unscaled); <paramref name="s"/> is the scale factor; <paramref name="rotDeg"/>
    /// is the combined rotation (vehicle heading + 90°) in degrees.
    /// </summary>
    private static Vector2 WheelOffset(float lx, float ly, float s, float rotDeg)
    {
        float r = rotDeg * MathF.PI / 180f;
        float c = MathF.Cos(r);
        float sn = MathF.Sin(r);
        float wx = lx * s;
        float wy = ly * s;
        return new Vector2(wx * c - wy * sn, wx * sn + wy * c);
    }
}
