using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders solar system visuals: orbit lines, NPC ships, and effects.
/// Background stars and nebulae are rendered via <see cref="StarsBackgroundRenderer"/>
/// and <see cref="NebulaBackgroundRenderer"/> respectively.
/// </summary>
public static class SolarSystemRenderer
{
    // ── Orbit Lines ─────────────────────────────────────────────────

    /// <summary>Renders orbit lines with fading glow effect.</summary>
    public static void RenderOrbitLines(ISpriteRenderer renderer, Camera camera,
        List<PlanetData> planets, Vector2 starCenter, float globalTime)
    {
        foreach (var planet in planets)
        {
            float radius = planet.OrbitRadius;

            if (!camera.CircleBorderOverlapsCamera(starCenter, radius)) continue;

            // Outer faint glow
            renderer.DrawCircle(camera, starCenter, radius + 2, new Color4(25, 30, 55, 180), 80);
            renderer.DrawCircle(camera, starCenter, radius - 2, new Color4(25, 30, 55, 180), 80);

            // Main orbit line
            renderer.DrawCircle(camera, starCenter, radius, new Color4(35, 40, 60, 180), 80);

            // Animated bright dot traveling the orbit
            float dotAngle = globalTime * (0.15f + planet.Index * 0.02f) + planet.Index * 1.5f;
            float dotX = starCenter.X + MathF.Cos(dotAngle) * radius;
            float dotY = starCenter.Y + MathF.Sin(dotAngle) * radius;
            renderer.DrawFilledCircle(camera, new Vector2(dotX, dotY), 3f,
                new Color4(60, 80, 120, 50));
        }
    }

    /// <summary>Renders orbit lines for all moons, centered on their parent planet's current position.</summary>
    public static void RenderMoonOrbitLines(ISpriteRenderer renderer, Camera camera,
        List<PlanetData> planets, Vector2 starCenter, float globalTime)
    {
        foreach (var planet in planets)
        {
            if (planet.Moons.Count == 0) continue;

            // Compute planet's current world position
            float planetAngle = planet.StartAngle + planet.OrbitSpeed * globalTime;
            var planetCenter = new Vector2(
                starCenter.X + MathF.Cos(planetAngle) * planet.OrbitRadius,
                starCenter.Y + MathF.Sin(planetAngle) * planet.OrbitRadius);

            foreach (var moon in planet.Moons)
            {
                float radius = moon.OrbitRadius;

                if (!camera.CircleBorderOverlapsCamera(planetCenter, radius)) continue;

                // Outer faint glow
                renderer.DrawCircle(camera, planetCenter, radius + 1, new Color4(20, 25, 45, 140), 48);
                renderer.DrawCircle(camera, planetCenter, radius - 1, new Color4(20, 25, 45, 140), 48);

                // Main orbit line (more subtle than planet orbits)
                renderer.DrawCircle(camera, planetCenter, radius, new Color4(28, 33, 50, 140), 48);

                // Animated bright dot traveling the moon orbit
                float dotAngle = globalTime * (0.4f + moon.Index * 0.05f) + moon.Index * 2.1f;
                float dotX = planetCenter.X + MathF.Cos(dotAngle) * radius;
                float dotY = planetCenter.Y + MathF.Sin(dotAngle) * radius;
                renderer.DrawFilledCircle(camera, new Vector2(dotX, dotY), 2f,
                    new Color4(50, 70, 110, 40));
            }
        }
    }

    /// <summary>Renders orbit lines for all space stations around their parent (star or planet).</summary>
    public static void RenderSpaceStationOrbitLines(ISpriteRenderer renderer, Camera camera,
        List<SpaceStationData> stations, List<PlanetData> planets, Vector2 starCenter, float globalTime)
    {
        foreach (var station in stations)
        {
            // Resolve parent center
            Vector2 parentCenter;
            if (station.OrbitParentPlanetIndex < 0 || station.OrbitParentPlanetIndex >= planets.Count)
            {
                parentCenter = starCenter;
            }
            else
            {
                var parent = planets[station.OrbitParentPlanetIndex];
                float parentAngle = parent.StartAngle + parent.OrbitSpeed * globalTime;
                parentCenter = new Vector2(
                    starCenter.X + MathF.Cos(parentAngle) * parent.OrbitRadius,
                    starCenter.Y + MathF.Sin(parentAngle) * parent.OrbitRadius);
            }

            float radius = station.OrbitRadius;

            if (!camera.CircleBorderOverlapsCamera(parentCenter, radius)) continue;

            // Outer faint glow (cyan-tinted for stations)
            renderer.DrawCircle(camera, parentCenter, radius + 1, new Color4(20, 40, 50, 140), 48);
            renderer.DrawCircle(camera, parentCenter, radius - 1, new Color4(20, 40, 50, 140), 48);

            // Main orbit line
            renderer.DrawCircle(camera, parentCenter, radius, new Color4(25, 50, 60, 160), 48);

            // Animated bright dot traveling the station orbit
            float dotAngle = station.StartAngle + station.OrbitSpeed * globalTime;
            float dotX = parentCenter.X + MathF.Cos(dotAngle) * radius;
            float dotY = parentCenter.Y + MathF.Sin(dotAngle) * radius;
            renderer.DrawFilledCircle(camera, new Vector2(dotX, dotY), 2.5f,
                new Color4(60, 160, 180, 60));
        }
    }

