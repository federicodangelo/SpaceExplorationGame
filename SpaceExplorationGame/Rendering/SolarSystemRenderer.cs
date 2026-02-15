using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
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

    /// <summary>Renders asteroids computed from global time.</summary>
    public static void RenderAsteroids(SpriteRenderer renderer, Camera camera,
        List<(float BaseAngle, float Radius, float Speed, float Size)> asteroids,
        Vector2 starCenter, double globalTime, nint asteroidTexture)
    {
        float asteroidTime = (float)globalTime;
        foreach (var (baseAngle, radius, speed, size) in asteroids)
        {
            float angle = baseAngle + speed * asteroidTime;
            var pos = starCenter + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            float rot = angle * 180f / MathF.PI * 2f;
            renderer.DrawTexture(camera, asteroidTexture, pos, (int)size + 4, (int)size + 4, rot);
        }
    }

    /// <summary>Renders the central star with texture.</summary>
    public static void RenderStar(SpriteRenderer renderer, Camera camera,
        nint starTexture, Vector2 starCenter, float starDisplayRadius)
    {
        renderer.DrawTexture(camera, starTexture, starCenter,
            (int)(starDisplayRadius * 3), (int)(starDisplayRadius * 3));
    }

    /// <summary>Renders planets with textures, settlement indicators, rings, moon orbits, and moons.</summary>
    public static void RenderPlanetsAndMoons(SpriteRenderer renderer, Camera camera,
        World ecsWorld, List<PlanetData> planets,
        List<Entity> planetEntities, List<List<Entity>> moonEntities,
        List<nint> planetTextures, List<List<nint>> moonTextures)
    {
        for (int i = 0; i < planets.Count; i++)
        {
            if (i >= planetEntities.Count) break;
            var pTransform = ecsWorld.Get<Transform>(planetEntities[i]);
            var p = planets[i];
            int texRenderSize = (int)(p.Radius * 2) + 4;

            // Planet texture
            if (i < planetTextures.Count)
            {
                renderer.DrawTexture(camera, planetTextures[i], pTransform.Position,
                    texRenderSize, texRenderSize);
            }

            // Settlement indicator (small diamond below planet)
            if (p.HasSettlement)
            {
                var indicatorPos = pTransform.Position + new Vector2(0, p.Radius + 6);
                renderer.DrawFilledCircle(camera, indicatorPos, 3f, 255, 210, 200, 220);
            }

            // Rings
            if (p.HasRings)
            {
                renderer.DrawCircle(camera, pTransform.Position, p.Radius * 1.5f,
                    p.R, p.G, p.B, 120, 48);
                renderer.DrawCircle(camera, pTransform.Position, p.Radius * 1.8f,
                    p.R, p.G, p.B, 80, 48);
            }

            // Moon orbit lines
            foreach (var moon in p.Moons)
            {
                renderer.DrawCircle(camera, pTransform.Position, moon.OrbitRadius, 20, 20, 40, 255, 24);
            }

            // Moon textures
            if (i < moonEntities.Count)
            {
                for (int m = 0; m < moonEntities[i].Count; m++)
                {
                    var moonTransform = ecsWorld.Get<Transform>(moonEntities[i][m]);
                    var moon = p.Moons[m];
                    int moonTexSize = (int)(moon.Radius * 2) + 2;
                    if (i < moonTextures.Count && m < moonTextures[i].Count)
                    {
                        renderer.DrawTexture(camera, moonTextures[i][m], moonTransform.Position,
                            moonTexSize, moonTexSize);
                    }
                }
            }
        }
    }

    /// <summary>Renders space stations with rotating texture.</summary>
    public static void RenderStations(SpriteRenderer renderer, Camera camera,
        World ecsWorld, List<Entity> stationEntities, nint stationTexture, double globalTime)
    {
        for (int i = 0; i < stationEntities.Count; i++)
        {
            var stTransform = ecsWorld.Get<Transform>(stationEntities[i]);
            float stRotation = (float)(globalTime * 10) % 360f;
            renderer.DrawTexture(camera, stationTexture, stTransform.Position, 28, 28, stRotation);
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
        renderer.DrawRectScreen(GameConfig.WindowWidth - 290, 5, 290, 130, 0, 0, 0, 160);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 10, "W/UP: THRUST", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 30, "A/D: ROTATE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 50, "S/DOWN: BRAKE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 70, "SCROLL: ZOOM", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 90, "M: GALAXY MAP", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 110, "E: INTERACT", 180, 180, 180, 1.5f);
    }
}
