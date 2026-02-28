using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders solar system visuals: background stars, nebulae, orbit lines, NPC ships, and effects.
/// </summary>
public static class SolarSystemRenderer
{
    // ── Background Stars ────────────────────────────────────────────

    /// <summary>Renders background stars with twinkling, color, and size variation.</summary>
    public static void RenderBackgroundStars(ISpriteRenderer renderer, Camera camera,
        List<BackgroundStar> bgStars, float globalTime)
    {
        foreach (var (x, y, brightness) in bgStars)
        {
            var pos = new Vector2(x, y);
            var screenPos = camera.WorldToScreen(pos) * 0.9f; // parallax

            if (screenPos.X < -4 || screenPos.X > GameConfig.WindowWidth + 4 ||
                screenPos.Y < -4 || screenPos.Y > GameConfig.WindowHeight + 4)
                continue;

            // Deterministic per-star hash for stable variation
            int hash = HashXY(x, y);

            // Twinkle: sinusoidal brightness pulsing, unique phase per star
            float phase = (hash & 0xFF) * 0.0245f; // 0-6.2
            float twinkleSpeed = 1.0f + ((hash >> 8) & 0x7) * 0.4f; // 1.0-3.8 Hz
            float twinkle = 0.7f + 0.3f * MathF.Sin(globalTime * twinkleSpeed + phase);
            byte b = (byte)Math.Clamp((int)(brightness * twinkle), 30, 255);

            // Color temperature variation
            int colorType = (hash >> 11) & 0x7;
            var color = colorType switch
            {
                0 => new Color3(b, b, (byte)Math.Min(b + 40, 255)),           // blue-white
                1 => new Color3((byte)Math.Min(b + 30, 255), (byte)Math.Min(b + 15, 255), b), // warm yellow
                2 => new Color3((byte)Math.Min(b + 25, 255), (byte)(b * 0.7f), (byte)(b * 0.6f)), // orange-red
                _ => new Color3(b, b, b) // white (most common)
            };

            // Size variation: most are 2px, some 3, rare 1
            int sizeClass = (hash >> 14) & 0xF;
            int starSize = sizeClass < 2 ? 1 : sizeClass < 4 ? 3 : 2;

            renderer.DrawRectScreen(screenPos.X - starSize * 0.5f, screenPos.Y - starSize * 0.5f,
                starSize, starSize, color);

            // Bright stars get a soft cross/glow
            if (brightness > 120 && starSize >= 2)
            {
                byte glowA = (byte)(b * 0.25f);
                renderer.DrawRectScreen(screenPos.X - 0.5f, screenPos.Y - 3, 1, 7,
                    new Color4(color.R, color.G, color.B, glowA));
                renderer.DrawRectScreen(screenPos.X - 3, screenPos.Y - 0.5f, 7, 1,
                    new Color4(color.R, color.G, color.B, glowA));
            }
        }
    }

    // ── Nebulae ─────────────────────────────────────────────────────

    /// <summary>Renders background nebulae with drift animation and internal structure.</summary>
    public static void RenderBackgroundNebulae(ISpriteRenderer renderer, Camera camera,
        List<NebulaCloud> nebulae, float globalTime)
    {
        foreach (var (nx, ny, nr, nColor) in nebulae)
        {
            // Slow drift
            float driftX = MathF.Sin(globalTime * 0.03f + nx * 0.001f) * nr * 0.08f;
            float driftY = MathF.Cos(globalTime * 0.025f + ny * 0.001f) * nr * 0.06f;
            var center = new Vector2(nx + driftX, ny + driftY);

            // Pulsing size
            float pulse = 1.0f + 0.04f * MathF.Sin(globalTime * 0.15f + nx * 0.002f);
            float r = nr * pulse;

            // Main cloud layers (more layers for depth)
            renderer.DrawFilledCircle(camera, center, r, nColor.WithAlpha(18));
            renderer.DrawFilledCircle(camera,
                center + new Vector2(r * 0.25f, -r * 0.15f),
                r * 0.75f, nColor.WithAlpha(14));
            renderer.DrawFilledCircle(camera,
                center + new Vector2(-r * 0.35f, r * 0.25f),
                r * 0.55f, nColor.WithAlpha(12));

            // Internal bright wisps
            float wispPhase = globalTime * 0.08f + ny * 0.001f;
            float wispX = MathF.Cos(wispPhase) * r * 0.3f;
            float wispY = MathF.Sin(wispPhase * 1.3f) * r * 0.2f;
            renderer.DrawFilledCircle(camera,
                center + new Vector2(wispX, wispY),
                r * 0.3f, nColor.WithAlpha(22));

            // Secondary wisp (different color shift)
            float wisp2Phase = globalTime * 0.06f + nx * 0.0015f;
            float wisp2X = MathF.Sin(wisp2Phase) * r * 0.25f;
            float wisp2Y = MathF.Cos(wisp2Phase * 0.8f) * r * 0.35f;
            byte altR = (byte)Math.Min(nColor.R + 15, 255);
            byte altG = (byte)Math.Min(nColor.G + 10, 255);
            byte altB = (byte)Math.Min(nColor.B + 20, 255);
            renderer.DrawFilledCircle(camera,
                center + new Vector2(wisp2X, wisp2Y),
                r * 0.25f, new Color4(altR, altG, altB, 16));
        }
    }

