using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

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

    private static int HashAngle(float angle, float radius)
    {
        int ia = (int)(angle * 10000);
        int ir = (int)(radius * 10);
        return (ia * 374761393 + ir * 668265263) ^ (ia * 17);
    }
}
