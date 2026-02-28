using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.UI.Hud;

/// <summary>
/// Renders off-screen edge indicators, world-space mission markers,
/// and navigation target markers.
/// </summary>
public static class HudIndicatorsRenderer
{
    // ── Off-screen indicators ──────────────────────────────────────────

    /// <summary>Render arrow indicators at screen edges for off-screen hostile NPC ships within range.</summary>
    public static void RenderOffscreenIndicators(ISpriteRenderer renderer, Camera camera, World ecsWorld,
        List<Entity> enemyEntities, Entity playerShip, float maxDistance = float.MaxValue)
    {
        Vector2 playerPos = ecsWorld.IsAlive(playerShip)
            ? ecsWorld.Get<Transform>(playerShip).Position
            : camera.Position;

        foreach (var entity in enemyEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (!ecsWorld.Has<Health>(entity)) continue;
            ref var health = ref ecsWorld.Get<Health>(entity);
            if (health.IsDead) continue;

            var ai = ecsWorld.Get<EnemyAI>(entity);

            // Only show indicators for hostile factions (pirates)
            if (ai.Config.Faction != Faction.Pirate) continue;

            ref var transform = ref ecsWorld.Get<Transform>(entity);

            // Skip ships beyond max distance
            float dist = Vector2.Distance(playerPos, transform.Position);
            if (dist > maxDistance) continue;

            // Fade alpha by distance (fully opaque at close range, fading toward maxDistance)
            float distFraction = dist / maxDistance;
            byte alpha = (byte)(255 * (1f - distFraction * distFraction)); // quadratic falloff

            RenderOffscreenIndicator(renderer, camera, transform.Position, new Color4(255, 80, 80, alpha));
        }
    }

    /// <summary>Render off-screen indicators for planets and stations in the solar system, fading by distance.</summary>
    public static void RenderSolarSystemObjectOffscreenIndicators(ISpriteRenderer renderer, Camera camera,
        Entity playerShip, World ecsWorld,
        List<Entity> planetEntities, List<PlanetData> planets,
        List<Entity> stationEntities, List<SpaceStationData> stations,
        float maxDistance = 5000f, PlayerData? player = null)
    {
        Vector2 playerPos = ecsWorld.IsAlive(playerShip)
            ? ecsWorld.Get<Transform>(playerShip).Position
            : camera.Position;

        // Planets
        for (int i = 0; i < planetEntities.Count; i++)
        {
            // Skip if this planet is the current nav target (target indicator takes priority)
            if (player is { Navigation: { HasTarget: true, Type: NavigationTargetType.Planet, PlanetIndex: var pi } } && pi == i)
                continue;
            if (!ecsWorld.IsAlive(planetEntities[i])) continue;
            var pos = ecsWorld.Get<Transform>(planetEntities[i]).Position;
            float dist = Vector2.Distance(playerPos, pos);
            if (dist > maxDistance) continue;
            float distFraction = dist / maxDistance;
            byte alpha = (byte)(255 * (1f - distFraction * distFraction));
            string name = i < planets.Count ? planets[i].Name.ToUpper() : "PLANET";
            byte pr = i < planets.Count ? planets[i].Color.R : (byte)180;
            byte pg = i < planets.Count ? planets[i].Color.G : (byte)180;
            byte pb = i < planets.Count ? planets[i].Color.B : (byte)180;
            RenderOffscreenIndicator(renderer, camera, pos, new Color4(pr, pg, pb, alpha),
                prefix: name + " ", arrowSize: 9f);
        }

        // Stations
        for (int i = 0; i < stationEntities.Count; i++)
        {
            // Skip if this station is the current nav target (target indicator takes priority)
            if (player is { Navigation: { HasTarget: true, Type: NavigationTargetType.SpaceStation, SpaceStationIndex: var si } } && si == i)
                continue;
            if (!ecsWorld.IsAlive(stationEntities[i])) continue;
            var pos = ecsWorld.Get<Transform>(stationEntities[i]).Position;
            float dist = Vector2.Distance(playerPos, pos);
            if (dist > maxDistance) continue;
            float distFraction = dist / maxDistance;
            byte alpha = (byte)(255 * (1f - distFraction * distFraction));
            string name = i < stations.Count ? stations[i].Name.ToUpper() : "SPACE STATION";
            RenderOffscreenIndicator(renderer, camera, pos, new Color4(100, 200, 255, alpha),
                prefix: name + " ", arrowSize: 9f);
        }
    }

