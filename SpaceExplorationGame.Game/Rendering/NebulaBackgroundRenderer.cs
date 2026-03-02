using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Generates and renders a parallax-scrolling background nebula field.
/// Nebulae are animated with slow drift, pulsing size, and internal wisps.
///
/// Usage:
///   1. Construct with <c>new NebulaBackgroundRenderer()</c>.
///   2. Call <see cref="Generate"/> once after world data is available to build the cloud cache.
///   3. Call <see cref="Render"/> every frame before drawing world geometry.
/// </summary>
public class NebulaBackgroundRenderer
{
    private List<NebulaCloud>? _nebulae;

    // ── Setup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates (and caches) nebula clouds distributed across the given world bounds.
    /// The placement area is extended by half the world size on each side so that
    /// clouds appear beyond the playable boundary for visual depth.
    /// </summary>
    /// <param name="fromX">Minimum world X of the camera roaming area.</param>
    /// <param name="fromY">Minimum world Y of the camera roaming area.</param>
    /// <param name="toX">Maximum world X of the camera roaming area.</param>
    /// <param name="toY">Maximum world Y of the camera roaming area.</param>
    /// <param name="seed">Seed for deterministic generation.</param>
    /// <param name="count">Number of nebula clouds to generate.</param>
    /// <param name="minRadius">Minimum cloud radius. Defaults to 1200.</param>
    /// <param name="maxRadius">Maximum cloud radius. Defaults to 5000.</param>
    public void Generate(float fromX, float fromY, float toX, float toY, ulong seed,
        int count = 32, float minRadius = 1200f, float maxRadius = 5000f)
    {
        float extW = (toX - fromX) * 0.5f;
        float extH = (toY - fromY) * 0.5f;
        float x0 = fromX - extW;
        float y0 = fromY - extH;
        float x1 = toX + extW;
        float y1 = toY + extH;

        // Two deterministic streams: one for colors/radius, one for positions.
        var colorRng = new SeededRandom(seed ^ 0xFACEFEEDuL);
        var posRng = new SeededRandom(seed ^ 0xCAFEBABEuL);

        _nebulae = new List<NebulaCloud>(count);
        for (int i = 0; i < count; i++)
        {
            byte[] choices =
            [
                (byte)colorRng.NextInt(20, 60),
                (byte)colorRng.NextInt(10, 40),
                (byte)colorRng.NextInt(30, 70)
            ];
            int ci = colorRng.NextInt(0, 3);
            float radius = colorRng.NextFloat(minRadius, maxRadius);

            _nebulae.Add(new NebulaCloud(
                posRng.NextFloat(x0, x1),
                posRng.NextFloat(y0, y1),
                radius,
                new Color3(
                    ci == 0 ? choices[0] : (byte)10,
                    ci == 1 ? choices[1] : (byte)10,
                    ci == 2 ? choices[2] : (byte)15)));
        }
    }

    /// <summary>Clears cached nebula data so the next <see cref="Generate"/> call rebuilds it.</summary>
    public void Invalidate() => _nebulae = null;

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders all cached nebula clouds in world space with drift animation and internal structure.
    /// Must be called after <see cref="Generate"/>.
    /// </summary>
    /// <param name="brightnessMultiplier">Scales all cloud alpha values. Default 1.0.</param>
    public void Render(ISpriteRenderer renderer, Camera camera, float globalTime,
        float brightnessMultiplier = 1.0f)
    {
        if (_nebulae == null) return;

        byte A(int baseAlpha) => (byte)Math.Clamp((int)(baseAlpha * brightnessMultiplier), 0, 255);

        foreach (var (nx, ny, nr, nColor) in _nebulae)
        {
            // Slow drift
            float driftX = MathF.Sin(globalTime * 0.03f + nx * 0.001f) * nr * 0.08f;
            float driftY = MathF.Cos(globalTime * 0.025f + ny * 0.001f) * nr * 0.06f;
            var center = new Vector2(nx + driftX, ny + driftY);

            // Pulsing size
            float pulse = 1.0f + 0.04f * MathF.Sin(globalTime * 0.15f + nx * 0.002f);
            float r = nr * pulse;

            // Main cloud layers (layered for depth)
            renderer.DrawFilledCircle(camera, center, r, nColor.WithAlpha(A(18)));
            renderer.DrawFilledCircle(camera,
                center + new Vector2(r * 0.25f, -r * 0.15f),
                r * 0.75f, nColor.WithAlpha(A(14)));
            renderer.DrawFilledCircle(camera,
                center + new Vector2(-r * 0.35f, r * 0.25f),
                r * 0.55f, nColor.WithAlpha(A(12)));

            // Animated interior wisp
            float wispPhase = globalTime * 0.08f + ny * 0.001f;
            float wispX = MathF.Cos(wispPhase) * r * 0.3f;
            float wispY = MathF.Sin(wispPhase * 1.3f) * r * 0.2f;
            renderer.DrawFilledCircle(camera,
                center + new Vector2(wispX, wispY),
                r * 0.3f, nColor.WithAlpha(A(22)));

            // Secondary wisp with slight color shift
            float wisp2Phase = globalTime * 0.06f + nx * 0.0015f;
            float wisp2X = MathF.Sin(wisp2Phase) * r * 0.25f;
            float wisp2Y = MathF.Cos(wisp2Phase * 0.8f) * r * 0.35f;
            byte altR = (byte)Math.Min(nColor.R + 15, 255);
            byte altG = (byte)Math.Min(nColor.G + 10, 255);
            byte altB = (byte)Math.Min(nColor.B + 20, 255);
            renderer.DrawFilledCircle(camera,
                center + new Vector2(wisp2X, wisp2Y),
                r * 0.25f, new Color4(altR, altG, altB, A(16)));
        }
    }
}
