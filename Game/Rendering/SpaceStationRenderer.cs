using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders space stations using primitive geometry.
/// </summary>
public class SpaceStationRenderer
{
    const int NumLightsOuterRing = 8;
    const double BlinkPeriod = 2.0; // seconds

    private static Color3 BlinkColor1 = new Color3(255, 40, 40); // red
    private static Color3 BlinkColor2 = new Color3(40, 255, 40); // green

    // Ring + hub
    const float RingInnerRadius = 76.0f;
    const float RingOuterRadius = 96.0f;
    const int RingSegments = 48;
    const float HubOuterRadius = 40f;
    const float HubInnerRadius = 18f;

    // Struts (cross)
    const float StrutInnerRadius = 40f;
    const float StrutOuterRadius = 108f;
    const int StrutThickness = 8;

    // Solar panels
    const float PanelHalfWidth = 14f;
    const float PanelInnerRadius = 96f;
    const float PanelOuterRadius = 118f;

    // Docking markers
    const int DockingMarkerCount = 16;
    const float DockingMarkerTipRadius = 102f;
    const float DockingMarkerBackRadius = 92f;
    const float DockingMarkerHalfWidth = 3f;
    const float DockingMarkerNotchTipRadius = 99f;
    const float DockingMarkerNotchBackRadius = 95f;
    const float DockingMarkerNotchHalfWidth = 1.3f;
    const float DockingPulseBase = 0.8f;
    const float DockingPulseAmplitude = 0.2f;
    const double DockingPulseSpeed = 4.0;
    const float DockingPulsePhaseStep = 0.7f;

    // Light glow
    const float OuterRingLightGlowMultiplier = 4f;
    const float CenterLightGlowMultiplier = 2.5f;
    const byte LightGlowInnerAlpha = 90;
    const float LightGlowTransitionRatio = 0.35f;
    const int LightGlowSegments = 24;

    const float OuterRingRadius = (RingInnerRadius + RingOuterRadius) * 0.5f;
    const float OuterRingLightRadius = 3f;
    const float CenterLightRadius = 8f;

    private static readonly Color4 StrutColor = new(100, 100, 130, 255);
    private static readonly Color4 RingColor = new(145, 150, 195, 255);
    private static readonly Color4 PanelColor = new(50, 70, 160, 255);
    private static readonly Color4 HubOuterColor = new(170, 175, 220, 255);
    private static readonly Color4 HubOutlineColor = new(120, 120, 150, 255);
    private static readonly Color4 HubInnerColor = new(120, 125, 165, 255);
    private static readonly Color4 DockingMarkerWarmBase = new(255, 205, 110, 180);
    private static readonly Color4 DockingMarkerCoolBase = new(120, 190, 255, 170);
    private static readonly Color4 DockingMarkerNotchColor = new(40, 45, 70, 180);

    public SpaceStationRenderer(ITextureManager textures)
    {
        _ = textures;
    }

    /// <summary>Renders all stations with a slowly rotating texture.</summary>
    public void RenderSpaceStations(ISpriteRenderer renderer, Camera camera,
        World ecsWorld, List<Entity> spaceStationEntities, double globalTime)
    {
        foreach (var spaceStation in spaceStationEntities)
        {
            var stTransform = ecsWorld.Get<Transform>(spaceStation);

            RenderSpaceStation(renderer, camera, stTransform.Position, globalTime);
        }
    }

    public void RenderSpaceStation(ISpriteRenderer renderer, Camera camera, Vector2 position, double globalTime, float alpha = 1f)
    {
        float stRotation = (float)(globalTime * 10) % 360f;

        DrawSpaceStationBody(renderer, camera, position, stRotation, globalTime, alpha);

        // Blinking lights overlay (on the outer ring)
        double blinkPhase = globalTime % BlinkPeriod;

        for (int l = 0; l < NumLightsOuterRing; l++)
        {
            float angle = (float)(l * MathF.PI * 2f / NumLightsOuterRing);
            // Rotate with station
            float totalAngle = angle + stRotation * MathF.PI / 180f;
            Vector2 offset = new Vector2(MathF.Cos(totalAngle), MathF.Sin(totalAngle)) * OuterRingRadius;
            Vector2 lightPos = position + offset;

            // Alternate blinking color for each light
            bool blinkState = (l % 2 == 0) ? (blinkPhase < BlinkPeriod / 2) : (blinkPhase >= BlinkPeriod / 2);
            var color = blinkState ? BlinkColor1 : BlinkColor2;
            DrawLightGlow(renderer, camera, lightPos, OuterRingLightRadius * OuterRingLightGlowMultiplier, color, alpha);
            DrawLight(renderer, camera, lightPos, OuterRingLightRadius, ScaleAlpha(color, alpha));
        }

        // Center light
        {
            Vector2 lightPos = position;
            bool blinkState = blinkPhase >= BlinkPeriod / 2;
            var color = blinkState ? BlinkColor1 : BlinkColor2; //Inverted colors for inner ring
            DrawLightGlow(renderer, camera, lightPos, CenterLightRadius * CenterLightGlowMultiplier, color, alpha); // Draw glow
            DrawLight(renderer, camera, lightPos, CenterLightRadius, ScaleAlpha(color, alpha));
        }
    }