    /// <summary>Render an off-screen indicator pointing toward the system's main star.</summary>
    public static void RenderStarOffscreenIndicator(ISpriteRenderer renderer, Camera camera,
        Vector2 starCenter)
    {
        RenderOffscreenIndicator(renderer, camera, starCenter, new Color3(255, 220, 80), prefix: "SUN ", arrowSize: 10f);
    }

    /// <summary>Render an off-screen indicator pointing toward the player's landed spaceship.</summary>
    public static void RenderShipOffscreenIndicator(ISpriteRenderer renderer, Camera camera,
        Vector2 shipWorldPos)
    {
        RenderOffscreenIndicator(renderer, camera, shipWorldPos, new Color3(120, 200, 255), prefix: "SHIP ", arrowSize: 10f);
    }

    /// <summary>Render off-screen indicators for settlements on a planet surface.</summary>
    public static void RenderSettlementOffscreenIndicators(ISpriteRenderer renderer, Camera camera,
        List<SettlementData> settlements, PlayerData? player = null)
    {
        foreach (var s in settlements)
        {
            // Skip if this settlement is the current nav target (target indicator takes priority)
            if (player is { Navigation: { HasTarget: true, Type: NavigationTargetType.SurfaceTarget } }
                && player.Navigation.Name == s.Name)
                continue;

            // Point toward the center of the settlement
            float cx = (s.TileRect.X + s.TileRect.Width / 2f) * GameConfig.TileSize;
            float cy = (s.TileRect.Y + s.TileRect.Height / 2f) * GameConfig.TileSize;
            RenderOffscreenIndicator(renderer, camera, new Vector2(cx, cy),
                new Color3(200, 180, 80), prefix: s.Name + " ", arrowSize: 8f);
        }
    }

    /// <summary>Render off-screen indicators pointing to mission target planets/stations in a solar system.</summary>
    public static void RenderSolarSystemMissionOffscreenIndicators(ISpriteRenderer renderer, Camera camera,
        PlayerData player, int systemIndex, List<Entity> stationEntities, List<Entity> planetEntities,
        World ecsWorld)
    {
        var missions = player.Missions.Active;
        if (missions.Count == 0) return;

        foreach (var mission in missions)
        {
            // Incomplete missions — point to objective
            if (mission.Target.IsSystem(systemIndex) && mission.Status != MissionStatus.Completed)
            {
                var mc = mission.TypeColor;
                switch (mission.Type)
                {
                    case MissionType.Delivery:
                        for (int s = 0; s < stationEntities.Count; s++)
                        {
                            if (!ecsWorld.IsAlive(stationEntities[s])) continue;
                            var pos = ecsWorld.Get<Transform>(stationEntities[s]).Position;
                            RenderOffscreenIndicator(renderer, camera, pos,
                                mc, prefix: mission.TypeLabel + " ", arrowSize: 10f);
                        }
                        break;

                    case MissionType.Exploration:
                    case MissionType.SettlementDelivery:
                        if (mission.Target.HasPlanet && mission.Target.PlanetIndex < planetEntities.Count)
                        {
                            var planetEntity = planetEntities[mission.Target.PlanetIndex];
                            if (ecsWorld.IsAlive(planetEntity))
                            {
                                var pos = ecsWorld.Get<Transform>(planetEntity).Position;
                                RenderOffscreenIndicator(renderer, camera, pos,
                                    mc, prefix: mission.TypeLabel + " ", arrowSize: 10f);
                            }
                        }
                        break;
                }
            }

            // Completed missions — point to turn-in station
            if (mission.Status == MissionStatus.Completed && mission.TurnIn.IsSystem(systemIndex))
            {
                for (int s = 0; s < stationEntities.Count; s++)
                {
                    if (!ecsWorld.IsAlive(stationEntities[s])) continue;
                    var pos = ecsWorld.Get<Transform>(stationEntities[s]).Position;
                    RenderOffscreenIndicator(renderer, camera, pos,
                        new Color3(100, 255, 100), prefix: "TURN IN ", arrowSize: 10f);
                }
            }
        }
    }

