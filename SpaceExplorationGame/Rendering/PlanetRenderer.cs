using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders planets and moons procedurally using layered circles.
/// </summary>
public class PlanetRenderer
{
    private readonly Camera _screenSpaceCamera;

    public PlanetRenderer()
    {
        _screenSpaceCamera = new Camera((int)GameConfig.WindowWidth, (int)GameConfig.WindowHeight, 1f, 1f)
        {
            Position = Vector2.Zero,
            Zoom = 1f
        };
    }

    /// <summary>
    /// Renders a single planet/moon body directly in screen space, reusing the same procedural visuals
    /// used in solar-system rendering.
    /// </summary>
    public void RenderBodyScreen(ISpriteRenderer renderer,
        float screenX, float screenY, float radius,
        Color3 color, PlanetType type, bool isMoon, int seed, float globalTime)
    {
        _screenSpaceCamera.Update(GameConfig.WindowWidth, GameConfig.WindowHeight);
        _screenSpaceCamera.Position = Vector2.Zero;
        _screenSpaceCamera.Zoom = 1f;
        _screenSpaceCamera.ViewportOffsetX = screenX - _screenSpaceCamera.ViewportWidth / 2f;
        _screenSpaceCamera.ViewportOffsetY = screenY - _screenSpaceCamera.ViewportHeight / 2f;

        RenderBody(renderer, _screenSpaceCamera, Vector2.Zero, radius, color, type, isMoon, seed, globalTime);

        _screenSpaceCamera.ViewportOffsetX = 0f;
        _screenSpaceCamera.ViewportOffsetY = 0f;
    }

    /// <summary>Renders planets with layered circles, settlement indicators, rings, moon orbits, and moons.</summary>
    public void RenderPlanetsAndMoons(ISpriteRenderer renderer, Camera camera,
        World ecsWorld, List<PlanetData> planets,
        List<Entity> planetEntities, List<List<Entity>> moonEntities,
        float globalTime)
    {
        for (int i = 0; i < planets.Count; i++)
        {
            if (i >= planetEntities.Count) break;
            var pTransform = ecsWorld.Get<Transform>(planetEntities[i]);
            var p = planets[i];

            // Planet body
            RenderBody(renderer, camera, pTransform.Position, p.Radius, p.Color, p.Type, false, p.Index, globalTime);

            // Settlement indicator (small diamond below planet)
            if (p.HasSettlement)
            {
                var indicatorPos = pTransform.Position + new Vector2(0, p.Radius + 15);
                float pulse = 1f + 0.25f * MathF.Sin(globalTime * 3f + p.Index * 0.7f);
                renderer.DrawFilledCircle(camera, indicatorPos, 3f * pulse, new Color4(255, 210, 200, 220));
            }

            // Rings
            if (p.HasRings)
            {
                byte ringAlphaA = (byte)Math.Clamp((int)(120 + 20 * MathF.Sin(globalTime * 1.4f + i)), 0, 255);
                byte ringAlphaB = (byte)Math.Clamp((int)(80 + 18 * MathF.Sin(globalTime * 1.1f + i * 0.8f)), 0, 255);

                float ringAInner = p.Radius * 1.42f;
                float ringAOuter = p.Radius * 1.58f;
                float ringBInner = p.Radius * 1.68f;
                float ringBOuter = p.Radius * 1.92f;

                renderer.DrawSolidRing(camera, pTransform.Position,
                    ringAInner, ringAOuter,
                    p.Color.WithAlpha(ringAlphaA), 48);

                renderer.DrawSolidRing(camera, pTransform.Position,
                    ringBInner, ringBOuter,
                    p.Color.WithAlpha(ringAlphaB), 48);
            }

            // Moon orbit lines
            foreach (var moon in p.Moons)
            {
                byte orbitA = (byte)Math.Clamp((int)(30 + 12 * MathF.Sin(globalTime * 0.9f + moon.Index)), 0, 255);
                renderer.DrawCircle(camera, pTransform.Position, moon.OrbitRadius, new Color4(20, 20, 40, orbitA), 24);
            }

            // Moons
            if (i < moonEntities.Count)
            {
                for (int m = 0; m < moonEntities[i].Count; m++)
                {
                    if (m >= p.Moons.Count) break;
                    var moonTransform = ecsWorld.Get<Transform>(moonEntities[i][m]);
                    var moon = p.Moons[m];
                    int seed = p.Index * 101 + moon.Index * 17 + 7;
                    RenderBody(renderer, camera, moonTransform.Position, moon.Radius, moon.Color, moon.Type, true, seed, globalTime);
                }
            }
        }
    }

