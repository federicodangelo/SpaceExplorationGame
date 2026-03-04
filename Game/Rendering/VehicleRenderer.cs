using System.Numerics;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player's ground vehicle (top-down view) using layered primitives.
/// Local space: -Y = front/nose, +Y = rear, X = left/right.
/// All unit coordinates are multiplied by the scale factor (VehicleSize / 20).
/// Layers are drawn back-to-front: shadow → underbody → body panels → cabin →
/// fender arches → wheels → lights → details.
/// </summary>
public class VehicleRenderer
{
    private const float VehicleSize = 40f;

    public const float VehicleScale = VehicleSize / 25f;

    // ── Wheel local-space offsets (unscaled unit coords, +Y = rear) ──────────
    public static readonly Vector2 WheelLocalFrontLeft = new(-8f, -7f);
    public static readonly Vector2 WheelLocalFrontRight = new(8f, -7f);
    public static readonly Vector2 WheelLocalRearLeft = new(-8f, 7f);
    public static readonly Vector2 WheelLocalRearRight = new(8f, 7f);

    public VehicleRenderer()
    {
    }

    /// <summary>Renders the vehicle and optional parked label.</summary>
    /// <param name="steerAngle">Front-wheel steering angle in degrees (negative = left, positive = right).</param>
    public void Render(ISpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, bool isMounted, float steerAngle = 0f)
    {
        DrawVehiclePrimitives(renderer, camera, position, rotation + 90f, steerAngle);
        if (!isMounted)
            renderer.DrawText(camera, position + new Vector2(-20, 14), "VEHICLE", new Color3(180, 160, 100));
    }

