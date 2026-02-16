using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Hud;

/// <summary>Minimap marker shape.</summary>
public enum MinimapMarkerShape { Rect, Circle }

/// <summary>A single point marker on the minimap.</summary>
public readonly record struct MinimapMarker(
    Vector2 WorldPos, byte R, byte G, byte B, byte A = 255,
    float Size = 3f, MinimapMarkerShape Shape = MinimapMarkerShape.Rect);

/// <summary>A rectangular area on the minimap (e.g. rooms, settlements).</summary>
public readonly record struct MinimapArea(
    float WorldX, float WorldY, float WorldW, float WorldH,
    byte R, byte G, byte B, byte A = 255);

/// <summary>
/// Unified minimap renderer used by HudRenderer across all game states.
/// Draws a bordered minimap panel (top-right) with areas, markers, and
/// a green player dot.
/// </summary>
public static class HudMinimapRenderer
{
    // Minimap constants
    private const float MinimapSize = 150f;
    private const float MinimapMargin = 10f;
    private const float MinimapViewFraction = 0.3f; // fraction of map shown as view radius

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API (called by HudRenderer)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render the solar system minimap (top-right, centered on the player).</summary>
    public static void RenderSolarSystemMinimap(SpriteRenderer renderer,
        List<PlanetData> planets, List<Entity> planetEntities,
        List<List<Entity>> moonEntities, List<Entity> stationEntities,
        List<Entity> asteroidEntities, List<Entity> enemyEntities,
        Entity playerShip, Entity starEntity, World ecsWorld,
        float starRadius)
    {
        float mapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float mapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;

        Vector2 playerPos = ecsWorld.IsAlive(playerShip)
            ? ecsWorld.Get<Transform>(playerShip).Position
            : new Vector2(mapW / 2f, mapH / 2f);
        var (viewOrigin, viewSize) = PlayerCenteredView(mapW, mapH, playerPos);

        // Collect markers
        var markers = new List<MinimapMarker>();

        // Star (yellow circle)
        if (ecsWorld.IsAlive(starEntity))
        {
            var pos = ecsWorld.Get<Transform>(starEntity).Position;
            markers.Add(new MinimapMarker(pos, 255, 220, 80, Size: 6f, Shape: MinimapMarkerShape.Circle));
        }

        // Asteroids (dim grey, tiny)
        foreach (var entity in asteroidEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (ecsWorld.Has<Health>(entity) && ecsWorld.Get<Health>(entity).IsDead) continue;
            markers.Add(new MinimapMarker(ecsWorld.Get<Transform>(entity).Position, 80, 80, 80, Size: 1f));
        }

        // Planets (colored circles) and moons
        for (int i = 0; i < planetEntities.Count; i++)
        {
            if (!ecsWorld.IsAlive(planetEntities[i])) continue;
            byte pr = i < planets.Count ? planets[i].R : (byte)180;
            byte pg = i < planets.Count ? planets[i].G : (byte)180;
            byte pb = i < planets.Count ? planets[i].B : (byte)180;
            markers.Add(new MinimapMarker(ecsWorld.Get<Transform>(planetEntities[i]).Position,
                pr, pg, pb, A: 220, Size: 4f, Shape: MinimapMarkerShape.Circle));

            if (i < moonEntities.Count)
            {
                foreach (var moonEntity in moonEntities[i])
                {
                    if (!ecsWorld.IsAlive(moonEntity)) continue;
                    markers.Add(new MinimapMarker(ecsWorld.Get<Transform>(moonEntity).Position,
                        160, 160, 180, Size: 2f));
                }
            }
        }

        // Stations (cyan)
        foreach (var entity in stationEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            markers.Add(new MinimapMarker(ecsWorld.Get<Transform>(entity).Position, 100, 200, 255));
        }

        // Enemies (faction-colored)
        foreach (var entity in enemyEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (!ecsWorld.Has<Health>(entity) || ecsWorld.Get<Health>(entity).IsDead) continue;
            var ai = ecsWorld.Get<EnemyAI>(entity);
            var (er, eg, eb) = ai.Config.Faction switch
            {
                Faction.Pirate => ((byte)255, (byte)80, (byte)80),
                Faction.Trader => ((byte)200, (byte)180, (byte)80),
                Faction.Patrol => ((byte)80, (byte)160, (byte)255),
                _ => ((byte)200, (byte)200, (byte)200)
            };
            markers.Add(new MinimapMarker(ecsWorld.Get<Transform>(entity).Position, er, eg, eb));
        }

        RenderMinimap(renderer, viewOrigin, viewSize,
            ReadOnlySpan<MinimapArea>.Empty, markers.ToArray(), playerPos, centerOnPlayer: true);
    }