    // ── Orbit Lines ─────────────────────────────────────────────────

    /// <summary>Renders orbit lines with fading glow effect.</summary>
    public static void RenderOrbitLines(ISpriteRenderer renderer, Camera camera,
        List<PlanetData> planets, Vector2 starCenter, float globalTime)
    {
        foreach (var planet in planets)
        {
            float radius = planet.OrbitRadius;

            // Outer faint glow
            renderer.DrawCircle(camera, starCenter, radius + 2, new Color4(25, 30, 55, 15), 80);
            renderer.DrawCircle(camera, starCenter, radius - 2, new Color4(25, 30, 55, 15), 80);

            // Main orbit line
            renderer.DrawCircle(camera, starCenter, radius, new Color4(35, 40, 60, 40), 80);

            // Animated bright dot traveling the orbit
            float dotAngle = globalTime * (0.15f + planet.Index * 0.02f) + planet.Index * 1.5f;
            float dotX = starCenter.X + MathF.Cos(dotAngle) * radius;
            float dotY = starCenter.Y + MathF.Sin(dotAngle) * radius;
            renderer.DrawFilledCircle(camera, new Vector2(dotX, dotY), 3f,
                new Color4(60, 80, 120, 50));
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
            var ai = ecsWorld.Get<EnemyAI>(entity);
            var velocity = ecsWorld.Get<Velocity>(entity);
            int shipSize = ai.Config.Faction switch
            {
                Faction.Pirate => 28,
                Faction.Trader => 32,
                Faction.Patrol => 30,
                _ => 28
            };

            enemyShipRenderer.Render(renderer, camera, transform.Position, transform.Rotation,
                ai.Config.Faction, shipSize);

            // Engine trail for moving ships
            float speed = velocity.Linear.Length();
            if (speed > 30f)
            {
                float rad = transform.Rotation * MathF.PI / 180f;
                var engineDir = new Vector2(-MathF.Cos(rad), -MathF.Sin(rad));
                var enginePos = transform.Position + engineDir * shipSize * 0.4f;
                float trailLen = Math.Min(speed * 0.08f, 12f);
                byte trailA = (byte)Math.Min(speed * 0.3f, 80);

                var trailColor = ai.Config.Faction switch
                {
                    Faction.Pirate => new Color4(255, 140, 50, trailA),
                    Faction.Patrol => new Color4(100, 160, 255, trailA),
                    _ => new Color4(255, 200, 100, trailA)
                };

                renderer.DrawFilledCircle(camera, enginePos, trailLen * 0.5f, trailColor);
                renderer.DrawFilledCircle(camera, enginePos + engineDir * trailLen * 0.3f,
                    trailLen * 0.3f, trailColor.WithAlpha((byte)(trailA / 2)));
            }

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
            var labelPos = transform.Position - new Vector2(0, shipSize / 2f + 18f);
            renderer.DrawText(camera, labelPos, factionLabel, factionColor, 0.8f);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static int HashXY(float x, float y)
    {
        int ix = (int)(x * 100);
        int iy = (int)(y * 100);
        return (ix * 374761393 + iy * 668265263) ^ (ix * 17 + iy * 31);
    }

    private static int HashAngle(float angle, float radius)
    {
        int ia = (int)(angle * 10000);
        int ir = (int)(radius * 10);
        return (ia * 374761393 + ir * 668265263) ^ (ia * 17);
    }
}