    private static void DrawVehiclePrimitives(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, float rot, float steerAngle = 0f)
    {
        float s = VehicleScale;

        // ── Palette ──────────────────────────────────────────────────────────
        var shadowCol = new Color4(0, 0, 0, 50);
        var underbodyCol = new Color4(32, 27, 22, 255);  // chassis floor (dark, shows under overhangs)
        var bodyLit = new Color4(158, 133, 60, 255);  // hood / lit side panels
        var bodyMid = new Color4(130, 110, 50, 255);  // main body
        var bodyDark = new Color4(95, 80, 36, 255);  // trunk / shadowed areas
        var roofCol = new Color4(145, 122, 56, 255);  // cabin roof
        var roofGloss = new Color4(190, 168, 80, 110);  // roof centre gloss strip
        var fenderCol = new Color4(85, 72, 33, 255);  // wheel arch housings
        var fenderEdge = new Color4(170, 148, 60, 150);  // fender outer highlight
        var bumperCol = new Color4(48, 43, 38, 255);  // push bars
        var bumperEdge = new Color4(88, 82, 70, 255);  // bumper face edge
        var grillCol = new Color4(42, 38, 33, 255);  // front grille slot
        var grillBar = new Color4(72, 67, 58, 205);  // grille horizontal bars
        var sillCol = new Color4(58, 50, 24, 255);  // door sill trim
        var windshieldCol = new Color4(85, 148, 205, 215);  // glass
        var windGlint = new Color4(215, 238, 255, 95);  // windshield reflection
        var scoopRim = new Color4(78, 65, 30, 255);  // hood scoop rim
        var scoopVent = new Color4(36, 30, 18, 255);  // hood scoop vent slot
        var exhaustCol = new Color4(40, 36, 30, 255);  // exhaust port hole
        var exhaustGlow = new Color4(255, 135, 35, 65);  // exhaust heat
        var lightBarMnt = new Color4(40, 36, 32, 210);  // light bar mount
        var lightBarLED = new Color4(215, 238, 255, 255);  // light bar LEDs
        var headGlow = new Color4(255, 242, 148, 60);  // headlight bloom
        var headCol = new Color4(255, 255, 200, 255);  // headlight lens
        var signalCol = new Color4(255, 170, 28, 255);  // corner turn signal
        var tailGlow = new Color4(255, 58, 30, 55);  // taillight bloom
        var tailCol = new Color4(255, 60, 45, 255);  // taillight lens

        // ── Ground shadow ─────────────────────────────────────────────────
        renderer.DrawFilledCircle(camera, pos + W(0, 0.8f, s, rot), 10f * s, shadowCol);

        // ── Underbody (full silhouette, darkest base) ─────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-7f, -11f), (7f, -11f), (7f, 11f), (-7f, 11f), underbodyCol);

        // ── Front bumper / push bar ───────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-7.4f, -11.4f), (7.4f, -11.4f), (7.4f, -10.1f), (-7.4f, -10.1f), bumperCol);
        // Face edge highlight
        Quad(renderer, camera, pos, rot, s,
            (-7.4f, -11.4f), (7.4f, -11.4f), (7.4f, -11.1f), (-7.4f, -11.1f), bumperEdge);

        // ── Front grille ──────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-4.8f, -10.5f), (4.8f, -10.5f), (4.8f, -9.9f), (-4.8f, -9.9f), grillCol);
        // Grille bar highlights
        Line(renderer, camera, pos, rot, s, (-3.5f, -10.5f), (-3.5f, -9.9f), grillBar);
        Line(renderer, camera, pos, rot, s, (-1.2f, -10.5f), (-1.2f, -9.9f), grillBar);
        Line(renderer, camera, pos, rot, s, (1.2f, -10.5f), (1.2f, -9.9f), grillBar);
        Line(renderer, camera, pos, rot, s, (3.5f, -10.5f), (3.5f, -9.9f), grillBar);

        // ── Hood (front body section, brightest) ──────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-6.7f, -10f), (6.7f, -10f), (7f, -5.2f), (-7f, -5.2f), bodyLit);
        // Hood centre gloss strip
        Quad(renderer, camera, pos, rot, s,
            (-1.6f, -9.6f), (1.6f, -9.6f), (1.6f, -5.4f), (-1.6f, -5.4f), roofGloss);

        // ── Hood scoop ────────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-1.3f, -8.6f), (1.3f, -8.6f), (1.3f, -6.6f), (-1.3f, -6.6f), scoopRim);
        Quad(renderer, camera, pos, rot, s,
            (-0.75f, -8.2f), (0.75f, -8.2f), (0.75f, -6.9f), (-0.75f, -6.9f), scoopVent);

        // ── Door sills (side trim) ────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-7.2f, -4.5f), (-6.5f, -4.5f), (-6.5f, 5.2f), (-7.2f, 5.2f), sillCol);
        Quad(renderer, camera, pos, rot, s,
            (6.5f, -4.5f), (7.2f, -4.5f), (7.2f, 5.2f), (6.5f, 5.2f), sillCol);

        // ── Main body / cabin sides ───────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-7f, -5.2f), (7f, -5.2f), (7f, 4.4f), (-7f, 4.4f), bodyMid);
        // Top-edge panel highlight
        Quad(renderer, camera, pos, rot, s,
            (-7f, -5.2f), (7f, -5.2f), (7f, -4.3f), (-7f, -4.3f), bodyLit.WithAlpha(140));

        // ── Windshield ────────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-5.2f, -5f), (5.2f, -5f), (4.8f, -2.8f), (-4.8f, -2.8f), windshieldCol);
        // Glint streak (top-left diagonal)
        Tri(renderer, camera, pos, rot, s,
            (-4.6f, -4.9f), (0.8f, -4.9f), (-3.5f, -3.1f), windGlint);

        // ── Roof panel ────────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-4.8f, -2.8f), (4.8f, -2.8f), (4.8f, 3.8f), (-4.8f, 3.8f), roofCol);
        // Roof centre gloss
        Quad(renderer, camera, pos, rot, s,
            (-1.4f, -2.7f), (1.4f, -2.7f), (1.4f, 3.7f), (-1.4f, 3.7f), roofGloss);

        // ── Roof light bar ────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-4.5f, -3.1f), (4.5f, -3.1f), (4.5f, -2.5f), (-4.5f, -2.5f), lightBarMnt);
        // LED dots
        Circ(renderer, camera, pos, rot, s, (-3.3f, -2.8f), 0.45f * s, lightBarLED);
        Circ(renderer, camera, pos, rot, s, (-1.7f, -2.8f), 0.45f * s, lightBarLED);
        Circ(renderer, camera, pos, rot, s, (0f, -2.8f), 0.45f * s, lightBarLED);
        Circ(renderer, camera, pos, rot, s, (1.7f, -2.8f), 0.45f * s, lightBarLED);
        Circ(renderer, camera, pos, rot, s, (3.3f, -2.8f), 0.45f * s, lightBarLED);

        // ── Rear window ───────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-4.8f, 3.8f), (4.8f, 3.8f), (5.2f, 5.3f), (-5.2f, 5.3f), windshieldCol.WithAlpha(175));

        // ── Trunk / rear body ─────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-7f, 4.4f), (7f, 4.4f), (7f, 10.2f), (-7f, 10.2f), bodyDark);
        Quad(renderer, camera, pos, rot, s,
            (-7f, 4.4f), (7f, 4.4f), (7f, 5.4f), (-7f, 5.4f), bodyMid.WithAlpha(155));

        // ── Rear bumper ───────────────────────────────────────────────────
        Quad(renderer, camera, pos, rot, s,
            (-7.4f, 10.1f), (7.4f, 10.1f), (7.4f, 11.4f), (-7.4f, 11.4f), bumperCol);
        Quad(renderer, camera, pos, rot, s,
            (-7.4f, 11.1f), (7.4f, 11.1f), (7.4f, 11.4f), (-7.4f, 11.4f), bumperEdge);

        // ── Exhaust ports ─────────────────────────────────────────────────
        Circ(renderer, camera, pos, rot, s, (-2.8f, 11f), 0.95f * s, exhaustCol);
        Circ(renderer, camera, pos, rot, s, (2.8f, 11f), 0.95f * s, exhaustCol);
        Circ(renderer, camera, pos, rot, s, (-2.8f, 11f), 1.9f * s, exhaustGlow);
        Circ(renderer, camera, pos, rot, s, (2.8f, 11f), 1.9f * s, exhaustGlow);

        // ── Wheel fender arches ───────────────────────────────────────────
        // Front-left arch
        Quad(renderer, camera, pos, rot, s,
            (-9.5f, -9.7f), (-6.7f, -9.7f), (-6.7f, -4.3f), (-9.5f, -4.3f), fenderCol);
        Tri(renderer, camera, pos, rot, s,
            (-9.5f, -9.7f), (-6.7f, -9.7f), (-8.1f, -10.7f), fenderCol);
        Line(renderer, camera, pos, rot, s, (-9.5f, -9.7f), (-9.5f, -4.3f), fenderEdge);
        // Front-right arch
        Quad(renderer, camera, pos, rot, s,
            (6.7f, -9.7f), (9.5f, -9.7f), (9.5f, -4.3f), (6.7f, -4.3f), fenderCol);
        Tri(renderer, camera, pos, rot, s,
            (6.7f, -9.7f), (9.5f, -9.7f), (8.1f, -10.7f), fenderCol);
        Line(renderer, camera, pos, rot, s, (9.5f, -9.7f), (9.5f, -4.3f), fenderEdge);
        // Rear-left arch
        Quad(renderer, camera, pos, rot, s,
            (-9.5f, 4.3f), (-6.7f, 4.3f), (-6.7f, 9.7f), (-9.5f, 9.7f), fenderCol);
        Tri(renderer, camera, pos, rot, s,
            (-9.5f, 9.7f), (-6.7f, 9.7f), (-8.1f, 10.7f), fenderCol);
        Line(renderer, camera, pos, rot, s, (-9.5f, 4.3f), (-9.5f, 9.7f), fenderEdge);
        // Rear-right arch
        Quad(renderer, camera, pos, rot, s,
            (6.7f, 4.3f), (9.5f, 4.3f), (9.5f, 9.7f), (6.7f, 9.7f), fenderCol);
        Tri(renderer, camera, pos, rot, s,
            (6.7f, 9.7f), (9.5f, 9.7f), (8.1f, 10.7f), fenderCol);
        Line(renderer, camera, pos, rot, s, (9.5f, 4.3f), (9.5f, 9.7f), fenderEdge);

        // ── Wheels (drawn over arches) ────────────────────────────────────
        DrawWheel(renderer, camera, pos, rot, WheelLocalFrontLeft.X, WheelLocalFrontLeft.Y, s, steerAngle);   // front-left  (steered)
        DrawWheel(renderer, camera, pos, rot, WheelLocalFrontRight.X, WheelLocalFrontRight.Y, s, steerAngle);   // front-right (steered)
        DrawWheel(renderer, camera, pos, rot, WheelLocalRearLeft.X, WheelLocalRearLeft.Y, s);               // rear-left
        DrawWheel(renderer, camera, pos, rot, WheelLocalRearRight.X, WheelLocalRearRight.Y, s);               // rear-right

        // ── Headlights ────────────────────────────────────────────────────
        Circ(renderer, camera, pos, rot, s, (-5.1f, -11.8f), 2.3f * s, headGlow);
        Circ(renderer, camera, pos, rot, s, (5.1f, -11.8f), 2.3f * s, headGlow);
        Circ(renderer, camera, pos, rot, s, (-5.1f, -11.8f), 1.35f * s, headCol);
        Circ(renderer, camera, pos, rot, s, (5.1f, -11.8f), 1.35f * s, headCol);
        // Corner turn signals
        Circ(renderer, camera, pos, rot, s, (-7.1f, -10.7f), 0.72f * s, signalCol);
        Circ(renderer, camera, pos, rot, s, (7.1f, -10.7f), 0.72f * s, signalCol);

        // ── Taillights ────────────────────────────────────────────────────
        Circ(renderer, camera, pos, rot, s, (-5.1f, 11.8f), 1.9f * s, tailGlow);
        Circ(renderer, camera, pos, rot, s, (5.1f, 11.8f), 1.9f * s, tailGlow);
        Circ(renderer, camera, pos, rot, s, (-5.1f, 11.8f), 1.15f * s, tailCol);
        Circ(renderer, camera, pos, rot, s, (5.1f, 11.8f), 1.15f * s, tailCol);
    }

