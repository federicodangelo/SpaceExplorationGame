using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders stars procedurally using layered circles (core + glow).
/// </summary>
public class StarRenderer : IDisposable
{
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
        float phase = (color.R * 0.013f + color.G * 0.007f + color.B * 0.005f);
        float flicker = 0.92f + 0.08f * (0.5f + 0.5f * MathF.Sin(globalTime * 7.2f + phase * 2f));

        // Surrounding glow
        renderer.DrawFilledCircleScreen(x, y, radius * 1.35f,
            new Color4(color.R, color.G, color.B, ScaleAlpha(alpha, MulByte(90, flicker))),
            new Color4(color.R, color.G, color.B, 0),
            radius * 0.45f);

        // Core circle (bright center -> star color)
        renderer.DrawFilledCircleScreen(x, y, radius * 0.75f,
            new Color4(255, 245, 220, alpha),
            new Color4(color.R, color.G, color.B, alpha),
            0f);
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
        float phase = (color.R * 0.013f + color.G * 0.007f + color.B * 0.005f);
        float flicker = 0.92f + 0.08f * (0.5f + 0.5f * MathF.Sin(globalTime * 7.2f + phase * 2f));

        // Surrounding glow
        renderer.DrawFilledCircle(camera, starCenter, starDisplayRadius * 1.35f,
            new Color4(color.R, color.G, color.B, ScaleAlpha(alpha, MulByte(95, flicker))),
            new Color4(color.R, color.G, color.B, 0),
            starDisplayRadius * 0.45f);

        // Core circle (bright center -> star color)
        renderer.DrawFilledCircle(camera, starCenter, starDisplayRadius * 0.75f,
            new Color4(255, 245, 220, alpha),
            new Color4(color.R, color.G, color.B, alpha),
            0f);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