    // ── Asteroid Belt Dust ──────────────────────────────────────────

    /// <summary>Renders a subtle dust ring along the asteroid belt orbit.</summary>
    public static void RenderAsteroidBeltDust(ISpriteRenderer renderer, Camera camera,
        Vector2 starCenter, float beltRadius, float beltWidth, float globalTime)
    {
        int segments = 120;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * MathF.PI * 2f / segments;
            // Irregular dust density
            int hash = HashAngle(angle, beltRadius);
            if ((hash & 0x3) != 0) continue; // only ~25% of segments

            float rOffset = ((hash >> 4) & 0xFF) / 255f * beltWidth - beltWidth * 0.5f;
            float r = beltRadius + rOffset;
            float x = starCenter.X + MathF.Cos(angle) * r;
            float y = starCenter.Y + MathF.Sin(angle) * r;

            // Gentle twinkle
            float twinkle = 0.5f + 0.5f * MathF.Sin(globalTime * 0.8f + hash * 0.01f);
            byte alpha = (byte)(8 + twinkle * 12);
            int size = ((hash >> 12) & 0x3) + 2;

            renderer.DrawFilledCircle(camera, new Vector2(x, y), size,
                new Color4(120, 115, 100, alpha));
        }
    }

    // ── NPC Ships ───────────────────────────────────────────────────

    /// <summary>Render all NPC ships with their textures, health bars, and faction labels.</summary>
    public static void RenderNPCShips(ISpriteRenderer renderer, Camera camera, World ecsWorld,
        List<Entity> enemyEntities, EnemyShipRenderer enemyShipRenderer, float globalTime)
    {
        foreach (var entity in enemyEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (!ecsWorld.Has<Health>(entity)) continue;

            ref var health = ref ecsWorld.Get<Health>(entity);
            if (health.IsDead) continue;

            ref var transform = ref ecsWorld.Get<Transform>(entity);
            ref var sprite = ref ecsWorld.Get<Sprite>(entity);
            var ai = ecsWorld.Get<EnemyAI>(entity);
            var velocity = ecsWorld.Get<Velocity>(entity);
            int shipSize = Math.Max(sprite.Width, sprite.Height);

            // ── Warp effect: render stretched/fading ship with flash ──
            bool isWarping = ecsWorld.Has<WarpEffect>(entity);
            if (isWarping)
            {
                var warp = ecsWorld.Get<WarpEffect>(entity);
                RenderWarpEffect(renderer, camera, transform.Position, transform.Rotation,
                    ai.Config.Faction, shipSize, warp, enemyShipRenderer, globalTime);
                continue; // skip normal rendering
            }

            enemyShipRenderer.Render(renderer, camera, transform.Position, transform.Rotation,
                ai.Config.Faction, shipSize);

            // Health bar
            enemyShipRenderer.RenderHealthBar(renderer, camera, transform.Position,
                health.HullPercent, health.ShieldPercent, health.MaxShield, shipSize);

            // Faction indicator (small colored text above health bar)
            string factionLabel = ai.Config.Faction switch
            {
                Faction.Pirate => "PIRATE",
                Faction.Trader => "TRADER",
                Faction.Patrol => "PATROL",
                _ => ""
            };
            var factionColor = ai.Config.Faction switch
            {
                Faction.Pirate => new Color3(255, 80, 80),
                Faction.Trader => new Color3(200, 180, 80),
                Faction.Patrol => new Color3(80, 160, 255),
                _ => new Color3(200, 200, 200)
            };
            var factionLabelScale = 1.0f;
            var labelPos = transform.Position + new Vector2(-renderer.MeasureText(factionLabel, factionLabelScale) / 2 / camera.Zoom, -shipSize / 2f - 24);
            renderer.DrawText(camera, labelPos, factionLabel, factionColor, factionLabelScale);
        }
    }

    // ── Warp Effect Rendering ───────────────────────────────────────

    /// <summary>Render the warp-in or warp-out visual effect for an NPC ship.</summary>
    private static void RenderWarpEffect(ISpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, Faction faction, int shipSize,
        WarpEffect warp, EnemyShipRenderer enemyShipRenderer, float globalTime)
    {
        float t = warp.Progress;
        // For warp-out, reverse the visual (1→0 instead of 0→1)
        float visualT = warp.IsWarpingIn ? t : 1f - t;

        // Phase 1 (0-0.4): Bright streak converging to a point
        // Phase 2 (0.4-0.7): Flash + ship appearing with horizontal stretch
        // Phase 3 (0.7-1.0): Ship settling to normal size
        float rad = rotation * MathF.PI / 180f;
        var forward = new Vector2(MathF.Cos(rad), MathF.Sin(rad));

        var factionColor = faction switch
        {
            Faction.Pirate => new Color4(255, 100, 80, 255),
            Faction.Patrol => new Color4(100, 180, 255, 255),
            _ => new Color4(255, 220, 120, 255)
        };

        if (visualT < 0.4f)
        {
            // Streak phase — elongated line converging to position
            float streakT = visualT / 0.4f; // 0→1 within this phase
            float streakLen = shipSize * (4f - 3f * streakT);
            byte alpha = (byte)(60 + 140 * streakT);

            var streakStart = position - forward * streakLen;
            var streakEnd = position + forward * streakLen * 0.3f;

            // Draw streak as multiple overlapping circles
            int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                float st = i / (float)steps;
                var p = Vector2.Lerp(streakStart, streakEnd, st);
                float radius = (1f - Math.Abs(st - 0.7f)) * shipSize * 0.3f * streakT;
                byte a = (byte)(alpha * (1f - Math.Abs(st - 0.5f) * 1.5f));
                renderer.DrawFilledCircle(camera, p, Math.Max(radius, 1f),
                    factionColor.WithAlpha(Math.Max(a, (byte)10)));
            }

            // Central bright point
            renderer.DrawFilledCircle(camera, position, 3f + 4f * streakT,
                new Color4(255, 255, 255, (byte)(100 + 155 * streakT)));
        }
        else if (visualT < 0.7f)
        {
            // Flash + stretched ship phase
            float flashT = (visualT - 0.4f) / 0.3f; // 0→1 within this phase

            // Bright flash at the beginning of this phase
            float flashIntensity = MathF.Max(0, 1f - flashT * 2f);
            if (flashIntensity > 0)
            {
                renderer.DrawFilledCircle(camera, position, shipSize * (1f + flashIntensity),
                    new Color4(255, 255, 255, (byte)(200 * flashIntensity)));
                renderer.DrawFilledCircle(camera, position, shipSize * (0.5f + flashIntensity * 0.5f),
                    factionColor.WithAlpha((byte)(150 * flashIntensity)));
            }

            // Render ship (it's stretched but becoming normal)
            enemyShipRenderer.Render(renderer, camera, position, rotation,
                faction, shipSize);

            // Overlay glow fading out
            byte glowAlpha = (byte)(120 * (1f - flashT));
            renderer.DrawFilledCircle(camera, position, shipSize * 0.4f,
                factionColor.WithAlpha(glowAlpha));
        }
        else
        {
            // Settlement phase — ship fully visible with fading glow
            float settleT = (visualT - 0.7f) / 0.3f; // 0→1

            enemyShipRenderer.Render(renderer, camera, position, rotation,
                faction, shipSize);

            // Residual energy glow
            byte glowAlpha = (byte)(60 * (1f - settleT));
            if (glowAlpha > 5)
            {
                renderer.DrawFilledCircle(camera, position, shipSize * 0.3f * (1f - settleT * 0.5f),
                    factionColor.WithAlpha(glowAlpha));
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static int HashAngle(float angle, float radius)
    {
        int ia = (int)(angle * 10000);
        int ir = (int)(radius * 10);
        return (ia * 374761393 + ir * 668265263) ^ (ia * 17);
    }
}
