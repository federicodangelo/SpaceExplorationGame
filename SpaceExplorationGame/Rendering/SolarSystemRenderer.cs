using System.Numerics;
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
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 130, "SPACE: MINE", 180, 180, 180, 1.5f);
    }

    /// <summary>Renders cargo info below the system HUD.</summary>
    public static void RenderCargoHud(SpriteRenderer renderer, PlayerData player)
    {
        float hudY = 80;
        renderer.DrawRectScreen(0, hudY, 220, 22, 0, 0, 0, 160);
        renderer.DrawTextScreen(10, hudY + 4, $"CARGO: {player.CargoUsed}/{player.MaxCargo}", 200, 180, 100, 1.5f);
    }

    /// <summary>Renders the mining target info panel when aiming at an asteroid.</summary>
    public static void RenderMiningPanel(SpriteRenderer renderer, MineableAsteroid asteroid)
    {
        var resInfo = ResourceCatalog.Get(asteroid.Resource);
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
        float hpRatio = asteroid.Hp / asteroid.MaxHp;
        renderer.DrawRectScreen(barX, barY, barW, 12, 40, 40, 40);
        renderer.DrawRectScreen(barX, barY, barW * hpRatio, 12, resInfo.R, resInfo.G, resInfo.B);

        renderer.DrawTextScreen(px + 10, py + 48, $"HP: {asteroid.Hp:F0}/{asteroid.MaxHp:F0}  QTY: {asteroid.ResourceAmount}", 180, 180, 180, 1.5f);
    }
}