    /// <summary>Render the planet surface minimap (top-right, centered on the player).</summary>
    public static void RenderPlanetSurfaceMinimap(SpriteRenderer renderer,
        PlanetSurfaceData surfaceData, Vector2 playerPos, Vector2 shipPos,
        Vector2? vehiclePos, World ecsWorld)
    {
        float mapW = surfaceData.Width * GameConfig.TileSize;
        float mapH = surfaceData.Height * GameConfig.TileSize;
        var (viewOrigin, viewSize) = PlayerCenteredView(mapW, mapH, playerPos);

        // Areas (settlements)
        var areas = new List<MinimapArea>();
        foreach (var s in surfaceData.Settlements)
        {
            areas.Add(new MinimapArea(
                s.TileX * GameConfig.TileSize, s.TileY * GameConfig.TileSize,
                s.Width * GameConfig.TileSize, s.Height * GameConfig.TileSize,
                200, 180, 80));
        }

        // Markers
        var markers = new List<MinimapMarker>();

        // Ship (blue-ish)
        markers.Add(new MinimapMarker(shipPos, 150, 150, 200, Size: 4f));

        // Vehicle (orange)
        if (vehiclePos.HasValue)
            markers.Add(new MinimapMarker(vehiclePos.Value, 180, 140, 80, Size: 4f));

        // Surface enemies
        CollectSurfaceEnemyMarkers(ecsWorld, markers);

        RenderMinimap(renderer, viewOrigin, viewSize,
            areas.ToArray(), markers.ToArray(), playerPos, centerOnPlayer: true);
    }

    /// <summary>Render the interior minimap (top-right, shows entire interior).</summary>
    public static void RenderInteriorMinimap(SpriteRenderer renderer, InteriorData interior,
        Vector2 playerPos)
    {
        float mapW = interior.Width * GameConfig.TileSize;
        float mapH = interior.Height * GameConfig.TileSize;
        var viewOrigin = Vector2.Zero;
        var viewSize = new Vector2(mapW, mapH);

        // Areas (rooms)
        var areas = new MinimapArea[interior.Rooms.Count];
        for (int i = 0; i < interior.Rooms.Count; i++)
        {
            var room = interior.Rooms[i];
            areas[i] = new MinimapArea(
                room.X * GameConfig.TileSize, room.Y * GameConfig.TileSize,
                room.Width * GameConfig.TileSize, room.Height * GameConfig.TileSize,
                50, 50, 60);
        }

        // Markers (NPCs + interactables)
        var markers = new List<MinimapMarker>(interior.Npcs.Count + interior.Interactables.Count);
        foreach (var npc in interior.Npcs)
        {
            markers.Add(new MinimapMarker(
                new Vector2(npc.TileX * GameConfig.TileSize, npc.TileY * GameConfig.TileSize),
                npc.R, npc.G, npc.B));
        }
        foreach (var interactable in interior.Interactables)
        {
            var (ir, ig, ib) = InteriorRenderer.GetInteractableColor(interactable.Type);
            markers.Add(new MinimapMarker(
                new Vector2(interactable.TileX * GameConfig.TileSize, interactable.TileY * GameConfig.TileSize),
                ir, ig, ib));
        }

        RenderMinimap(renderer, viewOrigin, viewSize,
            areas, markers.ToArray(), playerPos, centerOnPlayer: false);
    }