    private static void RenderBody(ISpriteRenderer renderer, Camera camera,
        Vector2 center, float radius, Color3 color, PlanetType type, bool isMoon, int seed, float globalTime)
    {
        var baseColor = isMoon ? Mul(color, 0.82f) : color;
        var inner = Lerp(baseColor, new Color3(255, 255, 245), isMoon ? 0.08f : 0.20f);
        var outer = Mul(baseColor, isMoon ? 0.70f : 0.82f);
        float phase = seed * 0.071f;

        // Main sphere gradient
        renderer.DrawFilledCircle(camera, center, radius,
            new Color4(inner.R, inner.G, inner.B, 255),
            new Color4(outer.R, outer.G, outer.B, 255),
            radius * 0.18f);

        // Atmospheric shell for larger planets (helps match transition visuals).
        if (!isMoon && type is PlanetType.Terrestrial or PlanetType.Ocean or PlanetType.GasGiant or PlanetType.IceGiant or PlanetType.Frozen)
        {
            DrawAtmosphereShell(renderer, camera, center, radius, type, globalTime, seed);
        }

        // Type-specific overlays
        switch (type)
        {
            case PlanetType.GasGiant:
                DrawBands(renderer, camera, center, radius, baseColor, seed, 6, 80, globalTime, 0.35f);
                DrawBands(renderer, camera, center, radius,
                    Lerp(baseColor, new Color3(250, 230, 180), 0.22f), seed + 81, 3, 55, globalTime, 0.22f);
                break;
            case PlanetType.IceGiant:
                DrawBands(renderer, camera, center, radius, Lerp(baseColor, new Color3(210, 240, 255), 0.35f), seed, 4, 55, globalTime, 0.28f);
                break;
            case PlanetType.Terrestrial:
                DrawPatches(renderer, camera, center, radius, Lerp(baseColor, new Color3(45, 140, 70), 0.35f), seed, 3, 0.32f, 115, globalTime, 0.20f);
                if (!isMoon)
                {
                    DrawPatches(renderer, camera, center, radius, new Color3(240, 245, 255), seed + 31, 2, 0.22f, 60, globalTime, 0.12f);
                    DrawCloudLayer(renderer, camera, center, radius, seed + 211, globalTime, 0.11f, 38);
                }
                break;
            case PlanetType.Ocean:
                DrawPatches(renderer, camera, center, radius, new Color3(40, 120, 185), seed, 3, 0.34f, 95, globalTime, 0.18f);
                if (!isMoon)
                {
                    DrawPatches(renderer, camera, center, radius, new Color3(230, 245, 255), seed + 19, 2, 0.20f, 70, globalTime, 0.10f);
                    DrawCloudLayer(renderer, camera, center, radius, seed + 173, globalTime, 0.14f, 44);
                }
                break;
            case PlanetType.Desert:
                DrawBands(renderer, camera, center, radius, Lerp(baseColor, new Color3(205, 165, 90), 0.40f), seed, 3, 45, globalTime, 0.16f);
                DrawPatches(renderer, camera, center, radius, new Color3(180, 145, 85), seed + 44, 2, 0.24f, 42, globalTime, 0.09f);
                break;
            case PlanetType.Volcanic:
                DrawPatches(renderer, camera, center, radius, new Color3(255, 110, 40), seed, 3, 0.18f, 135, globalTime, 0.32f);
                DrawPatches(renderer, camera, center, radius, new Color3(30, 20, 20), seed + 9, 2, 0.30f, 80, globalTime, 0.22f);
                DrawPatches(renderer, camera, center, radius, new Color3(255, 155, 80), seed + 121, 2, 0.14f, 120, globalTime, 0.45f);
                break;
            case PlanetType.Frozen:
                DrawCracks(renderer, camera, center, radius, new Color4(220, 245, 255, isMoon ? (byte)110 : (byte)140), seed, 4, globalTime);
                break;
            case PlanetType.Rocky:
                DrawPatches(renderer, camera, center, radius, new Color3(155, 140, 120), seed + 66, 2, 0.18f, 36, globalTime, 0.06f);
                break;
            default:
                break;
        }

        // Moons and rocky/frozen worlds get craters
        if (isMoon || type is PlanetType.Rocky or PlanetType.Frozen)
        {
            int craterCount = isMoon ? 4 : 3;
            DrawCraters(renderer, camera, center, radius, seed + 77, craterCount, globalTime);
        }

        renderer.DrawFilledCircle(camera, center, radius,
            new Color4(0, 0, 0, isMoon ? (byte)65 : (byte)50),
            new Color4(0, 0, 0, 0),
            radius * 0.55f);

        // Specular highlight (top-left)
        float specX = -radius * 0.22f + radius * 0.03f * MathF.Sin(globalTime * 0.9f + phase);
        float specY = -radius * 0.22f + radius * 0.02f * MathF.Cos(globalTime * 0.7f + phase * 1.5f);
        renderer.DrawFilledCircle(camera,
            center + new Vector2(specX, specY),
            radius * (isMoon ? 0.28f : 0.36f),
            new Color4(255, 255, 255, isMoon ? (byte)24 : (byte)36));

        // Subtle rim for moons
        if (isMoon)
        {
            renderer.DrawCircle(camera, center, radius * 0.98f, new Color4(235, 235, 240, 70), 24);
        }
        else
        {
            renderer.DrawCircle(camera, center, radius * 0.992f, new Color4(245, 248, 255, 45), 36);
        }
    }

