using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Overlays.Base;

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
    private const float HudMargin = 10f;
    private const float SepGap = 6f;

    // ─────────────────────────────────────────────────────────────
    //  TOP-LEFT HUD
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render the unified top-left HUD for the solar system state.</summary>
    public static void RenderSolarSystemHud(SpriteRenderer renderer, PlayerData player,
        StarSystemData starSystem, World ecsWorld, Entity playerShip, float speed)
    {
        string dangerStr = FormatDanger(starSystem.DangerLevel);
        string locationLine = $"{starSystem.Name}  |  CLASS {starSystem.StarClass} STAR  |  {dangerStr}  |  SPD {speed:F0}";
        string infoLine = FormatPlayerInfo(player);
        var tracked = player.GetTrackedMission();
        var stats = player.GetCombinedStats();
        bool hasShield = stats.ShieldStrength > 0 && ecsWorld.IsAlive(playerShip) && ecsWorld.Has<Health>(playerShip);

        // Measure panel width
        float panelW = MeasureHudPanelWidth(renderer, locationLine, infoLine, tracked);

        // Calculate panel height
        float panelH = Padding
            + LineHeight + SepGap   // location + sep
            + LineHeight + SepGap   // info + sep
            + BarHeight + 8;        // hull bar
        if (hasShield) panelH += BarHeight + 4;
        if (tracked != null)
        {
            panelH += SepGap + LineHeight * 2 + 4;
            if (player.ActiveMissions.Count > 1) panelH += LineHeight;
        }
        panelH += Padding;

        // Frame
        OverlayBase.DrawFrame(renderer, HudMargin, HudMargin, panelW, panelH);

        // Content
        float cx = HudMargin + Padding;
        float y = HudMargin + Padding;

        renderer.DrawTextScreen(cx, y, locationLine, new Color3(200, 200, 255), TextScale);
        y += LineHeight;
        DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
        y += SepGap;

        renderer.DrawTextScreen(cx, y, infoLine, new Color3(255, 220, 80), TextScale);
        y += LineHeight;
        DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
        y += SepGap;

        float hullPct = player.ShipMaxHealth > 0 ? player.ShipHealth / player.ShipMaxHealth : 0;
        RenderBarContent(renderer, cx, y, "HULL", player.ShipHealth, player.ShipMaxHealth, HPBarColor(hullPct));
        y += BarHeight + 8;

        if (hasShield)
        {
            ref var health = ref ecsWorld.Get<Health>(playerShip);
            RenderBarContent(renderer, cx, y, "SHLD", health.Shield, health.MaxShield, new Color3(80, 160, 255));
            y += BarHeight + 4;
        }

        if (tracked != null)
        {
            DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
            y += SepGap;
            RenderMissionContent(renderer, cx, y, player);
        }
    }

    /// <summary>Render the unified top-left HUD for the planet surface state.</summary>
    public static void RenderPlanetSurfaceHud(SpriteRenderer renderer, PlayerData player,
        PlanetData planet, int dangerLevel, bool inVehicle,
        World ecsWorld, Entity playerAvatar)
    {
        string dangerStr = FormatDanger(dangerLevel);
        string mode = inVehicle ? "VEHICLE" : "ON FOOT";
        string locationLine = $"{planet.Name.ToUpper()}  |  {planet.Type.ToString().ToUpper()}  |  {dangerStr}  |  {mode}";
        string infoLine = FormatPlayerInfo(player);
        var tracked = player.GetTrackedMission();
        bool hasHealth = ecsWorld.IsAlive(playerAvatar) && ecsWorld.Has<Health>(playerAvatar);
        Health avatarHealth = default;
        if (hasHealth) avatarHealth = ecsWorld.Get<Health>(playerAvatar);

        // Measure panel width
        float panelW = MeasureHudPanelWidth(renderer, locationLine, infoLine, tracked);

        // Calculate panel height
        float panelH = Padding
            + LineHeight + SepGap   // location + sep
            + LineHeight + SepGap;  // info + sep
        if (hasHealth) panelH += BarHeight + 8;
        if (tracked != null)
        {
            panelH += SepGap + LineHeight * 2 + 4;
            if (player.ActiveMissions.Count > 1) panelH += LineHeight;
        }
        panelH += Padding;

        // Frame
        OverlayBase.DrawFrame(renderer, HudMargin, HudMargin, panelW, panelH);

        // Content
        float cx = HudMargin + Padding;
        float y = HudMargin + Padding;

        renderer.DrawTextScreen(cx, y, locationLine, new Color3(200, 200, 255), TextScale);
        y += LineHeight;
        DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
        y += SepGap;

        renderer.DrawTextScreen(cx, y, infoLine, new Color3(255, 220, 80), TextScale);
        y += LineHeight;

        if (hasHealth)
        {
            DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
            y += SepGap;
            RenderBarContent(renderer, cx, y, "HP", avatarHealth.Hull, avatarHealth.MaxHull,
                HPBarColor(avatarHealth.HullPercent));
            y += BarHeight + 8;
        }

        if (tracked != null)
        {
            DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
            y += SepGap;
            RenderMissionContent(renderer, cx, y, player);
        }
    }

    /// <summary>Render the unified top-left HUD for the interior state.</summary>
    public static void RenderInteriorHud(SpriteRenderer renderer, PlayerData player,
        InteriorData interior, StarSystemData starSystem)
    {
        string typeLabel = interior.Type == InteriorType.Station ? "STATION" : "SETTLEMENT";
        string dangerStr = FormatDanger(starSystem.DangerLevel);
        string locationLine = $"{interior.Name.ToUpper()}  |  {typeLabel}  |  {starSystem.Name}  |  {dangerStr}";
        string infoLine = FormatPlayerInfo(player);
        var tracked = player.GetTrackedMission();

        // Measure panel width
        float panelW = MeasureHudPanelWidth(renderer, locationLine, infoLine, tracked);

        // Calculate panel height
        float panelH = Padding
            + LineHeight + SepGap   // location + sep
            + LineHeight + SepGap   // info + sep
            + BarHeight + 8;        // HP bar
        if (tracked != null)
        {
            panelH += SepGap + LineHeight * 2 + 4;
            if (player.ActiveMissions.Count > 1) panelH += LineHeight;
        }
        panelH += Padding;

        // Frame
        OverlayBase.DrawFrame(renderer, HudMargin, HudMargin, panelW, panelH);

        // Content
        float cx = HudMargin + Padding;
        float y = HudMargin + Padding;

        renderer.DrawTextScreen(cx, y, locationLine, new Color3(200, 200, 255), TextScale);
        y += LineHeight;
        DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
        y += SepGap;

        renderer.DrawTextScreen(cx, y, infoLine, new Color3(255, 220, 80), TextScale);
        y += LineHeight;
        DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
        y += SepGap;

        RenderBarContent(renderer, cx, y, "HP", player.AvatarHealth, player.AvatarMaxHealth,
            HPBarColor(player.AvatarMaxHealth > 0 ? player.AvatarHealth / player.AvatarMaxHealth : 1f));
        y += BarHeight + 8;

        if (tracked != null)
        {
            DrawHudSeparator(renderer, HudMargin + 1, y + 1, panelW - 2);
            y += SepGap;
            RenderMissionContent(renderer, cx, y, player);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Format the player info string (credits, cargo, fuel).</summary>
    private static string FormatPlayerInfo(PlayerData player) =>
        $"CREDITS: {player.Credits}  |  CARGO: {player.CargoUsed}/{player.MaxCargo}  |  FUEL: {player.ShipFuel:F0}/{player.ShipMaxFuel:F0}";

    /// <summary>Measure the required panel width for the HUD based on content.</summary>
    private static float MeasureHudPanelWidth(SpriteRenderer renderer, string locationLine,
        string infoLine, object? tracked)
    {
        float maxW = Math.Max(renderer.MeasureText(locationLine, TextScale),
                              renderer.MeasureText(infoLine, TextScale));
        float barW = renderer.MeasureText("HULL", TextScale) + 8 + BarWidth + 80;
        maxW = Math.Max(maxW, barW);
        return Math.Max(maxW, 380f) + Padding * 2;
    }

    /// <summary>Draw a thin separator line inside a HUD panel.</summary>
    private static void DrawHudSeparator(SpriteRenderer renderer, float x, float y, float w)
    {
        renderer.DrawRectScreen(x, y, w, 1, new Color4(60, 80, 140, 150));
    }

    /// <summary>Render a health/shield bar content (label + bar + numeric, no background).</summary>
    private static void RenderBarContent(SpriteRenderer renderer, float x, float y,
        string label, float current, float max, Color3 fillColor)
    {
        var lc = new Color3(200, 200, 200);
        float labelW = renderer.MeasureText(label, TextScale) + 8;
        renderer.DrawTextScreen(x, y + 2, label, lc, TextScale);

        float barX = x + labelW;
        renderer.DrawRectScreen(barX, y + 4, BarWidth, BarHeight, new Color3(40, 40, 40));
        float pct = max > 0 ? current / max : 0;
        renderer.DrawRectScreen(barX, y + 4, BarWidth * pct, BarHeight, fillColor);
        renderer.DrawTextScreen(barX + BarWidth + 5, y + 2,
            $"{(int)current}/{(int)max}", lc, TextScale);
    }

    /// <summary>Render mission tracker content (no background).</summary>
    private static void RenderMissionContent(SpriteRenderer renderer, float x, float y, PlayerData player)
    {
        var tracked = player.GetTrackedMission();
        if (tracked == null) return;

        bool completed = tracked.Status == MissionStatus.Completed;
        string statusIcon = completed ? ">> " : "* ";
        string missionText = $"{statusIcon}[{tracked.TypeLabel}] {tracked.Title}";

        renderer.DrawTextScreen(x, y + 2, missionText,
            completed ? new Color3(100, 255, 100) : tracked.TypeColor, TextScale);
        renderer.DrawTextScreen(x + 10, y + LineHeight + 2, tracked.ProgressText,
            completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), TextScale);

        int totalActive = player.ActiveMissions.Count;
        if (totalActive > 1)
        {
            int completedCount = player.ActiveMissions.Count(m => m.Status == MissionStatus.Completed);
            string extra = completedCount > 0
                ? $"+{totalActive - 1} MORE ({completedCount} READY)"
                : $"+{totalActive - 1} MORE";
            renderer.DrawTextScreen(x, y + LineHeight * 2 + 4, extra,
                new Color3(120, 120, 150), 1.2f);
        }
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
        float panelW = tw + 20;
        float panelH = 35;
        float px = w / 2f - panelW / 2f;
        float py = h - panelH - HudMargin;
        OverlayBase.DrawFrame(renderer, px, py, panelW, panelH);
        renderer.DrawTextScreen(px + 10, py + 8, text, new Color3(r, g, b), TitleScale);
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
        float py = h - panelH - HudMargin;
        OverlayBase.DrawFrame(renderer, px, py, panelW, panelH);

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

    /// <summary>Render arrow indicators at screen edges for off-screen hostile NPC ships within range.</summary>
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

            RenderOffscreenIndicator(renderer, camera, transform.Position, 255, 80, 80, alpha: alpha);
        }
    }

    /// <summary>Render off-screen indicators for planets and stations in the solar system, fading by distance.</summary>
    public static void RenderSolarSystemObjectOffscreenIndicators(SpriteRenderer renderer, Camera camera,
        Entity playerShip, World ecsWorld,
        List<Entity> planetEntities, List<PlanetData> planets,
        List<Entity> stationEntities, List<SpaceStationData> stations,
        float maxDistance = 5000f)
    {
        Vector2 playerPos = ecsWorld.IsAlive(playerShip)
            ? ecsWorld.Get<Transform>(playerShip).Position
            : camera.Position;

        // Planets
        for (int i = 0; i < planetEntities.Count; i++)
        {
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
            RenderOffscreenIndicator(renderer, camera, pos, pr, pg, pb,
                prefix: name + " ", dotRadius: 4f, arrowSize: 9f, alpha: alpha);
        }

        // Stations
        for (int i = 0; i < stationEntities.Count; i++)
        {
            if (!ecsWorld.IsAlive(stationEntities[i])) continue;
            var pos = ecsWorld.Get<Transform>(stationEntities[i]).Position;
            float dist = Vector2.Distance(playerPos, pos);
            if (dist > maxDistance) continue;
            float distFraction = dist / maxDistance;
            byte alpha = (byte)(255 * (1f - distFraction * distFraction));
            string name = i < stations.Count ? stations[i].Name.ToUpper() : "STATION";
            RenderOffscreenIndicator(renderer, camera, pos, 100, 200, 255,
                prefix: name + " ", dotRadius: 4f, arrowSize: 9f, alpha: alpha);
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
        float dotRadius = 3f, float arrowSize = 8f, byte alpha = 255)
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

        byte a1 = alpha;
        byte a2 = (byte)Math.Min((int)alpha, 200);
        renderer.DrawLineScreen(tipX, tipY, baseX1, baseY1, new Color4(cr, cg, cb, a1));
        renderer.DrawLineScreen(tipX, tipY, baseX2, baseY2, new Color4(cr, cg, cb, a1));
        renderer.DrawLineScreen(baseX1, baseY1, baseX2, baseY2, new Color4(cr, cg, cb, a1));
        renderer.DrawLineScreen(ix, iy, tipX, tipY, new Color4(cr, cg, cb, a1));
        renderer.DrawLineScreen(ix, iy, baseX1, baseY1, new Color4(cr, cg, cb, a2));
        renderer.DrawLineScreen(ix, iy, baseX2, baseY2, new Color4(cr, cg, cb, a2));

        renderer.DrawFilledCircleScreen(ix, iy, dotRadius, new Color4(cr, cg, cb, (byte)Math.Min((int)alpha, 220)));

        // Distance label: world distance from screen edge to target
        float screenPixelDist = Vector2.Distance(screenPos, new Vector2(ix, iy));
        float worldDist = screenPixelDist / camera.Zoom;
        string distText = worldDist < 1000 ? $"{worldDist:F0}" : $"{worldDist / 1000f:F1}K";
        string label = prefix != null ? prefix + distText : distText;

        float labelW = renderer.MeasureText(label, 1f);
        float labelOffX = -MathF.Cos(angle) * 16f - labelW / 2f;
        float labelOffY = -MathF.Sin(angle) * 16f - 4f;
        renderer.DrawTextScreen(ix + labelOffX, iy + labelOffY, label, new Color4(cr, cg, cb, alpha), 1f);
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
        OverlayBase.DrawFrame(renderer, timerX - 10, timerY - 6, timerW + 20, 34);
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

    // ─────────────────────────────────────────────────────────────
    //  MINING / DEATH / CENTERED MESSAGE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Renders the mining target info panel for an asteroid entity.</summary>
    public static void RenderMiningPanel(SpriteRenderer renderer, ResourceType resource,
        float hp, float maxHp, int resourceAmount)
    {
        var resInfo = ResourceCatalog.Get(resource);
        float panelW = 280;
        float panelH = 72;
        float px = GameConfig.WindowWidth / 2f - panelW / 2f;
        float py = GameConfig.WindowHeight - panelH - HudMargin;

        OverlayBase.DrawFrame(renderer, px, py, panelW, panelH);
        renderer.DrawTextScreen(px + 10, py + 6, $"ASTEROID - {resInfo.Name.ToUpper()}", resInfo.Color, 2f);

        // HP bar
        float barX = px + 10;
        float barY = py + 30;
        float barW = panelW - 20;
        float hpRatio = maxHp > 0 ? hp / maxHp : 0;
        renderer.DrawRectScreen(barX, barY, barW, 12, new Color3(40, 40, 40));
        renderer.DrawRectScreen(barX, barY, barW * hpRatio, 12, resInfo.Color);

        renderer.DrawTextScreen(px + 10, py + 48, $"HP: {hp:F0}/{maxHp:F0}  QTY: {resourceAmount}", new Color3(180, 180, 180), 1.5f);
    }

    /// <summary>Render the death overlay with respawn countdown.</summary>
    public static void RenderDeathScreen(SpriteRenderer renderer, float respawnTimer)
    {
        string deathText = $"SHIP DESTROYED - RESPAWNING IN {respawnTimer:F1}s";
        float textW = renderer.MeasureText(deathText, 3f);
        float panelW = textW + 40;
        float panelH = 50;
        float px = GameConfig.WindowWidth / 2f - panelW / 2f;
        float py = GameConfig.WindowHeight / 2f - panelH / 2f;
        OverlayBase.DrawFrame(renderer, px, py, panelW, panelH);
        renderer.DrawTextScreen(px + 20, py + 12, deathText, new Color3(255, 80, 80), 3f);
    }

    /// <summary>Render a centered feedback message at the given vertical offset.</summary>
    public static void RenderCenteredMessage(SpriteRenderer renderer, string message,
        float yOffset, Color4 color, float scale)
    {
        float msgW = renderer.MeasureText(message, scale);
        float msgX = GameConfig.WindowWidth / 2f - msgW / 2f;
        renderer.DrawTextScreen(msgX, GameConfig.WindowHeight / 2f + yOffset, message, color, scale);
    }

    /// <summary>
    /// Render a prominent offscreen indicator for the player's current navigation target.
    /// </summary>
    public static void RenderNavTargetOffscreenIndicator(SpriteRenderer renderer, Camera camera,
        Vector2 targetWorldPos, string targetName, Color3 targetColor)
    {
        RenderOffscreenIndicator(renderer, camera, targetWorldPos,
            targetColor.R, targetColor.G, targetColor.B,
            prefix: $"TARGET: {targetName}", dotRadius: 5f, arrowSize: 10f);
    }

    /// <summary>
    /// Render a pulsing world-space marker at the navigation target position on the planet surface.
    /// Shows concentric rings, a crosshair, and a label.
    /// </summary>
    public static void RenderSurfaceNavTargetMarker(SpriteRenderer renderer, Camera camera,
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
