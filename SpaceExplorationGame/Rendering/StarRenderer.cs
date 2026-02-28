using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders stars procedurally using layered circles (core + glow + corona + rays).
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

    // Corona/ray settings
    private const int RayCount = 12;
    private const float CoronaRadiusMultiplier = 1.8f;
    private const int ProminenceCount = 4;

    public StarRenderer()
    {
    }

    /// <summary>Renders a star at world position with corona and rays.</summary>
    public void Render(ISpriteRenderer renderer, Camera camera,
        Vector2 starCenter, float starDisplayRadius, Color3 color, float globalTime)
    {
        RenderCoronaAndRays(renderer, camera, starCenter, starDisplayRadius, color, globalTime);
        RenderStarWorld(renderer, camera, starCenter, starDisplayRadius, color, 255, globalTime);
        RenderProminences(renderer, camera, starCenter, starDisplayRadius, color, globalTime);
    }

    /// <summary>Renders a star directly in screen space (minimap, etc.).</summary>
    public void RenderScreen(ISpriteRenderer renderer,
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

    /// <summary>Renders animated corona halo and light rays around the star.</summary>
    private void RenderCoronaAndRays(ISpriteRenderer renderer, Camera camera,
        Vector2 center, float radius, Color3 color, float globalTime)
    {
        float flicker = ComputeFlicker(color, globalTime);

        // Outer corona halo (very soft, large)
        float coronaR = radius * CoronaRadiusMultiplier;
        byte coronaAlpha = MulByte(18, flicker);
        renderer.DrawFilledCircle(camera, center, coronaR,
            new Color4(color.R, color.G, color.B, coronaAlpha),
            new Color4(color.R, color.G, color.B, 0),
            coronaR * 0.3f);

        // Second corona layer, slightly offset
        float breathe = 1.0f + 0.05f * MathF.Sin(globalTime * 0.6f);
        renderer.DrawFilledCircle(camera, center, coronaR * 0.85f * breathe,
            new Color4(color.R, color.G, color.B, (byte)(coronaAlpha * 0.7f)),
            new Color4(color.R, color.G, color.B, 0),
            coronaR * 0.25f);

        // Light rays - soft lines radiating outward
        for (int i = 0; i < RayCount; i++)
        {
            float baseAngle = i * MathF.PI * 2f / RayCount;
            // Slow rotation + per-ray wobble
            float wobble = MathF.Sin(globalTime * 0.4f + i * 2.1f) * 0.08f;
            float angle = baseAngle + globalTime * 0.02f + wobble;

            // Ray length varies over time
            float lenPulse = 0.7f + 0.3f * MathF.Sin(globalTime * 0.8f + i * 1.7f);
            float rayLen = radius * (0.8f + lenPulse * 0.6f);
            byte rayAlpha = (byte)(12 + lenPulse * 8);

            var rayStart = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * 0.9f;
            var rayEnd = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (radius + rayLen);

            renderer.DrawLine(camera, rayStart, rayEnd,
                new Color4(color.R, color.G, color.B, rayAlpha));

            // Wider parallel ray for thickness
            var perp = new Vector2(-MathF.Sin(angle), MathF.Cos(angle)) * 2f;
            renderer.DrawLine(camera, rayStart + perp, rayEnd + perp,
                new Color4(color.R, color.G, color.B, (byte)(rayAlpha / 2)));
            renderer.DrawLine(camera, rayStart - perp, rayEnd - perp,
                new Color4(color.R, color.G, color.B, (byte)(rayAlpha / 2)));
        }
    }

    /// <summary>Renders animated solar prominences (arcs near the star surface).</summary>
    private void RenderProminences(ISpriteRenderer renderer, Camera camera,
        Vector2 center, float radius, Color3 color, float globalTime)
    {
        for (int i = 0; i < ProminenceCount; i++)
        {
            float baseAngle = i * MathF.PI * 2f / ProminenceCount + 0.3f;
            float phase = globalTime * 0.3f + i * 1.5f;

            // Prominence appears and disappears cyclically
            float visibility = (MathF.Sin(phase) + 1f) * 0.5f;
            if (visibility < 0.2f) continue;

            float angle = baseAngle + MathF.Sin(globalTime * 0.15f + i) * 0.3f;
            float arcHeight = radius * (0.2f + visibility * 0.25f);

            // Draw arc as a series of small circles along a curved path
            int segments = 6;
            for (int s = 0; s < segments; s++)
            {
                float t = s / (float)(segments - 1);
                float arcAngle = angle + (t - 0.5f) * 0.4f;
                float dist = radius * 1.05f + MathF.Sin(t * MathF.PI) * arcHeight;

                var pos = center + new Vector2(MathF.Cos(arcAngle), MathF.Sin(arcAngle)) * dist;
                byte promAlpha = (byte)(visibility * 35 * MathF.Sin(t * MathF.PI));

                // Bright inner
                byte warmR = (byte)Math.Min(color.R + 60, 255);
                byte warmG = (byte)Math.Min(color.G / 2 + 40, 255);
                renderer.DrawFilledCircle(camera, pos, 3f + visibility * 3f,
                    new Color4(warmR, warmG, 30, promAlpha));
            }
        }
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

    private static void RenderStarWorld(ISpriteRenderer renderer, Camera camera,
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
