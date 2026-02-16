using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders solar system visuals: background stars, celestial bodies, NPC ships, and effects.
/// </summary>
public static class SolarSystemRenderer
{
    /// <summary>Renders parallax background stars.</summary>
    public static void RenderBackgroundStars(SpriteRenderer renderer, Camera camera,
        List<(float X, float Y, byte Brightness)> bgStars, Vector2 starCenter)
    {
        foreach (var (x, y, brightness) in bgStars)
        {
            var parallaxPos = new Vector2(x, y);
            var screenPos = camera.WorldToScreen(parallaxPos);
            screenPos.X -= (camera.Position.X - starCenter.X) * 0.3f * camera.Zoom;
            screenPos.Y -= (camera.Position.Y - starCenter.Y) * 0.3f * camera.Zoom;

            if (screenPos.X >= 0 && screenPos.X < GameConfig.WindowWidth &&
                screenPos.Y >= 0 && screenPos.Y < GameConfig.WindowHeight)
            {
                renderer.DrawRectScreen(screenPos.X, screenPos.Y, 1, 1, brightness, brightness, brightness);
            }
        }
    }

    /// <summary>Renders orbit lines for all planets.</summary>
    public static void RenderOrbitLines(SpriteRenderer renderer, Camera camera,
        List<PlanetData> planets, Vector2 starCenter)
    {
        foreach (var planet in planets)
        {
            renderer.DrawCircle(camera, starCenter, planet.OrbitRadius, 30, 30, 50, 255, 64);
        }
    }

    /// <summary>Renders the mining target info panel for an asteroid entity.</summary>
    public static void RenderMiningPanel(SpriteRenderer renderer, ResourceType resource,
        float hp, float maxHp, int resourceAmount)
    {
        var resInfo = ResourceCatalog.Get(resource);
        float panelW = 280;
        float panelH = 72;
        float px = GameConfig.WindowWidth / 2f - panelW / 2f;
        float py = GameConfig.WindowHeight - panelH - 15;

        renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);
        renderer.DrawTextScreen(px + 10, py + 6, $"ASTEROID - {resInfo.Name.ToUpper()}", resInfo.R, resInfo.G, resInfo.B, 2f);

        // HP bar
        float barX = px + 10;
        float barY = py + 30;
        float barW = panelW - 20;
        float hpRatio = maxHp > 0 ? hp / maxHp : 0;
        renderer.DrawRectScreen(barX, barY, barW, 12, 40, 40, 40);
        renderer.DrawRectScreen(barX, barY, barW * hpRatio, 12, resInfo.R, resInfo.G, resInfo.B);

        renderer.DrawTextScreen(px + 10, py + 48, $"HP: {hp:F0}/{maxHp:F0}  QTY: {resourceAmount}", 180, 180, 180, 1.5f);
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

            bool isMoving = velocity.Value.LengthSquared() > 50f * 50f;
            int shipSize = ai.Config.Faction switch
            {
                Faction.Pirate => 28,
                Faction.Trader => 32,
                Faction.Patrol => 30,
                _ => 28
            };

            enemyShipRenderer.Render(renderer, camera, transform.Position, transform.Rotation,
                ai.Config.Faction, shipSize, isMoving);

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
            var (fr, fg, fb) = ai.Config.Faction switch
            {
                Faction.Pirate => ((byte)255, (byte)80, (byte)80),
                Faction.Trader => ((byte)200, (byte)180, (byte)80),
                Faction.Patrol => ((byte)80, (byte)160, (byte)255),
                _ => ((byte)200, (byte)200, (byte)200)
            };
            var labelPos = transform.Position - new Vector2(0, shipSize / 2f + 18f);
            renderer.DrawText(camera, labelPos, factionLabel, fr, fg, fb, 0.8f);
        }
    }

    /// <summary>Render the death overlay with respawn countdown.</summary>
    public static void RenderDeathScreen(SpriteRenderer renderer, float respawnTimer)
    {
        renderer.DrawRectScreen(0, GameConfig.WindowHeight / 2f - 40, GameConfig.WindowWidth, 80, 0, 0, 0, 180);
        string deathText = $"SHIP DESTROYED - RESPAWNING IN {respawnTimer:F1}s";
        float textW = renderer.MeasureText(deathText, 3f);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - textW / 2f,
            GameConfig.WindowHeight / 2f - 15, deathText, 255, 80, 80, 3f);
    }

    /// <summary>Render a centered feedback message at the given vertical offset.</summary>
    public static void RenderCenteredMessage(SpriteRenderer renderer, string message,
        float yOffset, byte r, byte g, byte b, float scale)
    {
        float msgW = renderer.MeasureText(message, scale);
        float msgX = GameConfig.WindowWidth / 2f - msgW / 2f;
        renderer.DrawTextScreen(msgX, GameConfig.WindowHeight / 2f + yOffset, message, r, g, b, scale);
    }

}
