using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Unified HUD renderer shared across all game states.
/// TOP-LEFT: location info, player stats (credits/cargo), health/shields.
/// TOP-RIGHT: minimap.
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

    // Minimap constants (matching InteriorRenderer style)
    private const float MinimapSize = 150f;
    private const float MinimapMargin = 10f;

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
        }
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
    }

    // ─────────────────────────────────────────────────────────────
    //  MINIMAPS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render the solar system minimap (top-right).</summary>
    public static void RenderSolarSystemMinimap(SpriteRenderer renderer,
        List<PlanetData> planets, List<Entity> planetEntities,
        List<List<Entity>> moonEntities, List<Entity> stationEntities,
        List<Entity> asteroidEntities, List<Entity> enemyEntities,
        Entity playerShip, Entity starEntity, World ecsWorld,
        float starRadius)
    {
        float mmX = GameConfig.WindowWidth - MinimapSize - MinimapMargin;
        float mmY = MinimapMargin;

        // Border (interior-minimap style)
        renderer.DrawRectScreen(mmX - 1, mmY - 1, MinimapSize + 2, MinimapSize + 2, 60, 60, 100);
        renderer.DrawRectScreen(mmX, mmY, MinimapSize, MinimapSize, 10, 10, 15, 220);

        // Map bounds in world space
        float mapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float mapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;
        float scaleX = MinimapSize / mapW;
        float scaleY = MinimapSize / mapH;

        // Star dot (yellow)
        if (ecsWorld.IsAlive(starEntity))
        {
            var starPos = ecsWorld.Get<Transform>(starEntity).Position;
            float sx = mmX + starPos.X * scaleX;
            float sy = mmY + starPos.Y * scaleY;
            renderer.DrawFilledCircleScreen(sx, sy, 3f, 255, 220, 80, 255);
        }

        // Asteroid dots (dim grey)
        foreach (var entity in asteroidEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (ecsWorld.Has<Health>(entity) && ecsWorld.Get<Health>(entity).IsDead) continue;
            var pos = ecsWorld.Get<Transform>(entity).Position;
            float ax = mmX + pos.X * scaleX;
            float ay = mmY + pos.Y * scaleY;
            if (ax < mmX || ax > mmX + MinimapSize || ay < mmY || ay > mmY + MinimapSize) continue;
            renderer.DrawRectScreen(ax, ay, 1, 1, 80, 80, 80);
        }

        // Planet dots
        for (int i = 0; i < planetEntities.Count; i++)
        {
            if (!ecsWorld.IsAlive(planetEntities[i])) continue;
            var pos = ecsWorld.Get<Transform>(planetEntities[i]).Position;
            float px = mmX + pos.X * scaleX;
            float py = mmY + pos.Y * scaleY;
            byte pr = i < planets.Count ? planets[i].R : (byte)180;
            byte pg = i < planets.Count ? planets[i].G : (byte)180;
            byte pb = i < planets.Count ? planets[i].B : (byte)180;
            renderer.DrawFilledCircleScreen(px, py, 2f, pr, pg, pb, 220);

            // Moon dots
            if (i < moonEntities.Count)
            {
                foreach (var moonEntity in moonEntities[i])
                {
                    if (!ecsWorld.IsAlive(moonEntity)) continue;
                    var moonPos = ecsWorld.Get<Transform>(moonEntity).Position;
                    float mx = mmX + moonPos.X * scaleX;
                    float my = mmY + moonPos.Y * scaleY;
                    renderer.DrawRectScreen(mx, my, 2, 2, 160, 160, 180);
                }
            }
        }

        // Station dots (cyan)
        foreach (var entity in stationEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            var pos = ecsWorld.Get<Transform>(entity).Position;
            float stx = mmX + pos.X * scaleX;
            float sty = mmY + pos.Y * scaleY;
            renderer.DrawRectScreen(stx - 1, sty - 1, 3, 3, 100, 200, 255);
        }

        // Enemy dots (red for pirates, yellow for traders, blue for patrols)
        foreach (var entity in enemyEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (!ecsWorld.Has<Health>(entity) || ecsWorld.Get<Health>(entity).IsDead) continue;
            var pos = ecsWorld.Get<Transform>(entity).Position;
            var ai = ecsWorld.Get<EnemyAI>(entity);
            float ex = mmX + pos.X * scaleX;
            float ey = mmY + pos.Y * scaleY;
            if (ex < mmX || ex > mmX + MinimapSize || ey < mmY || ey > mmY + MinimapSize) continue;
            var (er, eg, eb) = ai.Config.Faction switch
            {
                Faction.Pirate => ((byte)255, (byte)80, (byte)80),
                Faction.Trader => ((byte)200, (byte)180, (byte)80),
                Faction.Patrol => ((byte)80, (byte)160, (byte)255),
                _ => ((byte)200, (byte)200, (byte)200)
            };
            renderer.DrawRectScreen(ex - 1, ey - 1, 3, 3, er, eg, eb);
        }

        // Player ship dot (green, on top)
        if (ecsWorld.IsAlive(playerShip))
        {
            var shipPos = ecsWorld.Get<Transform>(playerShip).Position;
            float playerX = mmX + shipPos.X * scaleX;
            float playerY = mmY + shipPos.Y * scaleY;
            renderer.DrawRectScreen(playerX - 2, playerY - 2, 4, 4, 100, 255, 100);
        }
    }

    /// <summary>Render the planet surface minimap (top-right, styled like interior minimap).</summary>
    public static void RenderPlanetSurfaceMinimap(SpriteRenderer renderer,
        PlanetSurfaceData surfaceData, Vector2 playerPos, Vector2 shipPos,
        Vector2? vehiclePos, World ecsWorld)
    {
        float mmX = GameConfig.WindowWidth - MinimapSize - MinimapMargin;
        float mmY = MinimapMargin;

        // Border (interior-minimap style)
        renderer.DrawRectScreen(mmX - 1, mmY - 1, MinimapSize + 2, MinimapSize + 2, 60, 60, 100);
        renderer.DrawRectScreen(mmX, mmY, MinimapSize, MinimapSize, 10, 10, 15, 220);

        float mapW = surfaceData.Width * GameConfig.TileSize;
        float mapH = surfaceData.Height * GameConfig.TileSize;
        float scaleX = MinimapSize / mapW;
        float scaleY = MinimapSize / mapH;

        // Settlement dots (yellow)
        foreach (var settlement in surfaceData.Settlements)
        {
            float sx = mmX + settlement.TileX * GameConfig.TileSize * scaleX;
            float sy = mmY + settlement.TileY * GameConfig.TileSize * scaleY;
            float sw = settlement.Width * GameConfig.TileSize * scaleX;
            float sh = settlement.Height * GameConfig.TileSize * scaleY;
            renderer.DrawRectScreen(sx, sy, Math.Max(sw, 3), Math.Max(sh, 3), 200, 180, 80);
        }

        // Ship dot (blue-ish)
        float smx = mmX + shipPos.X * scaleX;
        float smy = mmY + shipPos.Y * scaleY;
        renderer.DrawRectScreen(smx - 2, smy - 2, 4, 4, 150, 150, 200);

        // Vehicle dot (orange)
        if (vehiclePos.HasValue)
        {
            float vmx = mmX + vehiclePos.Value.X * scaleX;
            float vmy = mmY + vehiclePos.Value.Y * scaleY;
            renderer.DrawRectScreen(vmx - 2, vmy - 2, 4, 4, 180, 140, 80);
        }

        // Enemy dots on minimap
        SurfaceEnemyRenderer.RenderMinimapDots(renderer, ecsWorld, mmX, mmY, MinimapSize, mapW, mapH);

        // Player dot (green, on top)
        float pmx = mmX + playerPos.X * scaleX;
        float pmy = mmY + playerPos.Y * scaleY;
        renderer.DrawRectScreen(pmx - 2, pmy - 2, 4, 4, 100, 255, 100);
    }

    /// <summary>Render the interior minimap (top-right, matching existing style).</summary>
    public static void RenderInteriorMinimap(SpriteRenderer renderer, InteriorData interior,
        Vector2 playerPos)
    {
        float mmX = GameConfig.WindowWidth - MinimapSize - MinimapMargin;
        float mmY = MinimapMargin;

        // Border
        renderer.DrawRectScreen(mmX - 1, mmY - 1, MinimapSize + 2, MinimapSize + 2, 60, 60, 100);
        renderer.DrawRectScreen(mmX, mmY, MinimapSize, MinimapSize, 10, 10, 15, 220);

        float scaleX = MinimapSize / (interior.Width * GameConfig.TileSize);
        float scaleY = MinimapSize / (interior.Height * GameConfig.TileSize);

        // Draw rooms
        foreach (var room in interior.Rooms)
        {
            float rx = mmX + room.X * GameConfig.TileSize * scaleX;
            float ry = mmY + room.Y * GameConfig.TileSize * scaleY;
            float rw = room.Width * GameConfig.TileSize * scaleX;
            float rh = room.Height * GameConfig.TileSize * scaleY;
            renderer.DrawRectScreen(rx, ry, rw, rh, 50, 50, 60);
        }

        // NPC dots
        foreach (var npc in interior.Npcs)
        {
            float nx = mmX + npc.TileX * GameConfig.TileSize * scaleX;
            float ny = mmY + npc.TileY * GameConfig.TileSize * scaleY;
            renderer.DrawRectScreen(nx - 1, ny - 1, 3, 3, npc.R, npc.G, npc.B);
        }

        // Interactable dots
        foreach (var interactable in interior.Interactables)
        {
            float ix = mmX + interactable.TileX * GameConfig.TileSize * scaleX;
            float iy = mmY + interactable.TileY * GameConfig.TileSize * scaleY;
            var (ir, ig, ib) = InteriorRenderer.GetInteractableColor(interactable.Type);
            renderer.DrawRectScreen(ix - 1, iy - 1, 3, 3, ir, ig, ib);
        }

        // Player dot (green, on top)
        float px = mmX + playerPos.X * scaleX;
        float py = mmY + playerPos.Y * scaleY;
        renderer.DrawRectScreen(px - 2, py - 2, 4, 4, 100, 255, 100);
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Render the location info line. Returns the Y position for the next line.</summary>
    private static float RenderLocationLine(SpriteRenderer renderer, float y, string text)
    {
        float textW = renderer.MeasureText(text, TextScale);
        float bgW = Math.Max(textW + Padding * 2, 300f);
        renderer.DrawRectScreen(0, y, bgW, LineHeight + 4, 0, 0, 0, BgAlpha);
        renderer.DrawTextScreen(Padding, y + 2, text, 200, 200, 255, TextScale);
        return y + LineHeight + 6;
    }

    /// <summary>Render credits and cargo line. Returns the Y position for the next line.</summary>
    private static float RenderPlayerInfoLine(SpriteRenderer renderer, float y, PlayerData player)
    {
        string info = $"CREDITS: {player.Credits}  |  CARGO: {player.CargoUsed}/{player.MaxCargo}  |  FUEL: {player.ShipFuel:F0}/{player.ShipMaxFuel:F0}";
        float textW = renderer.MeasureText(info, TextScale);
        float bgW = Math.Max(textW + Padding * 2, 300f);
        renderer.DrawRectScreen(0, y, bgW, LineHeight + 4, 0, 0, 0, BgAlpha);
        renderer.DrawTextScreen(Padding, y + 2, info, 255, 220, 80, TextScale);
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
                (80, 160, 255), null, null);
        }
    }

    /// <summary>Render a single health/shield bar with label and numeric text.</summary>
    private static void RenderHealthBar(SpriteRenderer renderer, float y,
        string label, float current, float max,
        (byte R, byte G, byte B) fillColor,
        (byte R, byte G, byte B)? labelColor,
        (byte R, byte G, byte B)? textColor)
    {
        var lc = labelColor ?? (200, 200, 200);
        var tc = textColor ?? (200, 200, 200);

        float labelW = renderer.MeasureText(label, TextScale) + 8;
        float totalW = labelW + BarWidth + 80;
        renderer.DrawRectScreen(0, y, totalW, BarHeight + 8, 0, 0, 0, BgAlpha);

        // Label
        renderer.DrawTextScreen(Padding, y + 2, label, lc.R, lc.G, lc.B, TextScale);

        // Bar background
        float barX = Padding + labelW;
        renderer.DrawRectScreen(barX, y + 4, BarWidth, BarHeight, 40, 40, 40);

        // Bar fill
        float pct = max > 0 ? current / max : 0;
        renderer.DrawRectScreen(barX, y + 4, BarWidth * pct, BarHeight, fillColor.R, fillColor.G, fillColor.B);

        // Numeric
        renderer.DrawTextScreen(barX + BarWidth + 5, y + 2,
            $"{(int)current}/{(int)max}", tc.R, tc.G, tc.B, TextScale);
    }

    /// <summary>Calculate the hull bar color based on current percentage.</summary>
    private static (byte R, byte G, byte B) HPBarColor(float pct)
    {
        byte r = pct > 0.5f ? (byte)(255 * (1 - pct) * 2) : (byte)255;
        byte g = pct > 0.5f ? (byte)255 : (byte)(255 * pct * 2);
        return (r, g, 0);
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
        renderer.DrawRectScreen(w / 2f - tw / 2f - 10, h - 60, tw + 20, 35, 0, 0, 0, 180);
        renderer.DrawTextScreen(w / 2f - tw / 2f, h - 55, text, r, g, b, TitleScale);
    }

    /// <summary>Render a multi-line interaction panel centered at the bottom of the screen.</summary>
    public static void RenderPromptPanel(SpriteRenderer renderer, string[] lines,
        (byte R, byte G, byte B)[] colors)
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
        renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);

        // First line (action) at title scale
        var c0 = colors[0];
        renderer.DrawTextScreen(px + 10, py + 6, lines[0], c0.R, c0.G, c0.B, TitleScale);

        // Remaining lines at text scale
        for (int i = 1; i < lines.Length; i++)
        {
            var c = i < colors.Length ? colors[i] : (R: (byte)150, G: (byte)150, B: (byte)150);
            renderer.DrawTextScreen(px + 10, py + 6 + 24 + (i - 1) * 18, lines[i], c.R, c.G, c.B, TextScale);
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
                [(100, 255, 100),
                 (180, 180, 180),
                 (150, 150, 150),
                 planet.HasSettlement ? ((byte)255, (byte)220, (byte)100) : ((byte)120, (byte)120, (byte)120)]);
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
                [(180, 255, 180),
                 (180, 180, 180),
                 (150, 150, 150)]);
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

    /// <summary>Render arrow indicators at screen edges for off-screen NPC ships.</summary>
    public static void RenderOffscreenIndicators(SpriteRenderer renderer, Camera camera, World ecsWorld,
        List<Entity> enemyEntities)
    {
        foreach (var entity in enemyEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (!ecsWorld.Has<Health>(entity)) continue;
            ref var health = ref ecsWorld.Get<Health>(entity);
            if (health.IsDead) continue;

            ref var transform = ref ecsWorld.Get<Transform>(entity);
            var ai = ecsWorld.Get<EnemyAI>(entity);

            var (cr, cg, cb) = ai.Config.Faction switch
            {
                Faction.Pirate => ((byte)255, (byte)80, (byte)80),
                Faction.Trader => ((byte)200, (byte)180, (byte)80),
                Faction.Patrol => ((byte)80, (byte)160, (byte)255),
                _ => ((byte)200, (byte)200, (byte)200)
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

        renderer.DrawLineScreen(tipX, tipY, baseX1, baseY1, cr, cg, cb, 255);
        renderer.DrawLineScreen(tipX, tipY, baseX2, baseY2, cr, cg, cb, 255);
        renderer.DrawLineScreen(baseX1, baseY1, baseX2, baseY2, cr, cg, cb, 255);
        renderer.DrawLineScreen(ix, iy, tipX, tipY, cr, cg, cb, 255);
        renderer.DrawLineScreen(ix, iy, baseX1, baseY1, cr, cg, cb, 200);
        renderer.DrawLineScreen(ix, iy, baseX2, baseY2, cr, cg, cb, 200);

        renderer.DrawFilledCircleScreen(ix, iy, dotRadius, cr, cg, cb, 220);

        // Distance label: world distance from screen edge to target
        float screenPixelDist = Vector2.Distance(screenPos, new Vector2(ix, iy));
        float worldDist = screenPixelDist / camera.Zoom;
        string distText = worldDist < 1000 ? $"{worldDist:F0}" : $"{worldDist / 1000f:F1}K";
        string label = prefix != null ? prefix + distText : distText;

        float labelW = renderer.MeasureText(label, 1f);
        float labelOffX = -MathF.Cos(angle) * 16f - labelW / 2f;
        float labelOffY = -MathF.Sin(angle) * 16f - 4f;
        renderer.DrawTextScreen(ix + labelOffX, iy + labelOffY, label, cr, cg, cb, 1f);
    }
}
