using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders mineable rocks on the planet surface with health bars.
/// Stateless — uses draw primitives, no owned textures.
/// </summary>
public static class SurfaceRockRenderer
{
    /// <summary>Render all surface mining rocks and their health bars.</summary>
    public static void RenderRocks(SpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, AsteroidField, Health, Sprite>();
        world.Query(in query, (ref Transform transform, ref AsteroidField rock, ref Health health, ref Sprite sprite) =>
        {
            if (health.IsDead) return;

            var pos = transform.Position;
            float size = rock.Size;
            var resInfo = ResourceCatalog.Get(rock.Resource);

            // Base rock color (slightly darker than sprite)
            byte r = (byte)Math.Clamp(resInfo.R * 0.5f + 40, 0, 255);
            byte g = (byte)Math.Clamp(resInfo.G * 0.5f + 30, 0, 255);
            byte b = (byte)Math.Clamp(resInfo.B * 0.5f + 20, 0, 255);

            // Shadow beneath rock
            renderer.DrawRect(camera, pos + new Vector2(1, size * 0.4f), (int)(size + 2), (int)(size * 0.3f), 0, 0, 0, 60);

            // Main rock body (irregular shape using overlapping rects)
            float half = size / 2f;
            renderer.DrawRect(camera, pos - new Vector2(half, half * 0.8f),
                (int)size, (int)(size * 0.8f), r, g, b);

            // Rock highlight (top-left, lighter)
            byte hr = (byte)Math.Min(r + 40, 255);
            byte hg = (byte)Math.Min(g + 35, 255);
            byte hb = (byte)Math.Min(b + 30, 255);
            renderer.DrawRect(camera, pos - new Vector2(half * 0.7f, half * 0.6f),
                (int)(size * 0.5f), (int)(size * 0.3f), hr, hg, hb);

            // Dark crevice (bottom-right, darker)
            byte dr = (byte)Math.Max(r - 30, 0);
            byte dg = (byte)Math.Max(g - 30, 0);
            byte db = (byte)Math.Max(b - 30, 0);
            renderer.DrawRect(camera, pos + new Vector2(half * 0.1f, half * 0.1f),
                (int)(size * 0.4f), (int)(size * 0.3f), dr, dg, db);

            // Resource vein (colored line/spot showing resource type)
            renderer.DrawRect(camera, pos + new Vector2(-half * 0.3f, -half * 0.2f),
                (int)(size * 0.25f), (int)(size * 0.15f), resInfo.R, resInfo.G, resInfo.B);

            // Health bar above rock (only when damaged)
            if (health.Hull < health.MaxHull)
            {
                float barWidth = size + 4;
                float barHeight = 3;
                float barY = pos.Y - half - 6;
                float barX = pos.X - barWidth / 2f;

                // Background
                renderer.DrawRect(camera, new Vector2(barX, barY), (int)barWidth, (int)barHeight, 40, 40, 40, 180);

                // Health fill
                float fillWidth = barWidth * health.HullPercent;
                renderer.DrawRect(camera, new Vector2(barX, barY), (int)fillWidth, (int)barHeight, 180, 140, 100);
            }
        });
    }
}