    /// <summary>Render off-screen indicators pointing to mission target settlements on a planet surface.</summary>
    public static void RenderPlanetSurfaceMissionOffscreenIndicators(ISpriteRenderer renderer, Camera camera,
        PlayerData player, int systemIndex, int planetIndex, List<SettlementData> settlements)
    {
        var missions = player.Missions.Active;
        if (missions.Count == 0 || settlements.Count == 0) return;

        foreach (var mission in missions)
        {
            if (mission.Status != MissionStatus.Completed
                && mission.Type == MissionType.SettlementDelivery
                && mission.Target.IsPlanet(systemIndex, planetIndex))
            {
                var mc = mission.TypeColor;
                foreach (var settlement in settlements)
                {
                    float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * GameConfig.TileSize;
                    float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * GameConfig.TileSize;
                    RenderOffscreenIndicator(renderer, camera, new Vector2(sx, sy),
                        mc, prefix: mission.TypeLabel + " ", arrowSize: 10f);
                }
            }
        }
    }

    /// <summary>Shared helper: renders a single off-screen edge indicator arrow with distance label.</summary>
    private static void RenderOffscreenIndicator(ISpriteRenderer renderer, Camera camera,
        Vector2 worldPos, Color4 color, string? prefix = null, float arrowSize = 8f)
    {
        const float margin = 30f;
        float screenW = GameConfig.WindowWidth;
        float screenH = GameConfig.WindowHeight;

        var screenPos = camera.WorldToScreen(worldPos);

        // Skip if on screen
        if (screenPos.X >= -20 && screenPos.X <= screenW + 20 &&
            screenPos.Y >= -20 && screenPos.Y <= screenH + 20)
            return;

        // Clamp to screen border
        float cx = screenW / 2f;
        float cy = screenH / 2f;
        float dx = screenPos.X - cx;
        float dy = screenPos.Y - cy;

        float halfW = cx - margin;
        float halfH = cy - margin;
        float scaleX = MathF.Abs(dx) > 0.001f ? halfW / MathF.Abs(dx) : float.MaxValue;
        float scaleY = MathF.Abs(dy) > 0.001f ? halfH / MathF.Abs(dy) : float.MaxValue;
        float scale = MathF.Min(scaleX, scaleY);

        // Indicator position at edge of screen (middle of the base of the arrow)
        float ix = cx + dx * scale;
        float iy = cy + dy * scale;

        float leftOrRight = Math.Clamp(dx / cx, -1.0f, 1.0f); // -1 on left edge, +1 on right edge
        float upOrDown = Math.Clamp(dy / cy, -1.0f, 1.0f);    // -1 on top edge, +1 on bottom edge

        // Triangle arrow pointing outward
        float angle = MathF.Atan2(dy, dx);
        float tipX = ix + MathF.Cos(angle) * arrowSize;
        float tipY = iy + MathF.Sin(angle) * arrowSize;
        float baseX1 = ix + MathF.Cos(angle + 2.5f) * arrowSize;
        float baseY1 = iy + MathF.Sin(angle + 2.5f) * arrowSize;
        float baseX2 = ix + MathF.Cos(angle - 2.5f) * arrowSize;
        float baseY2 = iy + MathF.Sin(angle - 2.5f) * arrowSize;

        byte a2 = (byte)Math.Min((int)color.A, 200);

        renderer.DrawFilledTriangleScreen(tipX, tipY, baseX1, baseY1, baseX2, baseY2, color);
        renderer.DrawFilledTriangleScreen(ix, iy, baseX1, baseY1, baseX2, baseY2, color.WithAlpha(a2));
        // Draw a black border around the arrow for better visibility
        renderer.DrawTriangleScreen(tipX, tipY, baseX1, baseY1, baseX2, baseY2, new Color4(0, 0, 0, a2));

        // Distance label: world distance from screen edge to target
        float screenPixelDist = Vector2.Distance(screenPos, new Vector2(ix, iy));
        float worldDist = screenPixelDist / camera.Zoom;
        string distText = worldDist < 1000 ? $"{worldDist:F0}" : $"{worldDist / 1000f:F1}K";
        string label = prefix != null ? prefix + distText : distText;

        const float labelFontScale = 1.3f;
        float labelW = renderer.MeasureText(label, labelFontScale);
        float labelH = labelFontScale * MiniBitmapFont.GlyphHeight;
        float labelOffX = -MathF.Cos(angle) * 20f - (1 + leftOrRight) * labelW / 2f;
        float labelOffY = -MathF.Sin(angle) * 20f - (1 + upOrDown) * labelH / 2f;
        float labelX = ix + labelOffX;
        float labelY = iy + labelOffY;

        // Draw a semi-transparent background for the label for better readability
        renderer.DrawRectScreen(labelX - 4, labelY - 2, labelW + 8, labelH + 4, new Color4(0, 0, 0, a2));
        renderer.DrawTextScreen(labelX, labelY, label, color, labelFontScale);
    }