    /// Top-down wheel: tyre rectangle → tread edge strips → alloy rim → cross-spoke shadows → hub nub.
    /// <param name="steerDeg">Optional steering rotation in degrees around the wheel's own centre.</param>
    private static void DrawWheel(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rot, float lx, float ly, float s, float steerDeg = 0f)
    {
        const float tw = 2.8f;   // tyre width  (X, across vehicle)
        const float tl = 5.0f;   // tyre length (Y, along vehicle axis)
        const float rw = 1.8f;   // rim width
        const float rl = 3.3f;   // rim length

        // Rotate a wheel-local offset around the wheel centre, then offset by wheel centre.
        static (float x, float y) S((float x, float y) pt, float deg, float wx, float wy)
        {
            var v = R(new Vector2(pt.x, pt.y), deg);
            return (wx + v.X, wy + v.Y);
        }

        // Tyre corners in wheel-local space (centred at 0,0), steered then offset
        var ta = S((-tw / 2f, -tl / 2f), steerDeg, lx, ly);
        var tb = S((tw / 2f, -tl / 2f), steerDeg, lx, ly);
        var tc = S((tw / 2f, tl / 2f), steerDeg, lx, ly);
        var td = S((-tw / 2f, tl / 2f), steerDeg, lx, ly);

        // Dark rubber tyre body
        Quad(renderer, camera, center, rot, s, ta, tb, tc, td, new Color4(26, 22, 18, 255));

        // Tread highlight strips along both long edges
        var tl0 = S((-tw / 2f, -tl / 2f), steerDeg, lx, ly);
        var tl1 = S((-tw / 2f + 0.35f, -tl / 2f), steerDeg, lx, ly);
        var tl2 = S((-tw / 2f + 0.35f, tl / 2f), steerDeg, lx, ly);
        var tl3 = S((-tw / 2f, tl / 2f), steerDeg, lx, ly);
        Quad(renderer, camera, center, rot, s, tl0, tl1, tl2, tl3, new Color4(58, 50, 38, 255));

        var tr0 = S((tw / 2f - 0.35f, -tl / 2f), steerDeg, lx, ly);
        var tr1 = S((tw / 2f, -tl / 2f), steerDeg, lx, ly);
        var tr2 = S((tw / 2f, tl / 2f), steerDeg, lx, ly);
        var tr3 = S((tw / 2f - 0.35f, tl / 2f), steerDeg, lx, ly);
        Quad(renderer, camera, center, rot, s, tr0, tr1, tr2, tr3, new Color4(58, 50, 38, 255));

        // Alloy rim face
        var ra = S((-rw / 2f, -rl / 2f), steerDeg, lx, ly);
        var rb = S((rw / 2f, -rl / 2f), steerDeg, lx, ly);
        var rc = S((rw / 2f, rl / 2f), steerDeg, lx, ly);
        var rd = S((-rw / 2f, rl / 2f), steerDeg, lx, ly);
        Quad(renderer, camera, center, rot, s, ra, rb, rc, rd, new Color4(138, 140, 148, 255));

        // Cross-spoke shadows (vertical bar)
        var sv0 = S((-0.28f, -rl / 2f), steerDeg, lx, ly);
        var sv1 = S((0.28f, -rl / 2f), steerDeg, lx, ly);
        var sv2 = S((0.28f, rl / 2f), steerDeg, lx, ly);
        var sv3 = S((-0.28f, rl / 2f), steerDeg, lx, ly);
        Quad(renderer, camera, center, rot, s, sv0, sv1, sv2, sv3, new Color4(65, 63, 68, 230));

        // Cross-spoke shadows (horizontal bar)
        var sh0 = S((-rw / 2f, -0.28f), steerDeg, lx, ly);
        var sh1 = S((rw / 2f, -0.28f), steerDeg, lx, ly);
        var sh2 = S((rw / 2f, 0.28f), steerDeg, lx, ly);
        var sh3 = S((-rw / 2f, 0.28f), steerDeg, lx, ly);
        Quad(renderer, camera, center, rot, s, sh0, sh1, sh2, sh3, new Color4(65, 63, 68, 230));

        // Hub cap (always at wheel centre — steer doesn't shift it)
        Circ(renderer, camera, center, rot, s, (lx, ly), 0.58f * s, new Color4(178, 182, 190, 255));
        Circ(renderer, camera, center, rot, s, (lx, ly), 0.32f * s, new Color4(130, 133, 140, 255));
    }

