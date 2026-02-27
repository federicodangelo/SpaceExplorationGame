using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders stars procedurally using layered circles (core + glow).
/// </summary>
public class StarRenderer
{
    // Flicker phase weights derived from star color channels
    private const float PhaseWeightR = 0.013f;
    private const float PhaseWeightG = 0.007f;
    private const float PhaseWeightB = 0.005f;
    private const float FlickerBase = 0.92f;
    private const float FlickerAmplitude = 0.08f;
    private const float FlickerSpeed = 7.2f;

    // Glow / core proportions relative to star radius
    private const float GlowRadiusMultiplier = 1.35f;
    private const float GlowTransitionRatio = 0.45f;
    private const float CoreRadiusMultiplier = 0.75f;

    // Glow base alpha differs slightly between screen and world rendering
    private const byte GlowBaseAlphaScreen = 90;
    private const byte GlowBaseAlphaWorld = 95;

    public StarRenderer()
    {
    }

    /// <summary>Renders a star at world position.</summary>
    public void Render(SpriteRenderer renderer, Camera camera,
        Vector2 starCenter, float starDisplayRadius, Color3 color, float globalTime)
    {
        RenderStarWorld(renderer, camera, starCenter, starDisplayRadius, color, 255, globalTime);
    }

    /// <summary>Renders a star directly in screen space.</summary>
    public void RenderScreen(SpriteRenderer renderer,
        float x, float y, float displaySize, Color3 color, byte alpha, float globalTime)
    {
        float radius = displaySize * 0.5f;
        float flicker = ComputeFlicker(color, globalTime);

        // Surrounding glow
        renderer.DrawFilledCircleScreen(x, y, radius * GlowRadiusMultiplier,
            new Color4(color.R, color.G, color.B, ScaleAlpha(alpha, MulByte(GlowBaseAlphaScreen, flicker))),
            new Color4(color.R, color.G, color.B, 0),
            radius * GlowTransitionRatio);

        // Core circle (bright center -> star color)
        renderer.DrawFilledCircleScreen(x, y, radius * CoreRadiusMultiplier,
            RenderColors.StarCoreHighlight.WithAlpha(alpha),
            new Color4(color.R, color.G, color.B, alpha),
            0f);
    }

    private static float ComputeFlicker(Color3 color, float globalTime)
    {
        float phase = color.R * PhaseWeightR + color.G * PhaseWeightG + color.B * PhaseWeightB;
        return FlickerBase + FlickerAmplitude * (0.5f + 0.5f * MathF.Sin(globalTime * FlickerSpeed + phase * 2f));
    }

    private static byte ScaleAlpha(byte baseAlpha, byte layerAlpha)
    {
        return (byte)((baseAlpha * layerAlpha) / 255);
    }

    private static byte MulByte(byte value, float factor)
    {
        return (byte)Math.Clamp((int)(value * factor), 0, 255);
    }

    private static void RenderStarWorld(SpriteRenderer renderer, Camera camera,
        Vector2 starCenter, float starDisplayRadius, Color3 color, byte alpha, float globalTime)
    {
        float flicker = ComputeFlicker(color, globalTime);

        // Surrounding glow
        renderer.DrawFilledCircle(camera, starCenter, starDisplayRadius * GlowRadiusMultiplier,
            new Color4(color.R, color.G, color.B, ScaleAlpha(alpha, MulByte(GlowBaseAlphaWorld, flicker))),
            new Color4(color.R, color.G, color.B, 0),
            starDisplayRadius * GlowTransitionRatio);

        // Core circle (bright center -> star color)
        renderer.DrawFilledCircle(camera, starCenter, starDisplayRadius * CoreRadiusMultiplier,
            RenderColors.StarCoreHighlight.WithAlpha(alpha),
            new Color4(color.R, color.G, color.B, alpha),
            0f);
    }
}