    // ─────────────────────────────────────────────────────────────
    //  MISSION MARKERS (world-space indicators on targets)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws pulsing mission target markers on planets and stations in a solar system
    /// that are objectives or turn-in locations of the player's active missions.
    /// </summary>
    public static void RenderSolarSystemMissionMarkers(ISpriteRenderer renderer, Camera camera,
        PlayerData player, float globalTime,
        int systemIndex, List<Entity> stationEntities, List<Entity> planetEntities,
        List<PlanetData> planets, World ecsWorld)
    {
        var missions = player.Missions.Active;
        if (missions.Count == 0) return;

        float pulse = (float)(0.5 + 0.5 * Math.Sin(globalTime * 3.0));
        byte ringAlpha = (byte)(80 + (int)(pulse * 175));

        foreach (var mission in missions)
        {
            // Show objective markers for incomplete missions in this system
            if (mission.Target.IsSystem(systemIndex) && mission.Status != MissionStatus.Completed)
            {
                var mc = mission.TypeColor;
                var ringColor = new Color4(mc.R, mc.G, mc.B, ringAlpha);
                var glowColor = new Color4(mc.R, mc.G, mc.B, (byte)(30 + (int)(pulse * 40)));

                switch (mission.Type)
                {
                    case MissionType.Delivery:
                        // Highlight all stations in this system (player must dock at one)
                        for (int s = 0; s < stationEntities.Count; s++)
                        {
                            if (!ecsWorld.IsAlive(stationEntities[s])) continue;
                            var pos = ecsWorld.Get<Transform>(stationEntities[s]).Position;
                            float markerRadius = 24 + pulse * 6;
                            renderer.DrawCircle(camera, pos, markerRadius, ringColor);
                            renderer.DrawCircle(camera, pos, markerRadius + 3, glowColor);
                            renderer.DrawText(camera, pos + new Vector2(0, -markerRadius - 12),
                                $"[{mission.TypeLabel}]", mc.WithAlpha(ringAlpha), Math.Max(1f, camera.Zoom * 0.8f));
                        }
                        break;

                    case MissionType.Exploration:
                    case MissionType.SettlementDelivery:
                        // Highlight the specific target planet
                        if (mission.Target.HasPlanet && mission.Target.PlanetIndex < planetEntities.Count)
                        {
                            var planetEntity = planetEntities[mission.Target.PlanetIndex];
                            if (ecsWorld.IsAlive(planetEntity))
                            {
                                var pos = ecsWorld.Get<Transform>(planetEntity).Position;
                                float planetRadius = planets[mission.Target.PlanetIndex].Radius;
                                float markerRadius = planetRadius + 8 + pulse * 4;
                                renderer.DrawCircle(camera, pos, markerRadius, ringColor);
                                renderer.DrawCircle(camera, pos, markerRadius + 3, glowColor);
                                renderer.DrawText(camera, pos + new Vector2(0, -markerRadius - 12),
                                    $"[{mission.TypeLabel}]", mc.WithAlpha(ringAlpha), Math.Max(1f, camera.Zoom * 0.8f));
                            }
                        }
                        break;
                }
            }

            // Show turn-in markers on stations for completed missions in this system
            if (mission.Status == MissionStatus.Completed && mission.TurnIn.IsSystem(systemIndex))
            {
                var turnInRing = new Color4(100, 255, 100, ringAlpha);
                var turnInGlow = new Color4(100, 255, 100, (byte)(30 + (int)(pulse * 40)));

                for (int s = 0; s < stationEntities.Count; s++)
                {
                    if (!ecsWorld.IsAlive(stationEntities[s])) continue;
                    var pos = ecsWorld.Get<Transform>(stationEntities[s]).Position;
                    float markerRadius = 24 + pulse * 6;
                    renderer.DrawCircle(camera, pos, markerRadius, turnInRing);
                    renderer.DrawCircle(camera, pos, markerRadius + 3, turnInGlow);
                    renderer.DrawText(camera, pos + new Vector2(0, -markerRadius - 12),
                        "[TURN IN]", new Color3(100, 255, 100).WithAlpha(ringAlpha), Math.Max(1f, camera.Zoom * 0.8f));
                }
            }
        }
    }

