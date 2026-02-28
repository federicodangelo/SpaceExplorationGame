using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders surface enemies (fauna and bandits) with health bars.
/// Stateless — uses draw primitives, no owned textures.
/// </summary>
public static class SurfaceEnemyRenderer
{
    /// <summary>Render all surface enemies and their health bars.</summary>
    public static void RenderEnemies(ISpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, SurfaceAI, Health, Sprite>();
        world.Query(in query, (ref Transform transform, ref SurfaceAI ai, ref Health health, ref Sprite sprite) =>
        {
            if (health.IsDead) return;

            var pos = transform.Position;

            // Draw enemy body
            if (ai.Config.Faction == Faction.Fauna)
            {
                RenderFauna(renderer, camera, pos, ai.State);
            }
            else if (ai.Config.Faction == Faction.Bandit)
            {
                RenderBandit(renderer, camera, pos, ai.State);
            }

            // Health bar above enemy
            float barWidth = sprite.Width + 4;
            float barHeight = 3;
            float barY = pos.Y - sprite.Height / 2f - 6;
            float barX = pos.X - barWidth / 2f;

            // Background
            renderer.DrawRect(camera, new Vector2(barX, barY), (int)barWidth, (int)barHeight, RenderColors.HealthBarBackground);

            // Health fill
            float fillWidth = barWidth * health.HullPercent;
            var fillColor = RenderColors.HullFillColor(health.HullPercent);
            renderer.DrawRect(camera, new Vector2(barX, barY), (int)fillWidth, (int)barHeight, new Color3(fillColor.R, fillColor.G, fillColor.B));
        });
    }

    /// <summary>Render a fauna creature — simple 4-legged creature shape.</summary>
    private static void RenderFauna(ISpriteRenderer renderer, Camera camera, Vector2 pos, AIState state)
    {
        // Shadow beneath feet
        renderer.DrawRect(camera, pos + new Vector2(0, 8), 14, 4, RenderColors.EntityShadow);

        // Body (reddish-brown oval)
        renderer.DrawRect(camera, pos - new Vector2(7, 5), 14, 10, new Color3(180, 60, 60));

        // Head (front, slightly lighter)
        renderer.DrawRect(camera, pos + new Vector2(5, -3), 5, 6, new Color3(200, 80, 70));

        // Legs (4 short stubs)
        renderer.DrawRect(camera, pos + new Vector2(-5, 5), 3, 3, new Color3(140, 50, 50));
        renderer.DrawRect(camera, pos + new Vector2(2, 5), 3, 3, new Color3(140, 50, 50));

        // Eyes (red when aggressive)
        if (state == AIState.Chase || state == AIState.Attack)
        {
            renderer.DrawRect(camera, pos + new Vector2(7, -2), 2, 2, new Color3(255, 50, 50));
        }
    }

    /// <summary>Render a bandit NPC — hostile humanoid shape.</summary>
    private static void RenderBandit(ISpriteRenderer renderer, Camera camera, Vector2 pos, AIState state)
    {
        // Shadow beneath feet
        renderer.DrawRect(camera, pos + new Vector2(0, 9), 10, 3, RenderColors.EntityShadow);

        // Head (tan)
        renderer.DrawRect(camera, pos - new Vector2(3, 8), 6, 5, new Color3(200, 160, 120));

        // Body (dark orange/brown armor)
        renderer.DrawRect(camera, pos - new Vector2(4, 3), 8, 7, new Color3(200, 100, 60));

        // Legs
        renderer.DrawRect(camera, pos + new Vector2(-3, 4), 3, 5, new Color3(100, 70, 40));
        renderer.DrawRect(camera, pos + new Vector2(0, 4), 3, 5, new Color3(100, 70, 40));

        // Arms
        renderer.DrawRect(camera, pos + new Vector2(-6, -2), 2, 4, new Color3(200, 100, 60));
        renderer.DrawRect(camera, pos + new Vector2(4, -2), 2, 4, new Color3(200, 100, 60));

        // Weapon (when attacking or chasing)
        if (state == AIState.Attack || state == AIState.Chase)
        {
            renderer.DrawRect(camera, pos + new Vector2(5, -1), 4, 2, new Color3(180, 180, 180));
        }
    }
}
