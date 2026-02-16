using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Hud;

/// <summary>
/// Unified HUD renderer shared across all game states.
/// TOP-LEFT: location info, player stats (credits/cargo), health/shields.
/// TOP-RIGHT: minimap (delegated to HudMinimapRenderer).
/// </summary>
public static class HudRenderer
{
    // Layout constants
    private const float Padding = 10f;
    private const float LineHeight = 20f;
    private const float TextScale = 1.5f;
    private const float TitleScale = 2f;
    private const float BarWidth = 160f;
    private const float BarHeight = 12f;
    private const byte BgAlpha = 160;

    // ─────────────────────────────────────────────────────────────
    //  TOP-LEFT HUD
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render the unified top-left HUD for the solar system state.</summary>
    public static void RenderSolarSystemHud(SpriteRenderer renderer, PlayerData player,
        StarSystemData starSystem, World ecsWorld, Entity playerShip, float speed)
    {
        float y = Padding;

        // Line 1: Location
        string dangerStr = FormatDanger(starSystem.DangerLevel);
        string locationLine = $"{starSystem.Name}  |  CLASS {starSystem.StarClass} STAR  |  {dangerStr}  |  SPD {speed:F0}";
        y = RenderLocationLine(renderer, y, locationLine);

        // Line 2: Player info (credits + cargo)
        y = RenderPlayerInfoLine(renderer, y, player);

        // Line 3: Health / shields
        RenderShipHealthBars(renderer, y, player, ecsWorld, playerShip);

        // Mission tracker (below health bars)
        float missionY = y + LineHeight + 4 + (player.GetCombinedStats().ShieldStrength > 0 ? LineHeight + 4 : 0) + 4;
        RenderMissionTracker(renderer, missionY, player);
    }

    /// <summary>Render the unified top-left HUD for the planet surface state.</summary>
    public static void RenderPlanetSurfaceHud(SpriteRenderer renderer, PlayerData player,
        PlanetData planet, int dangerLevel, bool inVehicle,
        World ecsWorld, Entity playerAvatar)
    {
        float y = Padding;

        // Line 1: Location
        string dangerStr = FormatDanger(dangerLevel);
        string mode = inVehicle ? "VEHICLE" : "ON FOOT";
        string locationLine = $"{planet.Name.ToUpper()}  |  {planet.Type.ToString().ToUpper()}  |  {dangerStr}  |  {mode}";
        y = RenderLocationLine(renderer, y, locationLine);

        // Line 2: Player info (credits + cargo)
        y = RenderPlayerInfoLine(renderer, y, player);

        // Line 3: Avatar health
        if (ecsWorld.IsAlive(playerAvatar) && ecsWorld.Has<Health>(playerAvatar))
        {
            var health = ecsWorld.Get<Health>(playerAvatar);
            RenderHealthBar(renderer, y, "HP", health.Hull, health.MaxHull,
                HPBarColor(health.HullPercent), null, null);
            y += LineHeight + 8;
        }

        // Mission tracker
        RenderMissionTracker(renderer, y, player);
    }