    private static void DrawAtmosphereShell(ISpriteRenderer renderer, Camera camera,
        Vector2 center, float radius, PlanetType type, float globalTime, int seed)
    {
        Color4 inner;
        Color4 mid;
        Color4 outer;

        switch (type)
        {
            case PlanetType.Terrestrial:
                inner = new Color4(170, 215, 255, 80);
                mid = new Color4(120, 180, 245, 48);
                outer = new Color4(90, 145, 220, 24);
                break;
            case PlanetType.Ocean:
                inner = new Color4(150, 210, 255, 86);
                mid = new Color4(110, 175, 245, 54);
                outer = new Color4(85, 140, 220, 28);
                break;
            case PlanetType.Frozen:
                inner = new Color4(200, 235, 255, 70);
                mid = new Color4(160, 210, 245, 45);
                outer = new Color4(120, 180, 225, 24);
                break;
            case PlanetType.GasGiant:
                inner = new Color4(235, 215, 170, 48);
                mid = new Color4(220, 190, 135, 34);
                outer = new Color4(190, 150, 95, 22);
                break;
            case PlanetType.IceGiant:
                inner = new Color4(170, 220, 255, 52);
                mid = new Color4(130, 185, 245, 36);
                outer = new Color4(95, 150, 220, 22);
                break;
            default:
                return;
        }

        float pulse = 0.92f + 0.08f * MathF.Sin(globalTime * 0.9f + seed * 0.21f);
        renderer.DrawSolidRing(camera, center, radius * 1.00f, radius * 1.05f, inner.WithAlpha((byte)(inner.A * pulse)), 40);
        renderer.DrawSolidRing(camera, center, radius * 1.05f, radius * 1.11f, mid.WithAlpha((byte)(mid.A * pulse)), 40);
        renderer.DrawSolidRing(camera, center, radius * 1.11f, radius * 1.18f, outer.WithAlpha((byte)(outer.A * pulse)), 40);
    }

    private static void DrawCloudLayer(ISpriteRenderer renderer, Camera camera, Vector2 center, float radius,
        int seed, float globalTime, float speed, byte alpha)
    {
        int cloudCount = 4;
        for (int i = 0; i < cloudCount; i++)
        {
            float angle = Hash01(seed + 19, i) * MathF.PI * 2f + globalTime * speed;
            float drift = (Hash01(seed + 41, i) - 0.5f) * radius * 0.18f;
            float dist = radius * (0.12f + Hash01(seed + 57, i) * 0.42f);
            float ox = MathF.Cos(angle) * dist;
            float oy = MathF.Sin(angle) * dist * 0.72f + drift * 0.12f;
            float cr = radius * (0.20f + Hash01(seed + 73, i) * 0.10f);
            byte a = (byte)Math.Clamp((int)(alpha * (0.85f + 0.15f * MathF.Sin(globalTime * 1.4f + i))), 0, 255);
            renderer.DrawFilledCircle(camera,
                center + new Vector2(ox, oy),
                cr,
                new Color4(240, 248, 255, a));
        }
    }

