using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Hud;

/// <summary>
/// Unified HUD renderer shared across all game states.
/// TOP-LEFT: location info, player stats (credits/cargo), health/shields.
/// TOP-RIGHT: minimap (delegated to HudMinimapRenderer).
/// Off-screen indicators and mission markers in HudIndicatorsRenderer.
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
        var tracked = player.Missions.GetTracked();
        var stats = player.GetCombinedStats();
        bool hasShield = stats.ShieldStrength > 0 && ecsWorld.IsAlive(playerShip) && ecsWorld.Has<Health>(playerShip);

        // Measure panel width
        float panelW = MeasureHudPanelWidth(renderer, locationLine, infoLine);

        // Calculate panel height
        float panelH = Padding
            + LineHeight + SepGap   // location + sep
            + LineHeight + SepGap   // info + sep
            + BarHeight + 8;        // hull bar
        if (hasShield) panelH += BarHeight + 4;
        if (tracked != null)
        {
            panelH += SepGap + LineHeight * 2 + 4;
            if (player.Missions.Active.Count > 1) panelH += LineHeight;
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
        var tracked = player.Missions.GetTracked();
        bool hasHealth = ecsWorld.IsAlive(playerAvatar) && ecsWorld.Has<Health>(playerAvatar);
        Health avatarHealth = default;
        if (hasHealth) avatarHealth = ecsWorld.Get<Health>(playerAvatar);

        // Measure panel width
        float panelW = MeasureHudPanelWidth(renderer, locationLine, infoLine);

        // Calculate panel height
        float panelH = Padding
            + LineHeight + SepGap   // location + sep
            + LineHeight + SepGap;  // info + sep
        if (hasHealth) panelH += BarHeight + 8;
        if (tracked != null)
        {
            panelH += SepGap + LineHeight * 2 + 4;
            if (player.Missions.Active.Count > 1) panelH += LineHeight;
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
        string typeLabel = interior.Type == InteriorType.SpaceStation ? "SPACE STATION" : "SETTLEMENT";
        string dangerStr = FormatDanger(starSystem.DangerLevel);
        string locationLine = $"{interior.Name.ToUpper()}  |  {typeLabel}  |  {starSystem.Name}  |  {dangerStr}";
        string infoLine = FormatPlayerInfo(player);
        var tracked = player.Missions.GetTracked();

        // Measure panel width
        float panelW = MeasureHudPanelWidth(renderer, locationLine, infoLine);

        // Calculate panel height
        float panelH = Padding
            + LineHeight + SepGap   // location + sep
            + LineHeight + SepGap   // info + sep
            + BarHeight + 8;        // HP bar
        if (tracked != null)
        {
            panelH += SepGap + LineHeight * 2 + 4;
            if (player.Missions.Active.Count > 1) panelH += LineHeight;
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
    private static float MeasureHudPanelWidth(SpriteRenderer renderer, string locationLine, string infoLine)
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
        renderer.DrawRectScreen(barX, y + 1, BarWidth, BarHeight, new Color3(40, 40, 40));
        float pct = max > 0 ? current / max : 0;
        renderer.DrawRectScreen(barX, y + 1, BarWidth * pct, BarHeight, fillColor);
        renderer.DrawTextScreen(barX + BarWidth + 5, y + 2,
            $"{(int)current}/{(int)max}", lc, TextScale);
    }

    /// <summary>Render mission tracker content (no background).</summary>
    private static void RenderMissionContent(SpriteRenderer renderer, float x, float y, PlayerData player)
    {
        var tracked = player.Missions.GetTracked();
        if (tracked == null) return;

        bool completed = tracked.Status == MissionStatus.Completed;
        string statusIcon = completed ? ">> " : "* ";
        string missionText = $"{statusIcon}[{tracked.TypeLabel}] {tracked.Title}";

        renderer.DrawTextScreen(x, y + 2, missionText,
            completed ? new Color3(100, 255, 100) : tracked.TypeColor, TextScale);
        renderer.DrawTextScreen(x + 10, y + LineHeight + 2, tracked.ProgressText,
            completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), TextScale);

        int totalActive = player.Missions.Active.Count;
        if (totalActive > 1)
        {
            int completedCount = player.Missions.Active.Count(m => m.Status == MissionStatus.Completed);
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
        int nearbyPlanetIndex, int nearbyMoonIndex, int nearbyMoonPlanetIndex, int nearbySpaceStationIndex,
        List<PlanetData> planets, List<SpaceStationData> stations,
        string interactHelpText)
    {
        if (nearbyPlanetIndex >= 0)
        {
            var planet = planets[nearbyPlanetIndex];
            string details = $"MOONS: {planet.MoonCount}";
            if (planet.HasRings) details += "  RINGS: YES";
            string settText = planet.HasSettlement ? "SETTLEMENTS: YES" : "NO SETTLEMENTS";

            RenderPromptPanel(renderer,
                [$"[{interactHelpText}] LAND ON {planet.Name.ToUpper()}",
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
                [$"[{interactHelpText}] LAND ON {moon.Name.ToUpper()}",
                 $"TYPE: {moon.Type.ToString().ToUpper()}",
                 $"ORBITS: {parent.Name.ToUpper()}"],
                [new Color3(180, 255, 180),
                 new Color3(180, 180, 180),
                 new Color3(150, 150, 150)]);
        }
        else if (nearbySpaceStationIndex >= 0)
        {
            RenderPrompt(renderer, $"[{interactHelpText}] DOCK AT {stations[nearbySpaceStationIndex].Name.ToUpper()}",
                100, 200, 255);
        }
    }

    /// <summary>Render planet surface interaction prompts (board ship, mount vehicle, enter settlement).</summary>
    public static void RenderPlanetSurfacePrompt(SpriteRenderer renderer,
        bool inVehicle, bool nearShip, bool nearVehicle, bool vehicleDeployed,
        SettlementData? nearSettlement, string interactHelpText)
    {
        if (inVehicle && nearShip)
            RenderPrompt(renderer, $"[{interactHelpText}] BOARD STARSHIP", 100, 255, 100);
        else if (inVehicle)
            RenderPrompt(renderer, $"[{interactHelpText}] DISMOUNT", 255, 200, 100);
        else if (nearShip)
            RenderPrompt(renderer, $"[{interactHelpText}] BOARD STARSHIP", 100, 255, 100);
        else if (nearVehicle && vehicleDeployed)
            RenderPrompt(renderer, $"[{interactHelpText}] MOUNT VEHICLE", 255, 200, 100);
        else if (nearSettlement != null)
            RenderPrompt(renderer, $"[{interactHelpText}] ENTER {nearSettlement.Name.ToUpper()}", 255, 255, 100);
    }

    /// <summary>Render interior interaction prompts (interactables and NPCs).</summary>
    public static void RenderInteriorPrompt(SpriteRenderer renderer,
        InteriorInteractable? nearestInteractable, InteriorNpc? nearestNpc,
        string interactHelpText)
    {
        if (nearestInteractable != null)
        {
            string prompt = nearestInteractable.Type switch
            {
                InteractableType.ExitDoor => $"[{interactHelpText}] EXIT",
                InteractableType.RepairStation => $"[{interactHelpText}] REPAIR",
                InteractableType.HealthStation => $"[{interactHelpText}] HEALTH STATION",
                InteractableType.MissionBoard => $"[{interactHelpText}] MISSIONS",
                InteractableType.ShipCustomization => $"[{interactHelpText}] SHIP CUSTOMIZATION",
                InteractableType.AvatarCustomization => $"[{interactHelpText}] AVATAR CUSTOMIZATION",
                InteractableType.VehicleCustomization => $"[{interactHelpText}] VEHICLE CUSTOMIZATION",
                InteractableType.ShipDealer => $"[{interactHelpText}] SHIP DEALER",
                InteractableType.CargoTerminal => $"[{interactHelpText}] SELL CARGO",
                _ => $"[{interactHelpText}] INTERACT"
            };
            RenderPrompt(renderer, prompt, 100, 255, 200);
        }
        else if (nearestNpc != null)
        {
            RenderPrompt(renderer, $"[{interactHelpText}] TALK TO {nearestNpc.Name.ToUpper()}", 200, 200, 255);
        }
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
}