    // ─────────────────────────────────────────────────────────────
    //  PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Core minimap renderer. Draws the border, background, all areas and markers,
    /// and the player dot. All coordinates are in world space and mapped to the
    /// minimap via the supplied view origin and view size.
    /// </summary>
    private static void RenderMinimap(SpriteRenderer renderer,
        Vector2 viewOrigin, Vector2 viewSize,
        ReadOnlySpan<MinimapArea> areas, ReadOnlySpan<MinimapMarker> markers,
        Vector2 playerWorldPos, bool centerOnPlayer)
    {
        float mmX = GameConfig.WindowWidth - MinimapSize - MinimapMargin;
        float mmY = MinimapMargin;

        // Border + background
        renderer.DrawRectScreen(mmX - 1, mmY - 1, MinimapSize + 2, MinimapSize + 2, 60, 60, 100);
        renderer.DrawRectScreen(mmX, mmY, MinimapSize, MinimapSize, 10, 10, 15, 220);

        float scaleX = MinimapSize / viewSize.X;
        float scaleY = MinimapSize / viewSize.Y;

        // Rectangular areas (rooms, settlements)
        foreach (var a in areas)
        {
            float sx = mmX + (a.WorldX - viewOrigin.X) * scaleX;
            float sy = mmY + (a.WorldY - viewOrigin.Y) * scaleY;
            float sw = a.WorldW * scaleX;
            float sh = a.WorldH * scaleY;
            if (sx + sw < mmX || sx > mmX + MinimapSize || sy + sh < mmY || sy > mmY + MinimapSize) continue;
            renderer.DrawRectScreen(sx, sy, Math.Max(sw, 3), Math.Max(sh, 3), a.R, a.G, a.B, a.A);
        }

        // Point markers
        foreach (var m in markers)
        {
            float sx = mmX + (m.WorldPos.X - viewOrigin.X) * scaleX;
            float sy = mmY + (m.WorldPos.Y - viewOrigin.Y) * scaleY;
            if (!InMinimap(sx, sy, mmX, mmY)) continue;

            float half = m.Size / 2f;
            if (m.Shape == MinimapMarkerShape.Circle)
                renderer.DrawFilledCircleScreen(sx, sy, half, m.R, m.G, m.B, m.A);
            else
                renderer.DrawRectScreen(sx - half, sy - half, m.Size, m.Size, m.R, m.G, m.B, m.A);
        }

        // Player dot (green, always on top)
        float px, py;
        if (centerOnPlayer)
        {
            px = mmX + MinimapSize / 2f;
            py = mmY + MinimapSize / 2f;
        }
        else
        {
            px = mmX + (playerWorldPos.X - viewOrigin.X) * scaleX;
            py = mmY + (playerWorldPos.Y - viewOrigin.Y) * scaleY;
        }
        renderer.DrawRectScreen(px - 2, py - 2, 4, 4, 100, 255, 100);
    }

    /// <summary>Computes the player-centered view for scrolling minimaps.</summary>
    private static (Vector2 Origin, Vector2 Size) PlayerCenteredView(
        float mapW, float mapH, Vector2 playerPos)
    {
        float viewRadius = MathF.Min(mapW, mapH) * MinimapViewFraction;
        float viewSize = viewRadius * 2f;
        var origin = new Vector2(playerPos.X - viewRadius, playerPos.Y - viewRadius);
        return (origin, new Vector2(viewSize, viewSize));
    }

    /// <summary>Returns true if screen coordinates fall within the minimap area.</summary>
    private static bool InMinimap(float sx, float sy, float mmX, float mmY) =>
        sx >= mmX && sx <= mmX + MinimapSize && sy >= mmY && sy <= mmY + MinimapSize;

    /// <summary>Collect surface enemy markers from the ECS world into the markers list.</summary>
    private static void CollectSurfaceEnemyMarkers(World world, List<MinimapMarker> markers)
    {
        var query = new QueryDescription().WithAll<Transform, SurfaceAI, Health>();
        world.Query(in query, (ref Transform transform, ref SurfaceAI ai, ref Health health) =>
        {
            if (health.IsDead) return;
            byte r = ai.Config.Faction == Faction.Fauna ? (byte)200 : (byte)255;
            byte g = ai.Config.Faction == Faction.Fauna ? (byte)60 : (byte)150;
            byte b = ai.Config.Faction == Faction.Fauna ? (byte)60 : (byte)50;
            markers.Add(new MinimapMarker(transform.Position, r, g, b));
        });
    }
}
