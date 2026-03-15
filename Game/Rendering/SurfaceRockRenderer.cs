using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders mineable rocks on the planet surface with health bars.
/// Stateless — uses draw primitives, no owned textures.
/// </summary>
public static class SurfaceRockRenderer
{
    /// <summary>Collect all surface mining rocks into a Y-sorted draw list.</summary>
    public static void RenderRocks(YSortedDrawList drawList, ISpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, AsteroidField, Health, Sprite>();
        world.Query(in query, (ref Transform transform, ref AsteroidField rock, ref Health health, ref Sprite sprite) =>
        {
            if (health.IsDead) return;
            var pos = transform.Position;
            var rockCopy = rock;
            var healthCopy = health;
            drawList.Add(pos.Y, () => RenderSingleRock(renderer, camera, pos, rockCopy, healthCopy));
        });
    }

    private static void RenderSingleRock(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, AsteroidField rock, Health health)
    {
        float size = rock.Size;
        var resInfo = ResourceCatalog.Get(rock.Resource);

        byte r = (byte)Math.Clamp(resInfo.Color.R * 0.5f + 40, 0, 255);
        byte g = (byte)Math.Clamp(resInfo.Color.G * 0.5f + 30, 0, 255);
        byte b = (byte)Math.Clamp(resInfo.Color.B * 0.5f + 20, 0, 255);

        float half = size / 2f;
        var bodyCenter = pos - new Vector2(half, half * 0.8f);
        float bodyHeight = size * 0.8f;

        float bodyBottomY = bodyCenter.Y + bodyHeight / 2f;
        var shadowPos = new Vector2(bodyCenter.X, bodyBottomY + Math.Max(1f, size * 0.14f));
        renderer.DrawRect(camera, shadowPos, (int)(size * 1.05f), Math.Max(3, (int)(size * 0.22f)), new Color4(0, 0, 0, 70));
        renderer.DrawRect(camera, shadowPos + new Vector2(0f, 1f), (int)(size * 0.7f), Math.Max(2, (int)(size * 0.12f)), new Color4(0, 0, 0, 45));

        byte or2 = (byte)Math.Max(r - 55, 0);
        byte og = (byte)Math.Max(g - 55, 0);
        byte ob = (byte)Math.Max(b - 55, 0);

        renderer.DrawRect(camera, bodyCenter - new Vector2(1f, 1f),
            (int)size + 2, (int)(size * 0.8f) + 2, new Color3(or2, og, ob));

        renderer.DrawRect(camera, bodyCenter,
            (int)size, (int)(size * 0.8f), new Color3(r, g, b));

        byte hr = (byte)Math.Min(r + 40, 255);
        byte hg = (byte)Math.Min(g + 35, 255);
        byte hb = (byte)Math.Min(b + 30, 255);
        renderer.DrawRect(camera, pos - new Vector2(half * 0.7f, half * 0.6f),
            (int)(size * 0.5f), (int)(size * 0.3f), new Color3(hr, hg, hb));

        byte dr = (byte)Math.Max(r - 30, 0);
        byte dg = (byte)Math.Max(g - 30, 0);
        byte db = (byte)Math.Max(b - 30, 0);
        renderer.DrawRect(camera, pos + new Vector2(half * 0.1f, half * 0.1f),
            (int)(size * 0.4f), (int)(size * 0.3f), new Color3(dr, dg, db));

        renderer.DrawRect(camera, pos + new Vector2(-half * 0.3f, -half * 0.2f),
            (int)(size * 0.25f), (int)(size * 0.15f), resInfo.Color);

        if (health.Hull < health.MaxHull)
        {
            float barWidth = size + 4;
            float barHeight = 3;
            float barY = pos.Y - half - 6;
            float barX = pos.X - barWidth / 2f;

            renderer.DrawRect(camera, new Vector2(barX, barY), (int)barWidth, (int)barHeight, RenderColors.HealthBarBackground);
            float fillWidth = barWidth * health.HullPercent;
            renderer.DrawRect(camera, new Vector2(barX, barY), (int)fillWidth, (int)barHeight, new Color3(180, 140, 100));
        }
    }

    /// <summary>Render all cover obstacles (destructible barriers) and their health bars.</summary>
    public static void RenderCoverObstacles(ISpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, CoverObstacle, Health>();
        world.Query(in query, (ref Transform transform, ref CoverObstacle cover, ref Health health) =>
        {
            if (health.IsDead) return;
            RenderSingleCoverObstacle(renderer, camera, transform.Position, cover, health);
        });
    }

    /// <summary>Collect all cover obstacles into a Y-sorted draw list.</summary>
    public static void CollectCoverObstacles(YSortedDrawList drawList, ISpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, CoverObstacle, Health>();
        world.Query(in query, (ref Transform transform, ref CoverObstacle cover, ref Health health) =>
        {
            if (health.IsDead) return;
            var pos = transform.Position;
            var coverCopy = cover;
            var healthCopy = health;
            drawList.Add(pos.Y, () => RenderSingleCoverObstacle(renderer, camera, pos, coverCopy, healthCopy));
        });
    }

    private static void RenderSingleCoverObstacle(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, CoverObstacle cover, Health health)
    {
        float size = cover.Size;
        float half = size / 2f;

        // Shadow
        renderer.DrawRect(camera, new Vector2(pos.X - half * 0.9f, pos.Y + half * 0.6f),
            (int)(size * 1.1f), Math.Max(3, (int)(size * 0.2f)), new Color4(0, 0, 0, 70));

        // Dark outline
        renderer.DrawRect(camera, new Vector2(pos.X - half - 1, pos.Y - half - 1),
            (int)size + 2, (int)size + 2, new Color3(30, 30, 40));

        // Main body — steel gray
        renderer.DrawRect(camera, new Vector2(pos.X - half, pos.Y - half),
            (int)size, (int)size, new Color3(90, 95, 100));

        // Panel lines (horizontal)
        renderer.DrawRect(camera, new Vector2(pos.X - half + 2, pos.Y - 1),
            (int)size - 4, 2, new Color3(55, 58, 62));

        // Top highlight
        renderer.DrawRect(camera, new Vector2(pos.X - half + 1, pos.Y - half + 1),
            (int)size - 2, (int)(size * 0.22f), new Color3(120, 128, 135));

        // Damage tint when low health
        if (health.HullPercent < 0.4f)
        {
            renderer.DrawRect(camera, new Vector2(pos.X - half, pos.Y - half),
                (int)size, (int)size, new Color4(120, 60, 0, 60));
        }

        // Health bar above cover (only when damaged)
        if (health.Hull < health.MaxHull)
        {
            float barWidth = size + 4;
            float barHeight = 3;
            float barY = pos.Y - half - 6;
            float barX = pos.X - barWidth / 2f;

            renderer.DrawRect(camera, new Vector2(barX, barY), (int)barWidth, (int)barHeight, RenderColors.HealthBarBackground);
            float fillWidth = barWidth * health.HullPercent;
            var barColor = health.HullPercent > 0.5f ? new Color3(80, 200, 80)
                         : health.HullPercent > 0.25f ? new Color3(230, 180, 0)
                         : new Color3(220, 60, 60);
            renderer.DrawRect(camera, new Vector2(barX, barY), (int)fillWidth, (int)barHeight, barColor);
        }
    }
}