    // ── Local-space helpers ───────────────────────────────────────────────────

    /// Rotate v by degrees.
    private static Vector2 R(Vector2 v, float deg)
    {
        float r = deg * MathF.PI / 180f;
        float c = MathF.Cos(r), sn = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * sn, v.X * sn + v.Y * c);
    }

    /// Local unit coords → scaled rotated world offset.
    private static Vector2 W(float x, float y, float s, float deg)
        => R(new Vector2(x * s, y * s), deg);

    private static void Tri(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rot, float s,
        (float x, float y) a, (float x, float y) b, (float x, float y) c, Color4 color)
    {
        var s1 = camera.WorldToScreen(center + W(a.x, a.y, s, rot));
        var s2 = camera.WorldToScreen(center + W(b.x, b.y, s, rot));
        var s3 = camera.WorldToScreen(center + W(c.x, c.y, s, rot));
        renderer.DrawFilledTriangleScreen(s1.X, s1.Y, s2.X, s2.Y, s3.X, s3.Y, color);
    }

    private static void Quad(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rot, float s,
        (float x, float y) a, (float x, float y) b, (float x, float y) c, (float x, float y) d, Color4 color)
    {
        Tri(renderer, camera, center, rot, s, a, b, c, color);
        Tri(renderer, camera, center, rot, s, a, c, d, color);
    }

    private static void Circ(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rot, float s, (float x, float y) offset, float worldRadius, Color4 color)
        => renderer.DrawFilledCircle(camera, center + W(offset.x, offset.y, s, rot), worldRadius, color);

    private static void Line(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rot, float s, (float x, float y) a, (float x, float y) b, Color4 color)
        => renderer.DrawLine(camera, center + W(a.x, a.y, s, rot), center + W(b.x, b.y, s, rot), color);

    // Legacy shims kept for API stability.
    private static Vector2 Rotate(Vector2 v, float deg) => R(v, deg);

    private static void DrawRotatedTriangle(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Color4 color)
    {
        var w1 = camera.WorldToScreen(center + Rotate(p1, rotationDeg));
        var w2 = camera.WorldToScreen(center + Rotate(p2, rotationDeg));
        var w3 = camera.WorldToScreen(center + Rotate(p3, rotationDeg));
        renderer.DrawFilledTriangleScreen(w1.X, w1.Y, w2.X, w2.Y, w3.X, w3.Y, color);
    }
}