    private static void DrawBands(ISpriteRenderer renderer, Camera camera, Vector2 center, float radius,
        Color3 bandColor, int seed, int count, byte alpha, float globalTime, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float t = (i + 1f) / (count + 1f);
            float yOff = (t - 0.5f) * radius * 1.4f;
            float phase = Hash01(seed + 59, i) * MathF.PI * 2f;
            float wobble = (Hash01(seed, i) - 0.5f) * radius * 0.07f
                + MathF.Sin(globalTime * speed + phase) * radius * 0.035f;
            float bandR = radius * (0.92f - 0.08f * i / MathF.Max(1, count - 1));
            byte a = (byte)Math.Clamp((int)(alpha * (0.85f + 0.15f * (0.5f + 0.5f * MathF.Sin(globalTime * speed * 1.8f + phase)))), 0, 255);
            renderer.DrawCircle(camera,
                center + new Vector2(0, yOff + wobble),
                bandR,
                new Color4(bandColor.R, bandColor.G, bandColor.B, a),
                36);
        }
    }

    private static void DrawPatches(ISpriteRenderer renderer, Camera camera, Vector2 center, float radius,
        Color3 patchColor, int seed, int count, float sizeFactor, byte alpha, float globalTime, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = Hash01(seed + 11, i) * MathF.PI * 2f + globalTime * speed;
            float dist = radius * (0.15f + Hash01(seed + 23, i) * 0.45f);
            float ox = MathF.Cos(angle) * dist;
            float oy = MathF.Sin(angle) * dist * 0.72f;
            float pr = radius * (sizeFactor + Hash01(seed + 37, i) * 0.12f);
            byte a = (byte)Math.Clamp((int)(alpha * (0.9f + 0.1f * MathF.Sin(globalTime * (speed + 0.3f) + i))), 0, 255);
            renderer.DrawFilledCircle(camera,
                center + new Vector2(ox, oy),
                pr,
                new Color4(patchColor.R, patchColor.G, patchColor.B, a));
        }
    }

    private static void DrawCraters(ISpriteRenderer renderer, Camera camera, Vector2 center, float radius,
        int seed, int count, float globalTime)
    {
        for (int i = 0; i < count; i++)
        {
            float ox = (Hash01(seed + 5, i) - 0.5f) * radius * 1.2f;
            float oy = (Hash01(seed + 13, i) - 0.5f) * radius * 1.0f;
            float craterR = radius * (0.10f + Hash01(seed + 29, i) * 0.10f);
            byte shadowA = (byte)Math.Clamp((int)(50 + 8 * MathF.Sin(globalTime * 0.7f + i * 0.9f)), 0, 255);

            renderer.DrawFilledCircle(camera,
                center + new Vector2(ox, oy),
                craterR,
                new Color4(0, 0, 0, shadowA));
            renderer.DrawCircle(camera,
                center + new Vector2(ox - craterR * 0.1f, oy - craterR * 0.1f),
                craterR * 1.05f,
                new Color4(230, 230, 235, 45),
                20);
        }
    }

    private static void DrawCracks(ISpriteRenderer renderer, Camera camera, Vector2 center, float radius,
        Color4 color, int seed, int count, float globalTime)
    {
        for (int i = 0; i < count; i++)
        {
            float a1 = Hash01(seed + 3, i) * MathF.PI * 2f;
            float a2 = a1 + (Hash01(seed + 17, i) - 0.5f) * 0.8f;
            float r1 = radius * (0.2f + Hash01(seed + 31, i) * 0.55f);
            float r2 = radius * (0.45f + Hash01(seed + 47, i) * 0.45f);
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r1;
            var p2 = center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * r2;
            byte a = (byte)Math.Clamp((int)(color.A * (0.85f + 0.15f * (0.5f + 0.5f * MathF.Sin(globalTime * 1.5f + i)))), 0, 255);
            renderer.DrawLine(camera, p1, p2, new Color4(color.R, color.G, color.B, a));
        }
    }

    private static Color3 Lerp(Color3 from, Color3 to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color3(
            (byte)Math.Clamp((int)float.Lerp(from.R, to.R, t), 0, 255),
            (byte)Math.Clamp((int)float.Lerp(from.G, to.G, t), 0, 255),
            (byte)Math.Clamp((int)float.Lerp(from.B, to.B, t), 0, 255));
    }

    private static Color3 Mul(Color3 c, float factor)
    {
        factor = Math.Max(0f, factor);
        return new Color3(
            (byte)Math.Clamp((int)(c.R * factor), 0, 255),
            (byte)Math.Clamp((int)(c.G * factor), 0, 255),
            (byte)Math.Clamp((int)(c.B * factor), 0, 255));
    }

    private static float Hash01(int seed, int i)
    {
        unchecked
        {
            uint x = (uint)(seed * 73856093) ^ (uint)(i * 19349663) ^ 0x9E3779B9u;
            x ^= x >> 16;
            x *= 0x85EBCA6Bu;
            x ^= x >> 13;
            x *= 0xC2B2AE35u;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }
    }
}
