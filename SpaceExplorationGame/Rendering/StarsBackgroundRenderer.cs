using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders a parallax-scrolling background star field generated with Poisson disk sampling.
/// Stars are distributed uniformly (no clustering), twinkle with per-star phase variation,
/// and vary in color temperature (blue-white, warm yellow, orange-red, plain white) and size.
///
/// Usage:
///   1. Construct with <see cref="StarsBackgroundRenderer(float, ulong, float)"/>.
///   2. Call <see cref="Generate"/> once after world data is available to build the star cache.
///   3. Call <see cref="Render"/> every frame before drawing world geometry.
/// </summary>
public class StarsBackgroundRenderer
{
    // ── Twinkling ────────────────────────────────────────────────────────────

    // Twinkle is a sinusoidal brightness multiplier in the range [TwinkleMin, TwinkleMin+TwinkleRange].
    private const float TwinkleMin = 0.7f;
    private const float TwinkleRange = 0.3f;
    private const float TwinkleSpeedBase = 1.0f;
    private const float TwinkleSpeedStep = 0.4f; // per slot (0-7)

    // ── Configuration ────────────────────────────────────────────────────────

    private readonly float _parallaxFactor;

    // ── Cache ────────────────────────────────────────────────────────────────

    private Vector2[]? _starPositions;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="StarsBackgroundRenderer"/>.
    /// </summary>
    /// <param name="parallaxFactor">
    ///   Fraction of camera movement applied to star displacement (e.g. 0.08 → 8 %).
    ///   Smaller values make stars feel more distant.  Zoom-independent.
    /// </param>
    public StarsBackgroundRenderer(float parallaxFactor)
    {
        _parallaxFactor = parallaxFactor;
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates (and caches) star positions covering the supplied camera roaming bounds.
    /// The generation area is automatically expanded so every screen position is covered
    /// no matter where the camera is inside <c>[camX0,camX1] × [camY0,camY1]</c>.
    /// </summary>
    /// <param name="fromX">Minimum expected camera X world position.</param>
    /// <param name="fromY">Minimum expected camera Y world position.</param>
    /// <param name="toX">Maximum expected camera X world position.</param>
    /// <param name="toY">Maximum expected camera Y world position.</param>
    /// <param name="seed">Seed for deterministic Poisson disk generation.</param>
    /// <param name="minDist">
    ///   Minimum distance between star positions in the same coordinate space as
    ///   <c>camera.Position</c>.  Larger values → sparser star field.
    /// </param>
    /// <param name="filter">
    ///   Optional predicate applied after sampling; points for which it returns
    ///   <c>false</c> are discarded.  Use it to exclude stars inside a planet disc, etc.
    /// </param>
    public void Generate(float fromX, float fromY, float toX, float toY,
        ulong seed, float minDist = 300f, Predicate<Vector2>? filter = null)
    {
        // Expand the generation region so stars appear at screen edges even when the
        // camera is at the extremes of its roaming area.
        //   sx = screenCX + (wx - camX) * parallax
        //   Star visible when |wx - camX| * parallax <= screenHalf
        //   ⟹ wx ∈ [ camX - screenHalf/parallax,  camX + screenHalf/parallax ]
        float screenHalfW = GameConfig.WindowWidth * 0.5f / _parallaxFactor;
        float screenHalfH = GameConfig.WindowHeight * 0.5f / _parallaxFactor;

        float x0 = fromX - screenHalfW;
        float y0 = fromY - screenHalfH;
        float x1 = toX + screenHalfW;
        float y1 = toY + screenHalfH;

        var rng = new SeededRandom(seed);
        var samples = PoissonDiskSampler.Sample(rng, x0, y0, x1, y1, minDist: minDist);

        if (filter != null)
        {
            var filtered = new List<Vector2>(samples.Count);
            foreach (var p in samples)
                if (filter(p)) filtered.Add(p);
            _starPositions = filtered.ToArray();
        }
        else
        {
            _starPositions = samples.ToArray();
        }
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders all cached background stars in screen space using parallax projection.
    /// Stars are zoom-independent (they represent infinitely distant objects).
    /// Must be called after <see cref="Generate"/> and before world geometry is drawn.
    /// </summary>
    public void Render(ISpriteRenderer renderer, Camera camera, float globalTime, float brightnessMultiplier = 0.5f)
    {
        if (_starPositions == null) return;

        float screenCX = camera.ViewportOffsetX + camera.ViewportWidth * 0.5f;
        float screenCY = camera.ViewportOffsetY + camera.ViewportHeight * 0.5f;
        float screenW = camera.ViewportWidth;
        float screenH = camera.ViewportHeight;

        for (int i = 0; i < _starPositions.Length; i++)
        {
            float wx = _starPositions[i].X;
            float wy = _starPositions[i].Y;

            // Parallax: stars shift at a fraction of the camera velocity, ignoring zoom.
            float sx = screenCX + (wx - camera.Position.X) * _parallaxFactor;
            float sy = screenCY + (wy - camera.Position.Y) * _parallaxFactor;

            if (sx < -4 || sx > screenW + 4 || sy < -4 || sy > screenH + 4) continue;

            // Deterministic per-star hash drives all visual variation.
            int hash = (int)((uint)(wx * 7.3f) * 374761393u ^ (uint)(wy * 7.3f) * 668265263u);

            // Twinkle: sinusoidal brightness pulse with a unique phase and speed per star.
            float phase = ((hash >> 4) & 0xFF) / 255f * MathF.PI * 2f;
            float twinkleSpeed = TwinkleSpeedBase + ((hash >> 8) & 0x7) * TwinkleSpeedStep;
            float twinkle = TwinkleMin + TwinkleRange * (MathF.Sin(globalTime * twinkleSpeed + phase) * 0.5f + 0.5f);

            byte brightness = (byte)(100 + ((hash >> 16) & 0x7F));
            byte b = (byte)Math.Clamp((int)(brightness * twinkle * brightnessMultiplier), 30, 255);

            // Color temperature: blue-white, warm yellow, orange-red, or plain white.
            int colorType = (hash >> 11) & 0x7;
            var color = colorType switch
            {
                0 => new Color3(b, b, (byte)Math.Min(b + 40, 255)),                            // blue-white
                1 => new Color3((byte)Math.Min(b + 30, 255), (byte)Math.Min(b + 15, 255), b),  // warm yellow
                2 => new Color3((byte)Math.Min(b + 25, 255), (byte)(b * 0.7f), (byte)(b * 0.6f)), // orange-red
                _ => new Color3(b, b, b)                                                        // white (most common)
            };

            // Size: mostly 1 px, occasionally 2, rarely 3.
            int sizeClass = (hash >> 14) & 0xF;
            int starSize = sizeClass < 2 ? 1 : sizeClass < 4 ? 3 : 2;

            renderer.DrawRectScreen(sx - starSize * 0.5f, sy - starSize * 0.5f,
                starSize, starSize, color);

            // Prominent stars (bright + large) get a soft cross-shaped glow.
            if (brightness > 120 && starSize >= 2)
            {
                byte glowA = (byte)(b * 0.25f);
                renderer.DrawRectScreen(sx - 0.5f, sy - 3, 1, 7,
                    new Color4(color.R, color.G, color.B, glowA));
                renderer.DrawRectScreen(sx - 3, sy - 0.5f, 7, 1,
                    new Color4(color.R, color.G, color.B, glowA));
            }
        }
    }
}