    private static void DrawSpaceStationBody(ISpriteRenderer renderer, Camera camera, Vector2 center, float rotationDeg, double globalTime, float alpha = 1f)
    {
        // Struts (cross)
        DrawThickLine(renderer, camera, center, Rotate(new Vector2(0, -StrutInnerRadius), rotationDeg), Rotate(new Vector2(0, -StrutOuterRadius), rotationDeg), StrutThickness, ScaleAlpha(StrutColor, alpha));
        DrawThickLine(renderer, camera, center, Rotate(new Vector2(0, StrutInnerRadius), rotationDeg), Rotate(new Vector2(0, StrutOuterRadius), rotationDeg), StrutThickness, ScaleAlpha(StrutColor, alpha));
        DrawThickLine(renderer, camera, center, Rotate(new Vector2(-StrutInnerRadius, 0), rotationDeg), Rotate(new Vector2(-StrutOuterRadius, 0), rotationDeg), StrutThickness, ScaleAlpha(StrutColor, alpha));
        DrawThickLine(renderer, camera, center, Rotate(new Vector2(StrutInnerRadius, 0), rotationDeg), Rotate(new Vector2(StrutOuterRadius, 0), rotationDeg), StrutThickness, ScaleAlpha(StrutColor, alpha));

        // Outer ring
        renderer.DrawSolidRing(camera, center, RingInnerRadius, RingOuterRadius, ScaleAlpha(RingColor, alpha), RingSegments);

        // Docking indicators on ring
        for (int i = 0; i < DockingMarkerCount; i++)
        {
            // Skip panel-aligned axes (0°, 90°, 180°, 270°) so markers never overlap panels.
            if (i % 4 == 0) continue;

            float a = rotationDeg + i * (360f / DockingMarkerCount);
            float pulse = DockingPulseBase + DockingPulseAmplitude * MathF.Sin((float)(globalTime * DockingPulseSpeed + i * DockingPulsePhaseStep));

            Color4 markerColor = i % 2 == 0
                ? new Color4(DockingMarkerWarmBase.R, DockingMarkerWarmBase.G, DockingMarkerWarmBase.B, (byte)(DockingMarkerWarmBase.A * pulse))
                : new Color4(DockingMarkerCoolBase.R, DockingMarkerCoolBase.G, DockingMarkerCoolBase.B, (byte)(DockingMarkerCoolBase.A * pulse));

            DrawDockingMarker(renderer, camera, center, a, ScaleAlpha(markerColor, alpha));
        }

        // Solar panels
        DrawRotatedQuad(renderer, camera, center, rotationDeg,
            new Vector2(-PanelHalfWidth, -PanelOuterRadius), new Vector2(PanelHalfWidth, -PanelOuterRadius), new Vector2(PanelHalfWidth, -PanelInnerRadius), new Vector2(-PanelHalfWidth, -PanelInnerRadius),
            ScaleAlpha(PanelColor, alpha));
        DrawRotatedQuad(renderer, camera, center, rotationDeg,
            new Vector2(-PanelHalfWidth, PanelInnerRadius), new Vector2(PanelHalfWidth, PanelInnerRadius), new Vector2(PanelHalfWidth, PanelOuterRadius), new Vector2(-PanelHalfWidth, PanelOuterRadius),
            ScaleAlpha(PanelColor, alpha));
        DrawRotatedQuad(renderer, camera, center, rotationDeg,
            new Vector2(-PanelOuterRadius, -PanelHalfWidth), new Vector2(-PanelInnerRadius, -PanelHalfWidth), new Vector2(-PanelInnerRadius, PanelHalfWidth), new Vector2(-PanelOuterRadius, PanelHalfWidth),
            ScaleAlpha(PanelColor, alpha));
        DrawRotatedQuad(renderer, camera, center, rotationDeg,
            new Vector2(PanelInnerRadius, -PanelHalfWidth), new Vector2(PanelOuterRadius, -PanelHalfWidth), new Vector2(PanelOuterRadius, PanelHalfWidth), new Vector2(PanelInnerRadius, PanelHalfWidth),
            ScaleAlpha(PanelColor, alpha));

        // Central hub
        renderer.DrawFilledCircle(camera, center, HubOuterRadius, ScaleAlpha(HubOuterColor, alpha));
        renderer.DrawCircle(camera, center, HubOuterRadius, ScaleAlpha(HubOutlineColor, alpha), 40);
        renderer.DrawFilledCircle(camera, center, HubInnerRadius, ScaleAlpha(HubInnerColor, alpha));
    }