    /// <summary>Render the unified top-left HUD for the interior state.</summary>
    public static void RenderInteriorHud(SpriteRenderer renderer, PlayerData player,
        InteriorData interior, StarSystemData starSystem)
    {
        float y = Padding;

        // Line 1: Location
        string typeLabel = interior.Type == InteriorType.Station ? "STATION" : "SETTLEMENT";
        string dangerStr = FormatDanger(starSystem.DangerLevel);
        string locationLine = $"{interior.Name.ToUpper()}  |  {typeLabel}  |  {starSystem.Name}  |  {dangerStr}";
        y = RenderLocationLine(renderer, y, locationLine);

        // Line 2: Player info (credits + cargo)
        y = RenderPlayerInfoLine(renderer, y, player);

        // Line 3: Avatar health
        RenderHealthBar(renderer, y, "HP", player.AvatarHealth, player.AvatarMaxHealth,
            HPBarColor(player.AvatarMaxHealth > 0 ? player.AvatarHealth / player.AvatarMaxHealth : 1f),
            null, null);
        y += LineHeight + 8;

        // Mission tracker
        RenderMissionTracker(renderer, y, player);
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render the location info line. Returns the Y position for the next line.</summary>
    private static float RenderLocationLine(SpriteRenderer renderer, float y, string text)
    {
        float textW = renderer.MeasureText(text, TextScale);
        float bgW = Math.Max(textW + Padding * 2, 300f);
        renderer.DrawRectScreen(0, y, bgW, LineHeight + 4, new Color4(0, 0, 0, BgAlpha));
        renderer.DrawTextScreen(Padding, y + 2, text, new Color3(200, 200, 255), TextScale);
        return y + LineHeight + 6;
    }

    /// <summary>Render credits and cargo line. Returns the Y position for the next line.</summary>
    private static float RenderPlayerInfoLine(SpriteRenderer renderer, float y, PlayerData player)
    {
        string info = $"CREDITS: {player.Credits}  |  CARGO: {player.CargoUsed}/{player.MaxCargo}  |  FUEL: {player.ShipFuel:F0}/{player.ShipMaxFuel:F0}";
        float textW = renderer.MeasureText(info, TextScale);
        float bgW = Math.Max(textW + Padding * 2, 300f);
        renderer.DrawRectScreen(0, y, bgW, LineHeight + 4, new Color4(0, 0, 0, BgAlpha));
        renderer.DrawTextScreen(Padding, y + 2, info, new Color3(255, 220, 80), TextScale);
        return y + LineHeight + 6;
    }

    /// <summary>Render a health bar (and optional shield bar) for ship combat HUD.</summary>
    private static void RenderShipHealthBars(SpriteRenderer renderer, float y,
        PlayerData player, World ecsWorld, Entity playerShip)
    {
        // Hull bar
        float hullPct = player.ShipMaxHealth > 0 ? player.ShipHealth / player.ShipMaxHealth : 0;
        var hullColor = HPBarColor(hullPct);
        RenderHealthBar(renderer, y, "HULL", player.ShipHealth, player.ShipMaxHealth, hullColor, null, null);

        // Shield bar (if player has shield)
        var stats = player.GetCombinedStats();
        if (stats.ShieldStrength > 0 && ecsWorld.IsAlive(playerShip) && ecsWorld.Has<Health>(playerShip))
        {
            ref var health = ref ecsWorld.Get<Health>(playerShip);
            RenderHealthBar(renderer, y + LineHeight + 4, "SHLD", health.Shield, health.MaxShield,
                new Color3(80, 160, 255), null, null);
        }
    }

    /// <summary>Render a single health/shield bar with label and numeric text.</summary>
    private static void RenderHealthBar(SpriteRenderer renderer, float y,
        string label, float current, float max,
        Color3 fillColor,
        Color3? labelColor,
        Color3? textColor)
    {
        var lc = labelColor ?? new Color3(200, 200, 200);
        var tc = textColor ?? new Color3(200, 200, 200);

        float labelW = renderer.MeasureText(label, TextScale) + 8;
        float totalW = labelW + BarWidth + 80;
        renderer.DrawRectScreen(0, y, totalW, BarHeight + 8, new Color4(0, 0, 0, BgAlpha));

        // Label
        renderer.DrawTextScreen(Padding, y + 2, label, lc, TextScale);

        // Bar background
        float barX = Padding + labelW;
        renderer.DrawRectScreen(barX, y + 4, BarWidth, BarHeight, new Color3(40, 40, 40));

        // Bar fill
        float pct = max > 0 ? current / max : 0;
        renderer.DrawRectScreen(barX, y + 4, BarWidth * pct, BarHeight, fillColor);

        // Numeric
        renderer.DrawTextScreen(barX + BarWidth + 5, y + 2,
            $"{(int)current}/{(int)max}", tc, TextScale);
    }

    /// <summary>Calculate the hull bar color based on current percentage.</summary>
    private static Color3 HPBarColor(float pct)
    {
        byte r = pct > 0.5f ? (byte)(255 * (1 - pct) * 2) : (byte)255;
        byte g = pct > 0.5f ? (byte)255 : (byte)(255 * pct * 2);
        return new Color3(r, g, 0);
    }

    /// <summary>Format danger level with color-coded text.</summary>
    private static string FormatDanger(int dangerLevel) => $"DANGER LV.{dangerLevel}";

    /// <summary>Render a compact mission tracker showing the most urgent active mission.</summary>
    private static void RenderMissionTracker(SpriteRenderer renderer, float y, PlayerData player)
    {
        var tracked = player.GetTrackedMission();
        if (tracked == null) return;

        bool completed = tracked.Status == MissionStatus.Completed;

        // Build mission text
        string statusIcon = completed ? ">> " : "* ";
        string missionText = $"{statusIcon}[{tracked.TypeLabel}] {tracked.Title}";
        string progressText = tracked.ProgressText;

        // Measure and draw background
        float textW1 = renderer.MeasureText(missionText, TextScale);
        float textW2 = renderer.MeasureText(progressText, TextScale);
        float bgW = Math.Max(Math.Max(textW1, textW2) + Padding * 2, 300f);

        renderer.DrawRectScreen(0, y, bgW, LineHeight * 2 + 4, new Color4(0, 0, 0, BgAlpha));

        // Mission title
        renderer.DrawTextScreen(Padding, y + 2, missionText,
            completed ? new Color3(100, 255, 100) : tracked.TypeColor, TextScale);

        // Progress
        renderer.DrawTextScreen(Padding + 10, y + LineHeight + 2, progressText,
            completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), TextScale);

        // Extra missions indicator
        int totalActive = player.ActiveMissions.Count;
        if (totalActive > 1)
        {
            int completedCount = player.ActiveMissions.Count(m => m.Status == MissionStatus.Completed);
            string extra = completedCount > 0
                ? $"+{totalActive - 1} MORE ({completedCount} READY)"
                : $"+{totalActive - 1} MORE";
            renderer.DrawTextScreen(Padding, y + LineHeight * 2 + 4, extra,
                new Color3(120, 120, 150), 1.2f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  INTERACTION PROMPTS (bottom-center)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render a single interaction prompt centered at the bottom of the screen.</summary>
    public static void RenderPrompt(SpriteRenderer renderer, string text,
        byte r = 100, byte g = 255, byte b = 200)
    {
        float tw = renderer.MeasureText(text, TitleScale);
        float w = GameConfig.WindowWidth;
        float h = GameConfig.WindowHeight;
        renderer.DrawRectScreen(w / 2f - tw / 2f - 10, h - 60, tw + 20, 35, new Color4(0, 0, 0, 180));
        renderer.DrawTextScreen(w / 2f - tw / 2f, h - 55, text, new Color3(r, g, b), TitleScale);
    }

    /// <summary>Render a multi-line interaction panel centered at the bottom of the screen.</summary>
    public static void RenderPromptPanel(SpriteRenderer renderer, string[] lines,
        Color3[] colors)
    {
        float w = GameConfig.WindowWidth;
        float h = GameConfig.WindowHeight;

        // Measure widest line
        float maxW = 0;
        foreach (var line in lines)
        {
            float lw = renderer.MeasureText(line, TextScale);
            if (lw > maxW) maxW = lw;
        }
        // First line uses TitleScale
        float firstW = renderer.MeasureText(lines[0], TitleScale);
        if (firstW > maxW) maxW = firstW;

        float panelW = Math.Max(maxW + 20, 280);
        float panelH = 28 + (lines.Length - 1) * 18 + 10;
        float px = w / 2f - panelW / 2f;
        float py = h - panelH - 15;
        renderer.DrawRectScreen(px, py, panelW, panelH, new Color4(0, 0, 0, 180));

        // First line (action) at title scale
        var c0 = colors[0];
        renderer.DrawTextScreen(px + 10, py + 6, lines[0], c0, TitleScale);

        // Remaining lines at text scale
        for (int i = 1; i < lines.Length; i++)
        {
            var c = i < colors.Length ? colors[i] : new Color3(150, 150, 150);
            renderer.DrawTextScreen(px + 10, py + 6 + 24 + (i - 1) * 18, lines[i], c, TextScale);
        }
    }

    /// <summary>Render solar system interaction prompts (planet, moon, station panels).</summary>
    public static void RenderSolarSystemPrompt(SpriteRenderer renderer,
        int nearbyPlanetIndex, int nearbyMoonIndex, int nearbyMoonPlanetIndex,
        int nearbyStationIndex,
        List<PlanetData> planets, List<SpaceStationData> stations)
    {
        if (nearbyPlanetIndex >= 0)
        {
            var planet = planets[nearbyPlanetIndex];
            string details = $"MOONS: {planet.MoonCount}";
            if (planet.HasRings) details += "  RINGS: YES";
            string settText = planet.HasSettlement ? "SETTLEMENTS: YES" : "NO SETTLEMENTS";

            RenderPromptPanel(renderer,
                [$"[E] LAND ON {planet.Name.ToUpper()}",
                 $"TYPE: {planet.Type.ToString().ToUpper()}",
                 details,
                 settText],
                [new Color3(100, 255, 100),
                 new Color3(180, 180, 180),
                 new Color3(150, 150, 150),
                 planet.HasSettlement ? new Color3(255, 220, 100) : new Color3(120, 120, 120)]);
        }
        else if (nearbyMoonIndex >= 0 && nearbyMoonPlanetIndex >= 0
            && nearbyMoonPlanetIndex < planets.Count
            && nearbyMoonIndex < planets[nearbyMoonPlanetIndex].Moons.Count)
        {
            var moon = planets[nearbyMoonPlanetIndex].Moons[nearbyMoonIndex];
            var parent = planets[nearbyMoonPlanetIndex];
            RenderPromptPanel(renderer,
                [$"[E] LAND ON {moon.Name.ToUpper()}",
                 $"TYPE: {moon.Type.ToString().ToUpper()}",
                 $"ORBITS: {parent.Name.ToUpper()}"],
                [new Color3(180, 255, 180),
                 new Color3(180, 180, 180),
                 new Color3(150, 150, 150)]);
        }
        else if (nearbyStationIndex >= 0)
        {
            RenderPrompt(renderer, $"[E] DOCK AT {stations[nearbyStationIndex].Name.ToUpper()}",
                100, 200, 255);
        }
    }

    /// <summary>Render planet surface interaction prompts (board ship, mount vehicle, enter settlement).</summary>
    public static void RenderPlanetSurfacePrompt(SpriteRenderer renderer,
        bool inVehicle, bool nearShip, bool nearVehicle, bool vehicleDeployed,
        SettlementData? nearSettlement)
    {
        if (inVehicle && nearShip)
            RenderPrompt(renderer, "[E] BOARD STARSHIP", 100, 255, 100);
        else if (inVehicle)
            RenderPrompt(renderer, "[E] DISMOUNT", 255, 200, 100);
        else if (nearShip)
            RenderPrompt(renderer, "[E] BOARD STARSHIP", 100, 255, 100);
        else if (nearVehicle && vehicleDeployed)
            RenderPrompt(renderer, "[E] MOUNT VEHICLE", 255, 200, 100);
        else if (nearSettlement != null)
            RenderPrompt(renderer, $"[E] ENTER {nearSettlement.Name.ToUpper()}", 255, 255, 100);
    }

    /// <summary>Render interior interaction prompts (interactables and NPCs).</summary>
    public static void RenderInteriorPrompt(SpriteRenderer renderer,
        InteriorInteractable? nearestInteractable, InteriorNpc? nearestNpc)
    {
        if (nearestInteractable != null)
        {
            string prompt = nearestInteractable.Type switch
            {
                InteractableType.ExitDoor => "[E] EXIT",
                InteractableType.RepairStation => "[E] REPAIR",
                InteractableType.HealthStation => "[E] HEALTH STATION",
                InteractableType.MissionBoard => "[E] MISSIONS",
                InteractableType.ShipCustomization => "[E] SHIP CUSTOMIZATION",
                InteractableType.AvatarCustomization => "[E] AVATAR CUSTOMIZATION",
                InteractableType.VehicleCustomization => "[E] VEHICLE CUSTOMIZATION",
                InteractableType.ShipDealer => "[E] SHIP DEALER",
                InteractableType.CargoTerminal => "[E] SELL CARGO",
                _ => "[E] INTERACT"
            };
            RenderPrompt(renderer, prompt, 100, 255, 200);
        }
        else if (nearestNpc != null)
        {
            RenderPrompt(renderer, $"[E] TALK TO {nearestNpc.Name.ToUpper()}", 200, 200, 255);
        }
    }

    // ── Off-screen indicators ──────────────────────────────────────────

    /// <summary>Render arrow indicators at screen edges for off-screen NPC ships within range.</summary>
    public static void RenderOffscreenIndicators(SpriteRenderer renderer, Camera camera, World ecsWorld,
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

            ref var transform = ref ecsWorld.Get<Transform>(entity);

            // Skip ships beyond max distance
            float dist = Vector2.Distance(playerPos, transform.Position);
            if (dist > maxDistance) continue;

            var ai = ecsWorld.Get<EnemyAI>(entity);

            var (cr, cg, cb) = ai.Config.Faction switch
            {
                Faction.Pirate => new Color3(255, 80, 80),
                Faction.Trader => new Color3(200, 180, 80),
                Faction.Patrol => new Color3(80, 160, 255),
                _ => new Color3(200, 200, 200)
            };

            RenderOffscreenIndicator(renderer, camera, transform.Position, cr, cg, cb);
        }
    }

    /// <summary>Render an off-screen indicator pointing toward the system's main star.</summary>
    public static void RenderStarOffscreenIndicator(SpriteRenderer renderer, Camera camera,
        Vector2 starCenter)
    {
        RenderOffscreenIndicator(renderer, camera, starCenter, 255, 220, 80, prefix: "* ", dotRadius: 4f, arrowSize: 10f);
    }

    /// <summary>Render an off-screen indicator pointing toward the player's landed spaceship.</summary>
    public static void RenderShipOffscreenIndicator(SpriteRenderer renderer, Camera camera,
        Vector2 shipWorldPos)
    {
        RenderOffscreenIndicator(renderer, camera, shipWorldPos, 120, 200, 255, prefix: "SHIP ", dotRadius: 4f, arrowSize: 10f);
    }

    /// <summary>Render off-screen indicators for settlements on a planet surface.</summary>
    public static void RenderSettlementOffscreenIndicators(SpriteRenderer renderer, Camera camera,
        List<SettlementData> settlements)
    {
        foreach (var s in settlements)
        {
            // Point toward the center of the settlement
            float cx = (s.TileRect.X + s.TileRect.Width / 2f) * GameConfig.TileSize;
            float cy = (s.TileRect.Y + s.TileRect.Height / 2f) * GameConfig.TileSize;
            RenderOffscreenIndicator(renderer, camera, new Vector2(cx, cy),
                200, 180, 80, prefix: s.Name + " ", dotRadius: 3f, arrowSize: 8f);
        }
    }

    /// <summary>Render off-screen indicators pointing to mission target planets/stations in a solar system.</summary>
    public static void RenderSolarSystemMissionOffscreenIndicators(SpriteRenderer renderer, Camera camera,
        PlayerData player, int systemIndex, List<Entity> stationEntities, List<Entity> planetEntities,
        List<PlanetData> planets, World ecsWorld)
    {
        var missions = player.ActiveMissions;
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
                                mc.R, mc.G, mc.B, prefix: mission.TypeLabel + " ", dotRadius: 4f, arrowSize: 10f);
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
                                    mc.R, mc.G, mc.B, prefix: mission.TypeLabel + " ", dotRadius: 4f, arrowSize: 10f);
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
                        100, 255, 100, prefix: "TURN IN ", dotRadius: 4f, arrowSize: 10f);
                }
            }
        }
    }

    /// <summary>Render off-screen indicators pointing to mission target settlements on a planet surface.</summary>
    public static void RenderPlanetSurfaceMissionOffscreenIndicators(SpriteRenderer renderer, Camera camera,
        PlayerData player, int systemIndex, int planetIndex, List<SettlementData> settlements)
    {
        var missions = player.ActiveMissions;
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
                        mc.R, mc.G, mc.B, prefix: mission.TypeLabel + " ", dotRadius: 4f, arrowSize: 10f);
                }
            }
        }
    }

    /// <summary>Shared helper: renders a single off-screen edge indicator arrow with distance label.</summary>
    private static void RenderOffscreenIndicator(SpriteRenderer renderer, Camera camera,
        Vector2 worldPos, byte cr, byte cg, byte cb, string? prefix = null,
        float dotRadius = 3f, float arrowSize = 8f)
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

        float ix = cx + dx * scale;
        float iy = cy + dy * scale;

        // Triangle arrow pointing outward
        float angle = MathF.Atan2(dy, dx);
        float tipX = ix + MathF.Cos(angle) * arrowSize;
        float tipY = iy + MathF.Sin(angle) * arrowSize;
        float baseX1 = ix + MathF.Cos(angle + 2.5f) * arrowSize;
        float baseY1 = iy + MathF.Sin(angle + 2.5f) * arrowSize;
        float baseX2 = ix + MathF.Cos(angle - 2.5f) * arrowSize;
        float baseY2 = iy + MathF.Sin(angle - 2.5f) * arrowSize;

        renderer.DrawLineScreen(tipX, tipY, baseX1, baseY1, new Color4(cr, cg, cb, 255));
        renderer.DrawLineScreen(tipX, tipY, baseX2, baseY2, new Color4(cr, cg, cb, 255));
        renderer.DrawLineScreen(baseX1, baseY1, baseX2, baseY2, new Color4(cr, cg, cb, 255));
        renderer.DrawLineScreen(ix, iy, tipX, tipY, new Color4(cr, cg, cb, 255));
        renderer.DrawLineScreen(ix, iy, baseX1, baseY1, new Color4(cr, cg, cb, 200));
        renderer.DrawLineScreen(ix, iy, baseX2, baseY2, new Color4(cr, cg, cb, 200));

        renderer.DrawFilledCircleScreen(ix, iy, dotRadius, new Color4(cr, cg, cb, 220));

        // Distance label: world distance from screen edge to target
        float screenPixelDist = Vector2.Distance(screenPos, new Vector2(ix, iy));
        float worldDist = screenPixelDist / camera.Zoom;
        string distText = worldDist < 1000 ? $"{worldDist:F0}" : $"{worldDist / 1000f:F1}K";
        string label = prefix != null ? prefix + distText : distText;

        float labelW = renderer.MeasureText(label, 1f);
        float labelOffX = -MathF.Cos(angle) * 16f - labelW / 2f;
        float labelOffY = -MathF.Sin(angle) * 16f - 4f;
        renderer.DrawTextScreen(ix + labelOffX, iy + labelOffY, label, new Color3(cr, cg, cb), 1f);
    }

    // ─────────────────────────────────────────────────────────────
    //  MISSION MARKERS (world-space indicators on targets)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws pulsing mission target markers on planets and stations in a solar system
    /// that are objectives or turn-in locations of the player's active missions.
    /// </summary>
    public static void RenderSolarSystemMissionMarkers(SpriteRenderer renderer, Camera camera,
        PlayerData player, float globalTime,
        int systemIndex, List<Entity> stationEntities, List<Entity> planetEntities,
        List<PlanetData> planets, World ecsWorld)
    {
        var missions = player.ActiveMissions;
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
    public static void RenderPlanetSurfaceMissionMarkers(SpriteRenderer renderer, Camera camera,
        PlayerData player, float globalTime,
        int systemIndex, int planetIndex, List<SettlementData> settlements)
    {
        var missions = player.ActiveMissions;
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

    // ─────────────────────────────────────────────────────────────
    //  LANDING / TAKEOFF ANIMATION HUD
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the centered landing or takeoff animation overlay
    /// (dim, timer text, progress bar).
    /// </summary>
    public static void RenderLandingTakeoffOverlay(SpriteRenderer renderer,
        bool isLanding, float progress, float remainingSeconds)
    {
        // Darken screen — fade in for takeoff, fade out for landing
        float dimProgress = isLanding ? (1f - progress) : progress;
        byte dimAlpha = (byte)(dimProgress * 120);
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, new Color4(0, 0, 0, dimAlpha));

        // Timer display
        string label = isLanding ? "LANDING..." : "TAKING OFF...";
        string timerText = $"{label} {remainingSeconds:F1}s";
        float timerScale = 2.5f;
        float timerW = renderer.MeasureText(timerText, timerScale);
        float timerX = GameConfig.WindowWidth / 2f - timerW / 2f;
        float timerY = GameConfig.WindowHeight / 2f - 40;
        var timerColor = isLanding ? new Color3(100, 255, 200) : new Color3(100, 200, 255);
        renderer.DrawRectScreen(timerX - 10, timerY - 6, timerW + 20, 34, new Color4(0, 0, 0, 200));
        renderer.DrawTextScreen(timerX, timerY, timerText, timerColor, timerScale);

        // Progress bar
        float barW = 200f;
        float barH = 6f;
        float barX = GameConfig.WindowWidth / 2f - barW / 2f;
        float barY = timerY + 36;
        var barColor = isLanding ? new Color4(100, 255, 200, 220) : new Color4(100, 200, 255, 220);
        renderer.DrawRectScreen(barX, barY, barW, barH, new Color4(40, 40, 60, 200));
        renderer.DrawRectScreen(barX, barY, barW * progress, barH, barColor);
    }
}
