using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders solar system visuals: background stars, celestial bodies, ship, HUD, and interaction panels.
/// </summary>
public static class SolarSystemRenderer
{
    /// <summary>Renders parallax background stars.</summary>
    public static void RenderBackgroundStars(SpriteRenderer renderer, Camera camera,
        List<(float X, float Y, byte Brightness)> bgStars, Vector2 starCenter)
    {
        foreach (var (x, y, brightness) in bgStars)
        {
            var parallaxPos = new Vector2(x, y);
            var screenPos = camera.WorldToScreen(parallaxPos);
            screenPos.X -= (camera.Position.X - starCenter.X) * 0.3f * camera.Zoom;
            screenPos.Y -= (camera.Position.Y - starCenter.Y) * 0.3f * camera.Zoom;

            if (screenPos.X >= 0 && screenPos.X < GameConfig.WindowWidth &&
                screenPos.Y >= 0 && screenPos.Y < GameConfig.WindowHeight)
            {
                renderer.DrawRectScreen(screenPos.X, screenPos.Y, 1, 1, brightness, brightness, brightness);
            }
        }
    }

    /// <summary>Renders orbit lines for all planets.</summary>
    public static void RenderOrbitLines(SpriteRenderer renderer, Camera camera,
        List<PlanetData> planets, Vector2 starCenter)
    {
        foreach (var planet in planets)
        {
            renderer.DrawCircle(camera, starCenter, planet.OrbitRadius, 30, 30, 50, 255, 64);
        }
    }

    /// <summary>Renders the solar system HUD: system info, speed display.</summary>
    public static void RenderHud(SpriteRenderer renderer, string systemName, ECS.Components.StarClass starClass, float speed)
    {
        renderer.DrawRectScreen(0, 0, 280, 75, 0, 0, 0, 160);
        renderer.DrawTextScreen(10, 10, $"SYSTEM: {systemName}", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"CLASS {starClass} STAR", 150, 150, 150, 1.5f);
        renderer.DrawTextScreen(10, 55, $"SPEED: {speed:F0}", 150, 150, 150, 1.5f);
    }

    /// <summary>Renders the planet interaction panel at the bottom of the screen.</summary>
    public static void RenderPlanetPanel(SpriteRenderer renderer, PlanetData planet)
    {
        string action = $"[E] LAND ON {planet.Name.ToUpper()}";
        float tw = renderer.MeasureText(action, 2f);
        float panelW = Math.Max(tw + 20, 320);
        float panelH = 90;
        float px = GameConfig.WindowWidth / 2f - panelW / 2f;
        float py = GameConfig.WindowHeight - panelH - 15;
        renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);

        renderer.DrawTextScreen(px + 10, py + 6, action, 100, 255, 100, 2f);
        renderer.DrawTextScreen(px + 10, py + 30, $"TYPE: {planet.Type.ToString().ToUpper()}", 180, 180, 180, 1.5f);
        string details = $"MOONS: {planet.MoonCount}";
        if (planet.HasRings) details += "  RINGS: YES";
        renderer.DrawTextScreen(px + 10, py + 48, details, 150, 150, 150, 1.5f);

