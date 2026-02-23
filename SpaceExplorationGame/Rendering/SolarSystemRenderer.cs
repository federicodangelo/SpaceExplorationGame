using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders solar system visuals: background stars, celestial bodies, NPC ships, and effects.
/// </summary>
public static class SolarSystemRenderer
{
    /// <summary>Renders background stars.</summary>
    public static void RenderBackgroundStars(SpriteRenderer renderer, Camera camera, List<BackgroundStar> bgStars)
    {
        foreach (var (x, y, brightness) in bgStars)
        {
            var pos = new Vector2(x, y);
            var screenPos = camera.WorldToScreen(pos) * 0.9f; // Move stars slightly slower for parallax effect

            if (screenPos.X >= 0 && screenPos.X < GameConfig.WindowWidth &&
                screenPos.Y >= 0 && screenPos.Y < GameConfig.WindowHeight)
            {
                renderer.DrawRectScreen(screenPos.X - 0.5f, screenPos.Y - 0.5f, 2, 2, new Color3(brightness, brightness, brightness));
            }
        }
    }

    /// <summary>Renders background nebulae.</summary>
    public static void RenderBackgroundNebulae(SpriteRenderer renderer, Camera camera, List<NebulaCloud> nebulae)
    {
        foreach (var (nx, ny, nr, nColor) in nebulae)
        {
            renderer.DrawFilledCircle(camera, new Vector2(nx, ny), nr, nColor.WithAlpha(20));
            renderer.DrawFilledCircle(camera, new Vector2(nx + nr * 0.3f, ny - nr * 0.2f), nr * 0.7f, nColor.WithAlpha(15));
            renderer.DrawFilledCircle(camera, new Vector2(nx - nr * 0.4f, ny + nr * 0.3f), nr * 0.5f, nColor.WithAlpha(10));
        }
    }

    /// <summary>Renders orbit lines for all planets.</summary>
    public static void RenderOrbitLines(SpriteRenderer renderer, Camera camera,
        List<PlanetData> planets, Vector2 starCenter)
    {
        foreach (var planet in planets)
        {
            renderer.DrawCircle(camera, starCenter, planet.OrbitRadius, new Color3(30, 30, 50), 64);
        }
    }

    /// <summary>Render all NPC ships with their textures, health bars, and faction labels.</summary>
    public static void RenderNPCShips(SpriteRenderer renderer, Camera camera, World ecsWorld,
        List<Entity> enemyEntities, EnemyShipRenderer enemyShipRenderer)
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

}