    /// <summary>
    /// Draws pulsing mission markers on settlements on a planet surface
    /// that are objectives of the player's active SettlementDelivery missions.
    /// </summary>
    public static void RenderPlanetSurfaceMissionMarkers(ISpriteRenderer renderer, Camera camera,
        PlayerData player, float globalTime,
        int systemIndex, int planetIndex, List<SettlementData> settlements)
    {
        var missions = player.Missions.Active;
        if (missions.Count == 0 || settlements.Count == 0) return;

        float pulse = (float)(0.5 + 0.5 * Math.Sin(globalTime * 3.0));
        byte ringAlpha = (byte)(80 + (int)(pulse * 175));

        foreach (var mission in missions)
        {
            // Settlement delivery targets on this planet
            if (mission.Status != MissionStatus.Completed
                && mission.Type == MissionType.SettlementDelivery
                && mission.Target.IsPlanet(systemIndex, planetIndex))
            {
                var mc = mission.TypeColor;
                var ringColor = new Color4(mc.R, mc.G, mc.B, ringAlpha);

                foreach (var settlement in settlements)
                {
                    float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * GameConfig.TileSize;
                    float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * GameConfig.TileSize;
                    var pos = new Vector2(sx, sy);
                    float markerRadius = Math.Max(settlement.TileRect.Width, settlement.TileRect.Height) * GameConfig.TileSize / 2f + 8 + pulse * 4;
                    renderer.DrawCircle(camera, pos, markerRadius, ringColor);
                    renderer.DrawText(camera, pos + new Vector2(0, -markerRadius - 10),
                        $"[{mission.TypeLabel}]", mc.WithAlpha(ringAlpha), Math.Max(1f, camera.Zoom * 0.8f));
                }
            }
        }
    }

    // ── Navigation target markers ──────────────────────────────────────

    /// <summary>
    /// Render a prominent offscreen indicator for the player's current navigation target.
    /// </summary>
    public static void RenderNavTargetOffscreenIndicator(ISpriteRenderer renderer, Camera camera,
        Vector2 targetWorldPos, string targetName, Color3 targetColor)
    {
        RenderOffscreenIndicator(renderer, camera, targetWorldPos,
            targetColor, prefix: $"TARGET: {targetName}", arrowSize: 10f);
    }

    /// <summary>
    /// Render a pulsing world-space marker at the navigation target position on the planet surface.
    /// Shows concentric rings, a crosshair, and a label.
    /// </summary>
    public static void RenderSurfaceNavTargetMarker(ISpriteRenderer renderer, Camera camera,
        Vector2 targetWorldPos, string targetName, Color3 targetColor, float globalTime)
    {
        float pulse = (float)(0.5 + 0.5 * Math.Sin(globalTime * 3.0));
        byte alpha1 = (byte)(120 + (int)(pulse * 135));
        byte alpha2 = (byte)(60 + (int)(pulse * 80));

        // Pulsing rings
        float innerR = 12f + pulse * 4f;
        float outerR = innerR + 6f;
        renderer.DrawCircle(camera, targetWorldPos, innerR, new Color4(targetColor.R, targetColor.G, targetColor.B, alpha1));
        renderer.DrawCircle(camera, targetWorldPos, outerR, new Color4(targetColor.R, targetColor.G, targetColor.B, alpha2));

        // Crosshair lines
        float crossLen = 8f;
        var crossColor = new Color4(targetColor.R, targetColor.G, targetColor.B, alpha1);
        renderer.DrawLine(camera,
            targetWorldPos + new Vector2(-crossLen, 0), targetWorldPos + new Vector2(-innerR + 2, 0), crossColor);
        renderer.DrawLine(camera,
            targetWorldPos + new Vector2(crossLen, 0), targetWorldPos + new Vector2(innerR - 2, 0), crossColor);
        renderer.DrawLine(camera,
            targetWorldPos + new Vector2(0, -crossLen), targetWorldPos + new Vector2(0, -innerR + 2), crossColor);
        renderer.DrawLine(camera,
            targetWorldPos + new Vector2(0, crossLen), targetWorldPos + new Vector2(0, innerR - 2), crossColor);

        // Label above marker
        string label = $"[TARGET: {targetName}]";
        renderer.DrawText(camera, targetWorldPos + new Vector2(0, -outerR - 10),
            label, new Color4(targetColor.R, targetColor.G, targetColor.B, alpha1), Math.Max(1f, camera.Zoom * 0.8f));
    }
}