        byte sr = planet.HasSettlement ? (byte)255 : (byte)120;
        byte sg = planet.HasSettlement ? (byte)220 : (byte)120;
        byte sb = planet.HasSettlement ? (byte)100 : (byte)120;
        string settText = planet.HasSettlement ? "SETTLEMENTS: YES" : "NO SETTLEMENTS";
        renderer.DrawTextScreen(px + 10, py + 66, settText, sr, sg, sb, 1.5f);
    }

    /// <summary>Renders the moon interaction panel at the bottom of the screen.</summary>
    public static void RenderMoonPanel(SpriteRenderer renderer, MoonData moon, PlanetData parentPlanet)
    {
        string action = $"[E] LAND ON {moon.Name.ToUpper()}";
        float tw = renderer.MeasureText(action, 2f);
        float panelW = Math.Max(tw + 20, 320);
        float panelH = 72;
        float px = GameConfig.WindowWidth / 2f - panelW / 2f;
        float py = GameConfig.WindowHeight - panelH - 15;
        renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);

        renderer.DrawTextScreen(px + 10, py + 6, action, 180, 255, 180, 2f);
        renderer.DrawTextScreen(px + 10, py + 30, $"TYPE: {moon.Type.ToString().ToUpper()}", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(px + 10, py + 48, $"ORBITS: {parentPlanet.Name.ToUpper()}", 150, 150, 150, 1.5f);
    }

    /// <summary>Renders the station docking prompt at the bottom of the screen.</summary>
    public static void RenderStationPanel(SpriteRenderer renderer, string stationName)
    {
        string text = $"[E] DOCK AT {stationName.ToUpper()}";
        float tw = renderer.MeasureText(text, 2f);
        float tx = GameConfig.WindowWidth / 2 - tw / 2;
        renderer.DrawRectScreen(tx - 10, GameConfig.WindowHeight - 70, tw + 20, 30, 0, 0, 0, 160);
        renderer.DrawTextScreen(tx, GameConfig.WindowHeight - 60, text, 100, 200, 255, 2f);
    }

    /// <summary>Renders the controls help box.</summary>
    public static void RenderControls(SpriteRenderer renderer)
    {
        renderer.DrawRectScreen(GameConfig.WindowWidth - 290, 5, 290, 150, 0, 0, 0, 160);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 10, "W/UP: THRUST", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 30, "A/D: ROTATE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 50, "S/DOWN: BRAKE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 70, "SCROLL: ZOOM", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 90, "M: GALAXY MAP", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 110, "E: INTERACT", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 130, "SPACE: SHOOT", 180, 180, 180, 1.5f);
    }

    /// <summary>Renders cargo info below the system HUD.</summary>
    public static void RenderCargoHud(SpriteRenderer renderer, PlayerData player)
    {
        float hudY = 80;
        renderer.DrawRectScreen(0, hudY, 220, 22, 0, 0, 0, 160);
        renderer.DrawTextScreen(10, hudY + 4, $"CARGO: {player.CargoUsed}/{player.MaxCargo}", 200, 180, 100, 1.5f);
    }

    /// <summary>Renders the mining target info panel for an asteroid entity.</summary>
    public static void RenderMiningPanel(SpriteRenderer renderer, ResourceType resource,
        float hp, float maxHp, int resourceAmount)
    {
        var resInfo = ResourceCatalog.Get(resource);
        float panelW = 280;
        float panelH = 72;
        float px = GameConfig.WindowWidth / 2f - panelW / 2f;
        float py = GameConfig.WindowHeight - panelH - 15;

        renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);
        renderer.DrawTextScreen(px + 10, py + 6, $"ASTEROID - {resInfo.Name.ToUpper()}", resInfo.R, resInfo.G, resInfo.B, 2f);

        // HP bar
        float barX = px + 10;
        float barY = py + 30;
        float barW = panelW - 20;
        float hpRatio = maxHp > 0 ? hp / maxHp : 0;
        renderer.DrawRectScreen(barX, barY, barW, 12, 40, 40, 40);
        renderer.DrawRectScreen(barX, barY, barW * hpRatio, 12, resInfo.R, resInfo.G, resInfo.B);

        renderer.DrawTextScreen(px + 10, py + 48, $"HP: {hp:F0}/{maxHp:F0}  QTY: {resourceAmount}", 180, 180, 180, 1.5f);
    }

    /// <summary>Render all NPC ships with their textures, health bars, and faction labels.</summary>
    public static void RenderNPCShips(SpriteRenderer renderer, Camera camera, World ecsWorld,
        List<Entity> enemyEntities, EnemyShipRenderer enemyShipRenderer)
    {
        foreach (var entity in enemyEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;
            if (!ecsWorld.Has<Health>(entity)) continue;

            ref var health = ref ecsWorld.Get<Health>(entity);
            if (health.IsDead) continue;

            ref var transform = ref ecsWorld.Get<Transform>(entity);
            var ai = ecsWorld.Get<EnemyAI>(entity);
            var velocity = ecsWorld.Get<Velocity>(entity);

            bool isMoving = velocity.Value.LengthSquared() > 50f * 50f;
            int shipSize = ai.Config.Faction switch
            {
                Faction.Pirate => 28,
                Faction.Trader => 32,
                Faction.Patrol => 30,
                _ => 28
            };

            enemyShipRenderer.Render(renderer, camera, transform.Position, transform.Rotation,
                ai.Config.Faction, shipSize, isMoving);

            // Health bar
            enemyShipRenderer.RenderHealthBar(renderer, camera, transform.Position,
                health.HullPercent, health.ShieldPercent, health.MaxShield, shipSize);

            // Faction indicator (small colored text above health bar)
            string factionLabel = ai.Config.Faction switch
            {
                Faction.Pirate => "PIRATE",
                Faction.Trader => "TRADER",
                Faction.Patrol => "PATROL",
                _ => ""
            };
            var (fr, fg, fb) = ai.Config.Faction switch
            {
                Faction.Pirate => ((byte)255, (byte)80, (byte)80),
                Faction.Trader => ((byte)200, (byte)180, (byte)80),
                Faction.Patrol => ((byte)80, (byte)160, (byte)255),
                _ => ((byte)200, (byte)200, (byte)200)
            };
            var labelPos = transform.Position - new Vector2(0, shipSize / 2f + 18f);
            renderer.DrawText(camera, labelPos, factionLabel, fr, fg, fb, 0.8f);
        }
    }

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

    /// <summary>Render the combat HUD: hull/shield bars and danger level.</summary>
    public static void RenderCombatHud(SpriteRenderer renderer, PlayerData player, World ecsWorld,
        Entity playerShip, int dangerLevel)
    {
        // Position below cargo HUD
        float hudX = 10;
        float hudY = 140;

        // Background
        renderer.DrawRectScreen(hudX - 5, hudY - 5, 230, 70, 0, 0, 0, 160);

        // Hull bar
        float hullPct = player.ShipMaxHealth > 0 ? player.ShipHealth / player.ShipMaxHealth : 0;
        renderer.DrawTextScreen(hudX, hudY, "HULL", 200, 200, 200, 1.5f);
        float barX = hudX + 60;
        float barW = 160;
        float barH = 12;
        renderer.DrawRectScreen(barX, hudY, barW, barH, 40, 40, 40);
        byte hullR = hullPct > 0.5f ? (byte)(255 * (1 - hullPct) * 2) : (byte)255;
        byte hullG = hullPct > 0.5f ? (byte)255 : (byte)(255 * hullPct * 2);
        renderer.DrawRectScreen(barX, hudY, barW * hullPct, barH, hullR, hullG, 0);
        renderer.DrawTextScreen(barX + barW + 5, hudY, $"{player.ShipHealth:F0}/{player.ShipMaxHealth:F0}", 200, 200, 200, 1f);

        // Shield bar (if player has shield)
        var stats = player.GetCombinedStats();
        if (stats.ShieldStrength > 0 && ecsWorld.IsAlive(playerShip) && ecsWorld.Has<Health>(playerShip))
        {
            ref var health = ref ecsWorld.Get<Health>(playerShip);
            float shieldPct = health.ShieldPercent;
            renderer.DrawTextScreen(hudX, hudY + 20, "SHLD", 100, 160, 255, 1.5f);
            renderer.DrawRectScreen(barX, hudY + 20, barW, barH, 40, 40, 60);
            renderer.DrawRectScreen(barX, hudY + 20, barW * shieldPct, barH, 80, 160, 255);
            renderer.DrawTextScreen(barX + barW + 5, hudY + 20, $"{health.Shield:F0}/{health.MaxShield:F0}", 100, 160, 255, 1f);
        }

        // Danger level
        string dangerText = $"DANGER LV.{dangerLevel}";
        byte dR = dangerLevel <= 2 ? (byte)100 : dangerLevel <= 3 ? (byte)255 : (byte)255;
        byte dG = dangerLevel <= 2 ? (byte)255 : dangerLevel <= 3 ? (byte)200 : (byte)80;
        byte dB = dangerLevel <= 2 ? (byte)100 : (byte)50;
        renderer.DrawTextScreen(hudX, hudY + 42, dangerText, dR, dG, dB, 1.5f);
    }

    /// <summary>Render the death overlay with respawn countdown.</summary>
    public static void RenderDeathScreen(SpriteRenderer renderer, float respawnTimer)
    {
        renderer.DrawRectScreen(0, GameConfig.WindowHeight / 2f - 40, GameConfig.WindowWidth, 80, 0, 0, 0, 180);
        string deathText = $"SHIP DESTROYED - RESPAWNING IN {respawnTimer:F1}s";
        float textW = renderer.MeasureText(deathText, 3f);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - textW / 2f,
            GameConfig.WindowHeight / 2f - 15, deathText, 255, 80, 80, 3f);
    }

    /// <summary>Render a centered feedback message at the given vertical offset.</summary>
    public static void RenderCenteredMessage(SpriteRenderer renderer, string message,
        float yOffset, byte r, byte g, byte b, float scale)
    {
        float msgW = renderer.MeasureText(message, scale);
        float msgX = GameConfig.WindowWidth / 2f - msgW / 2f;
        renderer.DrawTextScreen(msgX, GameConfig.WindowHeight / 2f + yOffset, message, r, g, b, scale);
    }

}