    // Draw a soft glow using a single radial gradient circle
    private void DrawLightGlow(ISpriteRenderer renderer, Camera camera, Vector2 position, float radius, Color3 color, float alpha = 1f)
    {
        var inner = new Color4(color.R, color.G, color.B, (byte)(LightGlowInnerAlpha * alpha));
        var outer = new Color4(color.R, color.G, color.B, 0);
        renderer.DrawFilledCircle(camera, position, radius, inner, outer, radius * LightGlowTransitionRatio, LightGlowSegments);
    }

    private void DrawLight(ISpriteRenderer renderer, Camera camera, Vector2 position, float radius, Color4 color)
    {
        renderer.DrawFilledCircle(camera, position, radius, color);
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static void DrawRotatedTriangle(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Color4 color)
    {
        var w1 = center + Rotate(p1, rotationDeg);
        var w2 = center + Rotate(p2, rotationDeg);
        var w3 = center + Rotate(p3, rotationDeg);
        var s1 = camera.WorldToScreen(w1);
        var s2 = camera.WorldToScreen(w2);
        var s3 = camera.WorldToScreen(w3);
        renderer.DrawFilledTriangleScreen(s1.X, s1.Y, s2.X, s2.Y, s3.X, s3.Y, color);
    }

    private static void DrawRotatedQuad(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, Color4 color)
    {
        DrawRotatedTriangle(renderer, camera, center, rotationDeg, p1, p2, p3, color);
        DrawRotatedTriangle(renderer, camera, center, rotationDeg, p1, p3, p4, color);
    }

    private static void DrawDockingMarker(ISpriteRenderer renderer, Camera camera, Vector2 center,
        float angleDeg, Color4 color)
    {
        Vector2 dir = Rotate(new Vector2(1f, 0f), angleDeg);
        Vector2 tan = new Vector2(-dir.Y, dir.X);

        Vector2 tip = center + dir * DockingMarkerTipRadius;
        Vector2 back = center + dir * DockingMarkerBackRadius;

        Vector2 p1 = tip;
        Vector2 p2 = back + tan * DockingMarkerHalfWidth;
        Vector2 p3 = back - tan * DockingMarkerHalfWidth;

        var s1 = camera.WorldToScreen(p1);
        var s2 = camera.WorldToScreen(p2);
        var s3 = camera.WorldToScreen(p3);
        renderer.DrawFilledTriangleScreen(s1.X, s1.Y, s2.X, s2.Y, s3.X, s3.Y, color);

        // Inner notch for a retro "chevron" look.
        Vector2 iTip = center + dir * DockingMarkerNotchTipRadius;
        Vector2 iBack = center + dir * DockingMarkerNotchBackRadius;
        Vector2 iL = iBack + tan * DockingMarkerNotchHalfWidth;
        Vector2 iR = iBack - tan * DockingMarkerNotchHalfWidth;
        var b1 = camera.WorldToScreen(iTip);
        var b2 = camera.WorldToScreen(iL);
        var b3 = camera.WorldToScreen(iR);
        renderer.DrawFilledTriangleScreen(b1.X, b1.Y, b2.X, b2.Y, b3.X, b3.Y, DockingMarkerNotchColor);
    }

    private static Color4 ScaleAlpha(Color4 c, float alpha) =>
        new(c.R, c.G, c.B, (byte)(c.A * alpha));

    private static void DrawThickLine(ISpriteRenderer renderer, Camera camera, Vector2 center,
        Vector2 localStart, Vector2 localEnd, int thickness, Color4 color)
    {
        Vector2 start = center + localStart;
        Vector2 end = center + localEnd;

        Vector2 dir = end - start;
        if (dir.LengthSquared() < 0.001f)
            return;

        Vector2 n = Vector2.Normalize(new Vector2(-dir.Y, dir.X));
        float half = (thickness - 1) * 0.5f;
        for (int i = 0; i < thickness; i++)
        {
            float o = i - half;
            Vector2 off = n * o;
            renderer.DrawLine(camera, start + off, end + off, color);
        }
    }
}
