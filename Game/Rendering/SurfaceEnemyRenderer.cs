using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using Engine.Platform;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders surface enemies (fauna and bandits) with health bars.
/// Appearances vary by planet type for visual diversity.
/// </summary>
public static class SurfaceEnemyRenderer
{
    /// <summary>Render all surface enemies and their health bars.</summary>
    public static void RenderEnemies(ISpriteRenderer renderer, Camera camera,
        World world, PlanetType planetType)
    {
        var query = new QueryDescription().WithAll<Transform, SurfaceAI, Health, Sprite>();
        world.Query(in query, (ref Transform transform, ref SurfaceAI ai, ref Health health, ref Sprite sprite) =>
        {
            if (health.IsDead) return;

            var pos = transform.Position;

            // Draw enemy body
            if (ai.Config.Faction == Faction.Fauna)
            {
                RenderFauna(renderer, camera, pos, ai.State, planetType);
            }
            else if (ai.Config.Faction == Faction.Bandit)
            {
                RenderBandit(renderer, camera, pos, ai.State, planetType);
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

    /// <summary>Get fauna body colors based on planet type.</summary>
    private static (Color3 body, Color3 head, Color3 legs, Color3 aggressiveEye) GetFaunaColors(PlanetType pt) => pt switch
    {
        PlanetType.Frozen => (new Color3(180, 190, 210), new Color3(200, 210, 230), new Color3(150, 160, 180), new Color3(100, 200, 255)),
        PlanetType.Desert => (new Color3(200, 170, 100), new Color3(220, 190, 120), new Color3(170, 140, 80), new Color3(255, 180, 50)),
        PlanetType.Volcanic => (new Color3(120, 50, 40), new Color3(150, 60, 45), new Color3(90, 40, 35), new Color3(255, 100, 30)),
        PlanetType.Ocean => (new Color3(80, 140, 160), new Color3(100, 160, 180), new Color3(60, 110, 130), new Color3(50, 255, 180)),
        PlanetType.IceGiant => (new Color3(170, 180, 220), new Color3(190, 200, 240), new Color3(140, 150, 190), new Color3(100, 180, 255)),
        _ => (new Color3(180, 60, 60), new Color3(200, 80, 70), new Color3(140, 50, 50), new Color3(255, 50, 50)),
    };

    /// <summary>Get bandit armor/clothing colors based on planet type.</summary>
    private static (Color3 head, Color3 body, Color3 legs, Color3 arms) GetBanditColors(PlanetType pt) => pt switch
    {
        PlanetType.Frozen => (new Color3(200, 180, 170), new Color3(100, 120, 160), new Color3(80, 90, 120), new Color3(100, 120, 160)),
        PlanetType.Desert => (new Color3(180, 140, 100), new Color3(180, 160, 100), new Color3(120, 100, 60), new Color3(180, 160, 100)),
        PlanetType.Volcanic => (new Color3(160, 130, 100), new Color3(140, 60, 40), new Color3(80, 50, 35), new Color3(140, 60, 40)),
        PlanetType.Ocean => (new Color3(180, 150, 120), new Color3(60, 120, 140), new Color3(50, 90, 100), new Color3(60, 120, 140)),
        _ => (new Color3(200, 160, 120), new Color3(200, 100, 60), new Color3(100, 70, 40), new Color3(200, 100, 60)),
    };

    /// <summary>Render a fauna creature — appearance varies by planet type.</summary>
    private static void RenderFauna(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AIState state, PlanetType planetType)
    {
        var (body, head, legs, aggressiveEye) = GetFaunaColors(planetType);

        // Shadow beneath feet
        renderer.DrawRect(camera, pos + new Vector2(0, 8), 14, 4, RenderColors.EntityShadow);

        // Body
        renderer.DrawRect(camera, pos - new Vector2(7, 5), 14, 10, body);

        // Head (front, slightly lighter)
        renderer.DrawRect(camera, pos + new Vector2(5, -3), 5, 6, head);

        // Planet-specific feature
        switch (planetType)
        {
            case PlanetType.Frozen:
                // Frost tuft on back
                renderer.DrawRect(camera, pos + new Vector2(-3, -6), 4, 3, new Color3(220, 230, 245));
                break;
            case PlanetType.Volcanic:
                // Glowing belly stripe
                renderer.DrawRect(camera, pos + new Vector2(-5, 1), 10, 2, new Color3(255, 120, 40));
                break;
            case PlanetType.Ocean:
                // Fin on back
                renderer.DrawRect(camera, pos + new Vector2(0, -7), 6, 3, new Color3(60, 120, 150));
                break;
            case PlanetType.Desert:
                // Spiny ridge
                renderer.DrawRect(camera, pos + new Vector2(-4, -6), 8, 2, new Color3(180, 150, 80));
                break;
        }

        // Legs (4 short stubs)
        renderer.DrawRect(camera, pos + new Vector2(-5, 5), 3, 3, legs);
        renderer.DrawRect(camera, pos + new Vector2(2, 5), 3, 3, legs);

        // Eyes (glow when aggressive)
        if (state == AIState.Chase || state == AIState.Attack)
        {
            renderer.DrawRect(camera, pos + new Vector2(7, -2), 2, 2, aggressiveEye);
        }
    }

    /// <summary>Render a bandit NPC — outfit varies by planet type.</summary>
    private static void RenderBandit(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AIState state, PlanetType planetType)
    {
        var (headC, bodyC, legsC, armsC) = GetBanditColors(planetType);

        // Shadow beneath feet
        renderer.DrawRect(camera, pos + new Vector2(0, 9), 10, 3, RenderColors.EntityShadow);

        // Head
        renderer.DrawRect(camera, pos - new Vector2(3, 8), 6, 5, headC);

        // Helmet / headgear per planet type
        switch (planetType)
        {
            case PlanetType.Frozen:
                // Fur-lined hood
                renderer.DrawRect(camera, pos - new Vector2(4, 9), 8, 3, new Color3(160, 150, 140));
                break;
            case PlanetType.Volcanic:
                // Metal mask visor
                renderer.DrawRect(camera, pos - new Vector2(3, 7), 6, 2, new Color3(80, 70, 65));
                break;
            case PlanetType.Desert:
                // Headscarf
                renderer.DrawRect(camera, pos - new Vector2(4, 9), 8, 2, new Color3(200, 180, 140));
                renderer.DrawRect(camera, pos + new Vector2(3, -7), 3, 4, new Color3(200, 180, 140));
                break;
        }

        // Body (armor)
        renderer.DrawRect(camera, pos - new Vector2(4, 3), 8, 7, bodyC);

        // Legs
        renderer.DrawRect(camera, pos + new Vector2(-3, 4), 3, 5, legsC);
        renderer.DrawRect(camera, pos + new Vector2(0, 4), 3, 5, legsC);

        // Arms
        renderer.DrawRect(camera, pos + new Vector2(-6, -2), 2, 4, armsC);
        renderer.DrawRect(camera, pos + new Vector2(4, -2), 2, 4, armsC);

        // Weapon (when attacking or chasing)
        if (state == AIState.Attack || state == AIState.Chase)
        {
            renderer.DrawRect(camera, pos + new Vector2(5, -1), 4, 2, new Color3(180, 180, 180));
        }
    }
}
