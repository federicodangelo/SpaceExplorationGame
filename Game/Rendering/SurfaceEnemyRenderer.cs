using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders surface NPCs (pirates, traders, patrols) with health bars.
/// Appearances vary by faction for visual distinction.
/// NPCs in Landing or TakingOff phase are skipped (they're inside their ship).
/// </summary>
public static class SurfaceEnemyRenderer
{
    /// <summary>Collect all surface enemies into a Y-sorted draw list.</summary>
    public static void RenderEnemies(YSortedDrawList drawList, ISpriteRenderer renderer, Camera camera,
        World world, PlanetType planetType)
    {
        var query = new QueryDescription().WithAll<Transform, SurfaceAI, Health, Sprite>();
        world.Query(in query, (Entity entity, ref Transform transform, ref SurfaceAI ai, ref Health health, ref Sprite sprite) =>
        {
            if (!ShouldRenderEnemy(world, entity, health)) return;

            var pos = transform.Position;
            var faction = ai.Config.Faction;
            var state = ai.State;
            var spriteCopy = sprite;
            var healthCopy = health;
            drawList.Add(pos.Y, () => RenderEnemy(renderer, camera, pos, faction, state, spriteCopy, healthCopy));
        });
    }

    private static bool ShouldRenderEnemy(World world, Entity entity, Health health)
    {
        if (health.IsDead) return false;
        if (world.Has<SurfaceNpcState>(entity))
        {
            ref var npcState = ref world.Get<SurfaceNpcState>(entity);
            if (npcState.Phase == SurfaceNpcPhase.Landing || npcState.Phase == SurfaceNpcPhase.TakingOff)
                return false;
        }
        return true;
    }

    private static void RenderEnemy(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, Faction faction, AIState state, Sprite sprite, Health health)
    {
        switch (faction)
        {
            case Faction.Pirate:
                RenderPirateNpc(renderer, camera, pos, state);
                break;
            case Faction.Trader:
                RenderTraderNpc(renderer, camera, pos, state);
                break;
            case Faction.Patrol:
                RenderPatrolNpc(renderer, camera, pos, state);
                break;
        }

        // Health bar above enemy
        float barWidth = sprite.Width + 4;
        float barHeight = 3;
        float barY = pos.Y - sprite.Height / 2f - 6;
        float barX = pos.X - barWidth / 2f;

        renderer.DrawRect(camera, new Vector2(barX, barY), (int)barWidth, (int)barHeight, RenderColors.HealthBarBackground);
        float fillWidth = barWidth * health.HullPercent;
        var fillColor = RenderColors.HullFillColor(health.HullPercent);
        renderer.DrawRect(camera, new Vector2(barX, barY), (int)fillWidth, (int)barHeight, new Color3(fillColor.R, fillColor.G, fillColor.B));
    }

    /// <summary>Render a pirate NPC — red/dark outfit with weapon.</summary>
    private static void RenderPirateNpc(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AIState state)
    {
        var headC = new Color3(200, 160, 120);
        var bodyC = new Color3(160, 50, 50);
        var legsC = new Color3(80, 40, 30);
        var armsC = new Color3(160, 50, 50);

        RenderHumanoidNpc(renderer, camera, pos, state, headC, bodyC, legsC, armsC);

        // Bandana
        renderer.DrawRect(camera, pos - new Vector2(4, 9), 8, 2, new Color3(200, 60, 40));

        // Weapon (when attacking or chasing)
        if (state == AIState.Attack || state == AIState.Chase)
            renderer.DrawRect(camera, pos + new Vector2(5, -1), 4, 2, new Color3(180, 180, 180));
    }

    /// <summary>Render a trader NPC — orange/brown outfit with cargo pack.</summary>
    private static void RenderTraderNpc(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AIState state)
    {
        var headC = new Color3(200, 170, 130);
        var bodyC = new Color3(200, 150, 80);
        var legsC = new Color3(120, 90, 50);
        var armsC = new Color3(200, 150, 80);

        RenderHumanoidNpc(renderer, camera, pos, state, headC, bodyC, legsC, armsC);

        // Cargo pack on back
        renderer.DrawRect(camera, pos + new Vector2(-6, -3), 3, 5, new Color3(140, 120, 80));
        renderer.DrawRect(camera, pos + new Vector2(-6, -3), 3, 1, new Color3(120, 100, 60));
    }

    /// <summary>Render a patrol NPC — blue/white outfit with badge.</summary>
    private static void RenderPatrolNpc(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AIState state)
    {
        var headC = new Color3(190, 175, 160);
        var bodyC = new Color3(60, 100, 170);
        var legsC = new Color3(50, 70, 120);
        var armsC = new Color3(60, 100, 170);

        RenderHumanoidNpc(renderer, camera, pos, state, headC, bodyC, legsC, armsC);

        // Helmet visor
        renderer.DrawRect(camera, pos - new Vector2(3, 7), 6, 2, new Color3(180, 200, 230));

        // Badge on chest
        renderer.DrawRect(camera, pos + new Vector2(1, -2), 2, 2, new Color3(220, 200, 80));
    }

    /// <summary>Shared humanoid rendering: shadow, head, body, legs, arms.</summary>
    private static void RenderHumanoidNpc(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AIState state, Color3 headC, Color3 bodyC, Color3 legsC, Color3 armsC)
    {
        // Shadow beneath feet
        renderer.DrawRect(camera, pos + new Vector2(0, 9), 10, 3, RenderColors.EntityShadow);

        // Head
        renderer.DrawRect(camera, pos - new Vector2(3, 8), 6, 5, headC);

        // Body (armor/clothing)
        renderer.DrawRect(camera, pos - new Vector2(4, 3), 8, 7, bodyC);

        // Legs
        renderer.DrawRect(camera, pos + new Vector2(-3, 4), 3, 5, legsC);
        renderer.DrawRect(camera, pos + new Vector2(0, 4), 3, 5, legsC);

        // Arms
        renderer.DrawRect(camera, pos + new Vector2(-6, -2), 2, 4, armsC);
        renderer.DrawRect(camera, pos + new Vector2(4, -2), 2, 4, armsC);
    }
}
