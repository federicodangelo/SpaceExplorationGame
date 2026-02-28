using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders interior visuals: exterior background, tiles, NPCs, interactables, and dialogue.
/// </summary>
public static class InteriorRenderer
{
    /// <summary>
    /// Renders the full interior world (background, tiles, NPCs, interactables, player avatar, atmosphere).
    /// </summary>
    public static void RenderWorld(ISpriteRenderer renderer, Camera camera, InteriorData interior,
        Vector2 playerPos, AvatarRenderer avatarRenderer, double globalTime, PlanetData? planet)
    {
        RenderExteriorBackground(renderer, camera, interior, planet, globalTime);
        RenderTiles(renderer, camera, interior, globalTime);
        RenderRoomAmbientTint(renderer, camera, interior);
        RenderRoomLabels(renderer, camera, interior);
        RenderNpcs(renderer, camera, interior, globalTime);
        RenderInteractableMarkers(renderer, camera, interior, globalTime);
        RenderPlayerAvatar(renderer, camera, playerPos, avatarRenderer);
    }

    /// <summary>
    /// Renders the vignette and atmosphere overlays on top of the scene (called from state after world render).
    /// </summary>
    public static void RenderAtmosphere(ISpriteRenderer renderer, int screenW, int screenH)
    {
        // Vignette: darken edges of the screen
        int bandSize = 120;
        byte maxAlpha = 60;

        // Top band
        for (int i = 0; i < bandSize; i += 20)
        {
            byte alpha = (byte)(maxAlpha * (1f - (float)i / bandSize));
            renderer.DrawRectScreen(0, i, screenW, 20,
                new Color4(0, 0, 0, alpha));
        }
        // Bottom band
        for (int i = 0; i < bandSize; i += 20)
        {
            byte alpha = (byte)(maxAlpha * (1f - (float)i / bandSize));
            renderer.DrawRectScreen(0, screenH - i - 20, screenW, 20,
                new Color4(0, 0, 0, alpha));
        }
        // Left band
        for (int i = 0; i < bandSize; i += 20)
        {
            byte alpha = (byte)(maxAlpha * (1f - (float)i / bandSize));
            renderer.DrawRectScreen(i, 0, 20, screenH,
                new Color4(0, 0, 0, alpha));
        }
        // Right band
        for (int i = 0; i < bandSize; i += 20)
        {
            byte alpha = (byte)(maxAlpha * (1f - (float)i / bandSize));
            renderer.DrawRectScreen(screenW - i - 20, 0, 20, screenH,
                new Color4(0, 0, 0, alpha));
        }
    }

    /// <summary>Renders weather particle effects for settlements based on planet biome.</summary>
    public static void RenderWeatherEffects(ISpriteRenderer renderer,
        int screenW, int screenH, PlanetData? planet, double globalTime)
    {
        if (planet == null) return;
        var biome = planet.Type;

        // Weather particle count and style per biome
        int particleCount;
        switch (biome)
        {
            case PlanetType.Terrestrial:
            case PlanetType.Ocean:
                // Rain
                particleCount = 60;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 374761 + (int)(globalTime * 100) * 17;
                    float px = ((hash & 0xFFFF) % screenW);
                    float speed = 400f + (hash >> 16 & 0xFF);
                    float py = (float)((i * 73.7 + globalTime * speed) % (screenH + 20)) - 10;
                    byte alpha = (byte)(30 + (hash >> 8 & 0x1F));
                    renderer.DrawRectScreen((int)px, (int)py, 1, 6,
                        new Color4(140, 160, 200, alpha));
                }
                break;

            case PlanetType.Desert:
                // Dust / sand particles drifting horizontally
                particleCount = 35;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 668265 + (int)(globalTime * 60) * 23;
                    float speed = 120f + (hash >> 16 & 0x7F);
                    float px = (float)((i * 97.3 + globalTime * speed) % (screenW + 20)) - 10;
                    float py = ((hash & 0xFFFF) % screenH);
                    byte alpha = (byte)(20 + (hash >> 8 & 0x1F));
                    renderer.DrawRectScreen((int)px, (int)py, 3, 2,
                        new Color4(180, 160, 120, alpha));
                }
                break;

            case PlanetType.Frozen:
                // Snow
                particleCount = 50;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 472882 + (int)(globalTime * 40) * 13;
                    float drift = (float)Math.Sin(globalTime * 0.8 + i * 0.7) * 30f;
                    float px = ((hash & 0xFFFF) % screenW) + drift;
                    float speed = 60f + (hash >> 16 & 0x3F);
                    float py = (float)((i * 53.1 + globalTime * speed) % (screenH + 10)) - 5;
                    byte alpha = (byte)(40 + (hash >> 8 & 0x2F));
                    int sz = (hash >> 12 & 1) == 0 ? 2 : 3;
                    renderer.DrawRectScreen((int)px, (int)py, sz, sz,
                        new Color4(210, 220, 230, alpha));
                }
                break;

            case PlanetType.Volcanic:
                // Floating ash / embers
                particleCount = 30;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 338947 + (int)(globalTime * 50) * 11;
                    float drift = (float)Math.Sin(globalTime * 1.5 + i * 1.3) * 20f;
                    float px = ((hash & 0xFFFF) % screenW) + drift;
                    float speed = 40f + (hash >> 16 & 0x3F);
                    float py = (float)(screenH - (i * 61.7 + globalTime * speed) % (screenH + 10));
                    bool isEmber = (hash >> 12 & 3) == 0;
                    var color = isEmber
                        ? new Color4(200, 100, 30, 50)
                        : new Color4(80, 70, 60, 30);
                    renderer.DrawRectScreen((int)px, (int)py, 2, 2, color);
                }
                break;

            default:
                // No weather for Rocky/GasGiant/IceGiant
                break;
        }
    }

    /// <summary>
    /// Draws the exterior background visible through Void tiles.
    /// Stations show space with stars; settlements show planet terrain.
    /// </summary>
    private static void RenderExteriorBackground(ISpriteRenderer renderer, Camera camera,
        InteriorData interior, PlanetData? planet, double globalTime)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();

        // Extend slightly beyond visible area
        int margin = GameConfig.TileSize * 2;
        float bgLeft = topLeft.X - margin;
        float bgTop = topLeft.Y - margin;
        float bgRight = bottomRight.X + margin;
        float bgBottom = bottomRight.Y + margin;
        float bgW = bgRight - bgLeft;
        float bgH = bgBottom - bgTop;
        var bgCenter = new Vector2(bgLeft + bgW / 2f, bgTop + bgH / 2f);

        if (interior.Type == InteriorType.SpaceStation)
        {
            RenderSpaceBackground(renderer, camera, bgCenter, bgW, bgH, bgLeft, bgTop, bgRight, bgBottom);
        }
        else
        {
            RenderTerrainBackground(renderer, camera, bgCenter, bgW, bgH,
                bgLeft, bgTop, bgRight, bgBottom, planet);
            RenderBiomeVegetation(renderer, camera,
                bgLeft, bgTop, bgRight, bgBottom, planet, globalTime);
        }

        // Draw interior boundary outline to make the structure edges visible
        RenderBoundaryOutline(renderer, camera, interior);
    }

    private static void RenderSpaceBackground(ISpriteRenderer renderer, Camera camera,
        Vector2 bgCenter, float bgW, float bgH, float bgLeft, float bgTop, float bgRight, float bgBottom)
    {
        // Space background: dark blue-black
        renderer.DrawRect(camera, bgCenter, (int)bgW, (int)bgH, new Color3(4, 4, 12));

        // Deterministic stars based on visible area
        int starGridSize = 60;
        int sx0 = (int)MathF.Floor(bgLeft / starGridSize) - 1;
        int sy0 = (int)MathF.Floor(bgTop / starGridSize) - 1;
        int sx1 = (int)MathF.Ceiling(bgRight / starGridSize) + 1;
        int sy1 = (int)MathF.Ceiling(bgBottom / starGridSize) + 1;

        for (int sx = sx0; sx <= sx1; sx++)
        {
            for (int sy = sy0; sy <= sy1; sy++)
            {
                int h = (sx * 374761393 + sy * 668265263) ^ (sx * 17 + sy * 31);
                if ((h & 3) != 0) continue; // ~25% density

                float px = sx * starGridSize + ((h >> 4) & 0x3F) - 32;
                float py = sy * starGridSize + ((h >> 10) & 0x3F) - 32;

                int bright = 80 + ((h >> 16) & 0x7F);
                byte sr = (byte)Math.Min(255, bright + ((h >> 2) & 0x1F));
                byte sg = (byte)Math.Min(255, bright + ((h >> 5) & 0x0F));
                byte sb = (byte)Math.Min(255, bright + ((h >> 8) & 0x2F));

                int starSize = ((h >> 20) & 1) == 0 ? 2 : 1;
                renderer.DrawRect(camera, new Vector2(px, py), starSize, starSize, new Color3(sr, sg, sb));
            }
        }
    }

    private static void RenderTerrainBackground(ISpriteRenderer renderer, Camera camera,
        Vector2 bgCenter, float bgW, float bgH, float bgLeft, float bgTop, float bgRight, float bgBottom,
        PlanetData? planet)
    {
        byte tr = (byte)((planet?.Color.R ?? 80) * 0.4f);
        byte tg = (byte)((planet?.Color.G ?? 120) * 0.4f);
        byte tb = (byte)((planet?.Color.B ?? 60) * 0.4f);

        renderer.DrawRect(camera, bgCenter, (int)bgW, (int)bgH, new Color3(tr, tg, tb));

        // Terrain detail: scattered dots for ground texture
        int detailGridSize = 40;
        int dx0 = (int)MathF.Floor(bgLeft / detailGridSize) - 1;
        int dy0 = (int)MathF.Floor(bgTop / detailGridSize) - 1;
        int dx1 = (int)MathF.Ceiling(bgRight / detailGridSize) + 1;
        int dy1 = (int)MathF.Ceiling(bgBottom / detailGridSize) + 1;

        for (int dx = dx0; dx <= dx1; dx++)
        {
            for (int dy = dy0; dy <= dy1; dy++)
            {
                int h = (dx * 374761393 + dy * 668265263) ^ (dx * 13 + dy * 29);
                if ((h & 7) > 2) continue;

                float px = dx * detailGridSize + ((h >> 4) & 0x1F) - 16;
                float py = dy * detailGridSize + ((h >> 10) & 0x1F) - 16;

                int var_ = ((h >> 16) & 0x1F) - 16;
                byte dr = (byte)Math.Clamp(tr + var_, 0, 255);
                byte dg = (byte)Math.Clamp(tg + var_, 0, 255);
                byte db = (byte)Math.Clamp(tb + var_, 0, 255);

                renderer.DrawRect(camera, new Vector2(px, py), 3, 3, new Color3(dr, dg, db));
            }
        }
    }

    /// <summary>Renders biome-specific vegetation and details on the terrain background.</summary>
    private static void RenderBiomeVegetation(ISpriteRenderer renderer, Camera camera,
        float bgLeft, float bgTop, float bgRight, float bgBottom,
        PlanetData? planet, double globalTime)
    {
        var biome = planet?.Type ?? PlanetType.Rocky;
        int gridSize = 80;
        int gx0 = (int)MathF.Floor(bgLeft / gridSize) - 1;
        int gy0 = (int)MathF.Floor(bgTop / gridSize) - 1;
        int gx1 = (int)MathF.Ceiling(bgRight / gridSize) + 1;
        int gy1 = (int)MathF.Ceiling(bgBottom / gridSize) + 1;

        for (int gx = gx0; gx <= gx1; gx++)
        {
            for (int gy = gy0; gy <= gy1; gy++)
            {
                int h = (gx * 472882027 + gy * 338947111) ^ (gx * 19 + gy * 37);
                if ((h & 7) > 3) continue;

                float px = gx * gridSize + ((h >> 4) & 0x3F) - 32;
                float py = gy * gridSize + ((h >> 10) & 0x3F) - 32;
                int variant = (h >> 16) & 3;
                float sway = (float)Math.Sin(globalTime * 1.2 + h * 0.01) * 2f;

                RenderBiomeSprite(renderer, camera, biome, px, py, variant, sway);
            }
        }
    }

    /// <summary>Draws a single biome-specific vegetation/decoration sprite.</summary>
    private static void RenderBiomeSprite(ISpriteRenderer renderer, Camera camera,
        PlanetType biome, float px, float py, int variant, float sway)
    {
        switch (biome)
        {
            case PlanetType.Terrestrial:
            case PlanetType.Ocean:
                // Bushes and grass tufts
                if (variant < 2)
                {
                    // Bush: cluster of green circles
                    var leafColor = new Color3(30, (byte)(70 + variant * 20), 25);
                    renderer.DrawRect(camera, new Vector2(px + sway * 0.5f, py - 4), 8, 6, leafColor);
                    renderer.DrawRect(camera, new Vector2(px - 3 + sway * 0.5f, py - 2), 6, 5, leafColor);
                    renderer.DrawRect(camera, new Vector2(px + 3 + sway * 0.5f, py - 2), 6, 5, leafColor);
                    // Trunk
                    renderer.DrawRect(camera, new Vector2(px, py + 2), 2, 4, new Color3(60, 40, 25));
                }
                else
                {
                    // Grass tuft: thin vertical lines
                    var grass = new Color3(40, (byte)(80 + variant * 10), 30);
                    for (int i = -2; i <= 2; i++)
                        renderer.DrawRect(camera, new Vector2(px + i * 2 + sway, py - 3), 1, 6, grass);
                }
                break;

            case PlanetType.Desert:
                // Cacti and desert rocks
                if (variant == 0)
                {
                    // Cactus
                    var cactus = new Color3(50, 90, 40);
                    renderer.DrawRect(camera, new Vector2(px, py - 6), 3, 12, cactus);
                    renderer.DrawRect(camera, new Vector2(px - 4, py - 4), 3, 6, cactus);
                    renderer.DrawRect(camera, new Vector2(px + 4, py - 2), 3, 6, cactus);
                }
                else
                {
                    // Desert rock
                    var rock = new Color3(120, 100, 70);
                    renderer.DrawRect(camera, new Vector2(px, py), (int)(5 + variant), 3, rock);
                }
                break;

            case PlanetType.Frozen:
                // Ice crystals and snow mounds
                if (variant == 0)
                {
                    // Ice crystal
                    var ice = new Color3(160, 200, 230);
                    renderer.DrawRect(camera, new Vector2(px, py - 5), 2, 10, ice);
                    renderer.DrawRect(camera, new Vector2(px - 3, py - 2), 6, 2, ice);
                }
                else
                {
                    // Snow mound
                    var snow = new Color3(200, 210, 220);
                    renderer.DrawRect(camera, new Vector2(px, py), 6 + variant, 3, snow);
                    renderer.DrawRect(camera, new Vector2(px, py - 2), 4, 2, snow);
                }
                break;

            case PlanetType.Volcanic:
                // Lava vents and charred rocks
                if (variant == 0)
                {
                    // Vent with glow
                    renderer.DrawRect(camera, new Vector2(px, py), 4, 2, new Color3(60, 30, 20));
                    renderer.DrawRect(camera, new Vector2(px, py - 1), 2, 2, new Color3(180, 80, 20));
                }
                else
                {
                    // Basalt rock
                    renderer.DrawRect(camera, new Vector2(px, py), 5 + variant, 3, new Color3(40, 35, 30));
                }
                break;

            default:
                // Rocky / generic: scattered stones
                var stoneColor = new Color3(80, 75, 70);
                renderer.DrawRect(camera, new Vector2(px, py), 3 + variant, 2, stoneColor);
                break;
        }
    }

    private static void RenderBoundaryOutline(ISpriteRenderer renderer, Camera camera, InteriorData interior)
    {
        float interiorPixelW = interior.Width * GameConfig.TileSize;
        float interiorPixelH = interior.Height * GameConfig.TileSize;
        var tl = new Vector2(0, 0);
        var tr = new Vector2(interiorPixelW, 0);
        var bl = new Vector2(0, interiorPixelH);
        var br = new Vector2(interiorPixelW, interiorPixelH);

        byte lr = interior.Type == InteriorType.SpaceStation ? (byte)40 : (byte)50;
        byte lg = interior.Type == InteriorType.SpaceStation ? (byte)50 : (byte)45;
        byte lb = interior.Type == InteriorType.SpaceStation ? (byte)80 : (byte)35;

        var lineColor = new Color3(lr, lg, lb);
        renderer.DrawLine(camera, tl, tr, lineColor);
        renderer.DrawLine(camera, tr, br, lineColor);
        renderer.DrawLine(camera, br, bl, lineColor);
        renderer.DrawLine(camera, bl, tl, lineColor);
    }

    /// <summary>Renders tiles using TileMapRenderer with interior-specific detail overlays.</summary>
    private static void RenderTiles(ISpriteRenderer renderer, Camera camera,
        InteriorData interior, double globalTime)
    {
        renderer.RenderTiles(camera, interior.Width, interior.Height,
            (x, y) =>
            {
                var tile = interior.Tiles[x, y];
                if (tile == InteriorTileType.Void) return null;
                return InteriorGenerator.GetTileColor(tile);
            },
            1200f,
            (x, y, worldPos, hash) =>
            {
                var tile = interior.Tiles[x, y];
                int ts = GameConfig.TileSize;
                var roomFunc = (x >= 0 && x < interior.Width && y >= 0 && y < interior.Height)
                    ? interior.RoomTiles[x, y] : null;

                // Floor detail patterns based on room function
                if (tile == InteriorTileType.Floor && roomFunc != null)
                {
                    switch (roomFunc.Value)
                    {
                        // Station corridors / cargo: metal grate pattern
                        case RoomFunction.Corridor:
                        case RoomFunction.CargoBay:
                            if ((x + y) % 3 == 0)
                                renderer.DrawRect(camera, worldPos, ts - 2, 1, new Color3(50, 50, 58));
                            if ((x - y) % 4 == 0)
                                renderer.DrawRect(camera, worldPos, 1, ts - 2, new Color3(50, 50, 58));
                            break;
                        // Medbay: clean tile with cross markers
                        case RoomFunction.Medbay:
                            if ((x + y) % 4 == 0)
                            {
                                renderer.DrawRect(camera, worldPos, 4, 1, new Color3(80, 80, 90));
                                renderer.DrawRect(camera, worldPos, 1, 4, new Color3(80, 80, 90));
                            }
                            break;
                        // Command: subtle grid overlay
                        case RoomFunction.CommandCenter:
                            renderer.DrawRect(camera, worldPos + new Vector2(ts / 2f - 1, 0), 1, ts, new Color3(55, 55, 65));
                            renderer.DrawRect(camera, worldPos + new Vector2(0, ts / 2f - 1), ts, 1, new Color3(55, 55, 65));
                            break;
                    }
                }

                // Street tile detail: dirt/gravel texture
                if (tile == InteriorTileType.StreetTile)
                {
                    int dh = (hash >> 8) & 0xF;
                    if (dh < 4)
                    {
                        float dx = ((hash >> 4) & 0xF) - 8;
                        float dy = ((hash >> 12) & 0xF) - 8;
                        renderer.DrawRect(camera, worldPos + new Vector2(dx, dy), 2, 2,
                            new Color3((byte)(45 + dh * 3), (byte)(42 + dh * 2), (byte)(38 + dh * 2)));
                    }
                }

                // Wall detail: highlight top edge
                if (tile == InteriorTileType.Wall)
                {
                    var topEdge = new Vector2(x * ts + ts / 2f, y * ts + 2);
                    renderer.DrawRect(camera, topEdge, ts, 2, new Color3(55, 55, 65));
                }

                // Window: translucent blue pane with highlight
                if (tile == InteriorTileType.Window)
                {
                    float shimmer = MathF.Sin((float)globalTime * 1.5f + x * 3 + y * 7) * 0.15f + 0.85f;
                    byte wr = (byte)(30 * shimmer);
                    byte wg = (byte)(50 * shimmer);
                    byte wb = (byte)(90 * shimmer);
                    renderer.DrawRect(camera, worldPos, ts - 4, ts - 4, new Color3(wr, wg, wb));
                    // Highlight line
                    renderer.DrawRect(camera, worldPos + new Vector2(-4, -4), 2, ts - 8, new Color3(60, 80, 120));
                }

                // Console glow
                if (tile == InteriorTileType.Console)
                {
                    float pulse = MathF.Sin((float)globalTime * 3f + x + y) * 0.3f + 0.7f;
                    byte gr = (byte)(40 * pulse);
                    byte gg = (byte)(120 * pulse);
                    byte gb = (byte)(180 * pulse);
                    renderer.DrawRect(camera, worldPos, ts - 8, ts - 8, new Color3(gr, gg, gb));
                }

                // Crate detail: cross pattern
                if (tile == InteriorTileType.Crate)
                {
                    renderer.DrawRect(camera, worldPos, ts - 6, 2, new Color3(120, 100, 60));
                    renderer.DrawRect(camera, worldPos, 2, ts - 6, new Color3(120, 100, 60));
                }

                // Landing pad markings
                if (tile == InteriorTileType.LandingPad)
                {
                    if ((x + y) % 2 == 0)
                        renderer.DrawRect(camera, worldPos, 4, 4, new Color3(80, 80, 40));
                }

                // Table: dark surface with edge highlight
                if (tile == InteriorTileType.Table)
                {
                    renderer.DrawRect(camera, worldPos, ts - 6, ts - 6, new Color3(75, 55, 40));
                    renderer.DrawRect(camera, worldPos + new Vector2(0, -(ts / 2 - 4)), ts - 6, 2, new Color3(110, 85, 60));
                }

                // Chair: small seat
                if (tile == InteriorTileType.Chair)
                {
                    renderer.DrawRect(camera, worldPos, ts - 14, ts - 14, new Color3(80, 65, 55));
                    renderer.DrawRect(camera, worldPos + new Vector2(0, -4), ts - 16, 2, new Color3(95, 75, 60));
                }

                // Plant: green leaves over brown pot
                if (tile == InteriorTileType.Plant)
                {
                    // Pot
                    renderer.DrawRect(camera, worldPos + new Vector2(0, 4), 8, 8, new Color3(100, 70, 40));
                    // Leaves (sway slightly)
                    float sway = MathF.Sin((float)globalTime * 2f + x * 5 + y * 3) * 2f;
                    renderer.DrawRect(camera, worldPos + new Vector2(sway - 3, -4), 6, 8, new Color3(40, 110, 50));
                    renderer.DrawRect(camera, worldPos + new Vector2(sway + 3, -6), 6, 6, new Color3(50, 130, 55));
                    renderer.DrawRect(camera, worldPos + new Vector2(sway, -8), 4, 4, new Color3(60, 140, 60));
                }

                // Rug: subtle patterned overlay
                if (tile == InteriorTileType.Rug)
                {
                    renderer.DrawRect(camera, worldPos, ts - 2, ts - 2, new Color3(80, 50, 60));
                    // Inner pattern
                    if ((x + y) % 2 == 0)
                        renderer.DrawRect(camera, worldPos, ts - 10, ts - 10, new Color3(95, 60, 70));
                }

                // Pipe: horizontal pipe with rivet details
                if (tile == InteriorTileType.Pipe)
                {
                    renderer.DrawRect(camera, worldPos, ts - 4, 6, new Color3(80, 80, 90));
                    // Rivets
                    renderer.DrawRect(camera, worldPos + new Vector2(-6, 0), 3, 3, new Color3(100, 100, 110));
                    renderer.DrawRect(camera, worldPos + new Vector2(6, 0), 3, 3, new Color3(100, 100, 110));
                    // Steam wisps from pipe joints
                    float steamPhase = (float)globalTime * 1.5f + x * 13 + y * 7;
                    float steamY = MathF.Sin(steamPhase) * 4f - 6f;
                    float steamAlpha = (MathF.Sin(steamPhase * 0.7f) + 1f) * 0.3f;
                    if (steamAlpha > 0.1f)
                    {
                        byte sa = (byte)(60 * steamAlpha);
                        renderer.DrawRect(camera, worldPos + new Vector2(0, steamY), 6, 4,
                            new Color3((byte)(150 + sa / 2), (byte)(150 + sa / 2), (byte)(160 + sa / 2)));
                    }
                }

                // Light: glowing circle with occasional flicker
                if (tile == InteriorTileType.Light)
                {
                    // Flicker: occasional dip based on hash + time
                    float flickerSeed = x * 37 + y * 53;
                    float flickerVal = MathF.Sin((float)globalTime * 8f + flickerSeed) *
                                       MathF.Sin((float)globalTime * 13f + flickerSeed * 0.7f);
                    float flicker = flickerVal > 0.85f ? 0.5f : 1f; // occasional dim

                    float glow = (MathF.Sin((float)globalTime * 2f + x * 7) * 0.1f + 0.9f) * flicker;
                    byte lr = (byte)(200 * glow);
                    byte lg = (byte)(195 * glow);
                    byte lb = (byte)(140 * glow);
                    renderer.DrawRect(camera, worldPos, 6, 6, new Color3(lr, lg, lb));
                    // Outer glow
                    renderer.DrawRect(camera, worldPos, 14, 14, new Color3((byte)(lr / 4), (byte)(lg / 4), (byte)(lb / 4)));
                    // Floor glow circle
                    renderer.DrawRect(camera, worldPos, 20, 20, new Color3((byte)(lr / 8), (byte)(lg / 8), (byte)(lb / 8)));
                }

                // Shelf: stacked horizontal bars
                if (tile == InteriorTileType.Shelf)
                {
                    renderer.DrawRect(camera, worldPos + new Vector2(0, -6), ts - 6, 3, new Color3(95, 80, 60));
                    renderer.DrawRect(camera, worldPos, ts - 6, 3, new Color3(90, 75, 55));
                    renderer.DrawRect(camera, worldPos + new Vector2(0, 6), ts - 6, 3, new Color3(85, 70, 50));
                    // Items on shelves (small colored dots)
                    renderer.DrawRect(camera, worldPos + new Vector2(-4, -8), 3, 3, new Color3(120, 60, 60));
                    renderer.DrawRect(camera, worldPos + new Vector2(4, -2), 3, 3, new Color3(60, 120, 80));
                }

                // Bed: pillow and blanket
                if (tile == InteriorTileType.Bed)
                {
                    renderer.DrawRect(camera, worldPos, ts - 4, ts - 4, new Color3(60, 50, 75));
                    // Pillow
                    renderer.DrawRect(camera, worldPos + new Vector2(0, -(ts / 2 - 6)), ts - 10, 6, new Color3(180, 175, 160));
                    // Blanket fold line
                    renderer.DrawRect(camera, worldPos + new Vector2(0, 2), ts - 8, 2, new Color3(80, 65, 95));
                }

                // Bar counter: wooden surface with tap
                if (tile == InteriorTileType.BarCounter)
                {
                    renderer.DrawRect(camera, worldPos, ts - 2, ts - 4, new Color3(90, 65, 45));
                    renderer.DrawRect(camera, worldPos + new Vector2(0, -(ts / 2 - 3)), ts - 2, 2, new Color3(110, 80, 55));
                    // Drinks (small colored squares)
                    if ((x + y) % 3 == 0)
                        renderer.DrawRect(camera, worldPos + new Vector2(2, 2), 4, 5, new Color3(160, 120, 40));
                }

                // Generator: machinery with pulsing core
                if (tile == InteriorTileType.Generator)
                {
                    renderer.DrawRect(camera, worldPos, ts - 4, ts - 4, new Color3(55, 65, 70));
                    float pulse = MathF.Sin((float)globalTime * 4f + x + y) * 0.4f + 0.6f;
                    byte er = (byte)(80 * pulse);
                    byte eg = (byte)(180 * pulse);
                    byte eb = (byte)(100 * pulse);
                    renderer.DrawRect(camera, worldPos, 8, 8, new Color3(er, eg, eb));
                }

                // Antenna: tall vertical structure
                if (tile == InteriorTileType.Antenna)
                {
                    renderer.DrawRect(camera, worldPos, 4, ts - 4, new Color3(70, 85, 95));
                    // Blinking light at top
                    float blink = MathF.Sin((float)globalTime * 5f) > 0 ? 1f : 0.3f;
                    renderer.DrawRect(camera, worldPos + new Vector2(0, -(ts / 2 - 4)), 4, 4,
                        new Color3((byte)(255 * blink), (byte)(50 * blink), (byte)(50 * blink)));
                }
            });
    }

    /// <summary>Renders a subtle per-room ambient color tint overlay.</summary>
    private static void RenderRoomAmbientTint(ISpriteRenderer renderer, Camera camera, InteriorData interior)
    {
        foreach (var room in interior.Rooms)
        {
            var tint = GetRoomAmbientTint(room.Function);
            if (tint.A == 0) continue;

            var r = room.TileRect;
            float roomWorldX = (r.X + r.Width / 2f) * GameConfig.TileSize;
            float roomWorldY = (r.Y + r.Height / 2f) * GameConfig.TileSize;
            float roomWorldW = r.Width * GameConfig.TileSize;
            float roomWorldH = r.Height * GameConfig.TileSize;

            renderer.DrawRect(camera,
                new Vector2(roomWorldX, roomWorldY),
                (int)roomWorldW, (int)roomWorldH, tint);
        }
    }

    /// <summary>Returns a subtle tint color per room function.</summary>
    private static Color4 GetRoomAmbientTint(RoomFunction func) => func switch
    {
        RoomFunction.Medbay => new Color4(30, 80, 120, 15),
        RoomFunction.Cantina => new Color4(120, 80, 30, 15),
        RoomFunction.CommandCenter => new Color4(30, 40, 100, 12),
        RoomFunction.DockingBay => new Color4(40, 40, 30, 10),
        RoomFunction.CargoBay => new Color4(50, 45, 30, 12),
        RoomFunction.CrewQuarters => new Color4(60, 50, 80, 10),
        RoomFunction.TradingPost => new Color4(80, 70, 40, 12),
        RoomFunction.Market => new Color4(90, 80, 40, 12),
        RoomFunction.Housing => new Color4(70, 60, 50, 10),
        RoomFunction.Generator => new Color4(40, 80, 50, 12),
        RoomFunction.CommsCenter => new Color4(40, 60, 90, 12),
        RoomFunction.LandingPad => new Color4(50, 50, 40, 8),
        _ => new Color4(0, 0, 0, 0)
    };

    /// <summary>Renders room name labels above each room.</summary>
    private static void RenderRoomLabels(ISpriteRenderer renderer, Camera camera, InteriorData interior)
    {
        foreach (var room in interior.Rooms)
        {
            float roomLabelW = renderer.MeasureText(room.Name, 3f) / 2f / camera.Zoom;
            var labelPos = new Vector2(
                (room.TileRect.X + room.TileRect.Width / 2f) * GameConfig.TileSize - roomLabelW,
                room.TileRect.Y * GameConfig.TileSize - 8
            );
            renderer.DrawText(camera, labelPos, room.Name, new Color3(120, 120, 160), 3f);
        }
    }

    /// <summary>Renders all NPCs with body, head, accessories, nametag, role tag, and idle animation.</summary>
    private static void RenderNpcs(ISpriteRenderer renderer, Camera camera, InteriorData interior,
        double globalTime)
    {
        foreach (var npc in interior.Npcs)
        {
            // Each NPC gets a unique phase offset based on name hash
            float phaseOffset = (npc.Name.GetHashCode() & 0xFFFF) * 0.01f;
            float time = (float)globalTime;
            float scale = npc.BodyScale;

            // Idle breathing: body scale pulsing
            float breathe = MathF.Sin(time * 1.5f + phaseOffset) * 1.5f;

            // Weight shift: subtle horizontal sway
            float sway = MathF.Sin(time * 0.8f + phaseOffset * 2f) * 1f;

            var npcPos = new Vector2(
                npc.TilePos.X * GameConfig.TileSize + GameConfig.TileSize / 2f + sway,
                npc.TilePos.Y * GameConfig.TileSize + GameConfig.TileSize / 2f
            );

            // Shadow beneath feet (scaled)
            var shadowPos = npcPos + new Vector2(0, 8);
            renderer.DrawRect(camera, shadowPos, (int)(12 * scale), 3, RenderColors.EntityShadow);

            // Legs
            renderer.DrawRect(camera, npcPos + new Vector2(-3 * scale, 6), (int)(3 * scale), 4,
                new Color3((byte)(npc.Color.R * 0.6f), (byte)(npc.Color.G * 0.6f), (byte)(npc.Color.B * 0.6f)));
            renderer.DrawRect(camera, npcPos + new Vector2(3 * scale, 6), (int)(3 * scale), 4,
                new Color3((byte)(npc.Color.R * 0.6f), (byte)(npc.Color.G * 0.6f), (byte)(npc.Color.B * 0.6f)));

            // Body (breathing + scale)
            int bodyW = (int)(10 * scale);
            int bodyH = (int)(14 + breathe * 0.4f);
            renderer.DrawRect(camera, npcPos + new Vector2(0, -breathe * 0.3f),
                bodyW, bodyH, npc.Color);

            // Head
            float headTurn = MathF.Sin(time * 0.5f + phaseOffset * 3f) * 2f;
            var headPos = npcPos - new Vector2(-headTurn, 8 + breathe * 0.5f);
            var headColor = new Color3(
                (byte)Math.Min(npc.Color.R + 30, 255),
                (byte)Math.Min(npc.Color.G + 30, 255),
                (byte)Math.Min(npc.Color.B + 30, 255));
            renderer.DrawRect(camera, headPos, 8, 8, headColor);

            // Eyes
            float eyeDir = MathF.Sign(headTurn);
            renderer.DrawRect(camera, headPos + new Vector2(-2 + eyeDir, -1), 2, 2,
                new Color3(30, 30, 40));
            renderer.DrawRect(camera, headPos + new Vector2(2 + eyeDir, -1), 2, 2,
                new Color3(30, 30, 40));

            // Accessory rendering
            switch (npc.Accessory)
            {
                case 1: // Hat — rectangle above head
                    renderer.DrawRect(camera, headPos - new Vector2(0, 5), 10, 4,
                        new Color3((byte)(npc.Color.R * 0.7f), (byte)(npc.Color.G * 0.7f), (byte)(npc.Color.B * 0.7f)));
                    renderer.DrawRect(camera, headPos - new Vector2(0, 7), 6, 3,
                        new Color3(npc.Color.R, npc.Color.G, npc.Color.B));
                    break;
                case 2: // Helmet — wider head cover
                    renderer.DrawRect(camera, headPos - new Vector2(0, 2), 10, 10,
                        new Color3(80, 85, 95));
                    renderer.DrawRect(camera, headPos - new Vector2(0, 0), 8, 3,
                        new Color3(60, 120, 160));
                    break;
                case 3: // Hood — triangular drape
                    renderer.DrawRect(camera, headPos - new Vector2(0, 4), 10, 6,
                        new Color3((byte)(npc.Color.R * 0.5f), (byte)(npc.Color.G * 0.5f), (byte)(npc.Color.B * 0.5f)));
                    renderer.DrawRect(camera, headPos + new Vector2(-5, 0), 2, 4,
                        new Color3((byte)(npc.Color.R * 0.4f), (byte)(npc.Color.G * 0.4f), (byte)(npc.Color.B * 0.4f)));
                    renderer.DrawRect(camera, headPos + new Vector2(5, 0), 2, 4,
                        new Color3((byte)(npc.Color.R * 0.4f), (byte)(npc.Color.G * 0.4f), (byte)(npc.Color.B * 0.4f)));
                    break;
            }

            // Nametag (centered)
            float nameW = renderer.MeasureText(npc.Name, 1.5f) / 2f / camera.Zoom;
            var namePos = npcPos - new Vector2(nameW, 20 + (npc.Accessory > 0 ? 4 : 0));
            renderer.DrawText(camera, namePos, npc.Name, new Color3(200, 200, 200), 1.5f);

            // Role tag (centered)
            float roleW = renderer.MeasureText(npc.Role, 1.5f) / 2f / camera.Zoom;
            var rolePos = npcPos + new Vector2(-roleW, 12);
            renderer.DrawText(camera, rolePos, npc.Role, npc.Color, 1.5f);
        }
    }

    /// <summary>Renders floating indicator markers and recognizable icons above each interactable.</summary>
    private static void RenderInteractableMarkers(ISpriteRenderer renderer, Camera camera,
        InteriorData interior, double globalTime)
    {
        foreach (var interactable in interior.Interactables)
        {
            var intPos = new Vector2(
                interactable.TilePos.X * GameConfig.TileSize + GameConfig.TileSize / 2f,
                interactable.TilePos.Y * GameConfig.TileSize + GameConfig.TileSize / 2f
            );

            var intColor = GetInteractableColor(interactable.Type);
            float bob = MathF.Sin((float)globalTime * 2f) * 3f;

            // Render recognizable base object on the tile
            RenderInteractableObject(renderer, camera, intPos, interactable.Type, intColor, globalTime);

            // Floating indicator above
            var indicatorPos = intPos - new Vector2(0, 22 + bob);
            // Indicator diamond shape
            renderer.DrawRect(camera, indicatorPos, 5, 5, intColor);
            renderer.DrawRect(camera, indicatorPos, 3, 3, new Color3(255, 255, 255));

            // Label
            float intLabelW = renderer.MeasureText(interactable.Name, 1.5f) / 2f / camera.Zoom;
            renderer.DrawText(camera, indicatorPos - new Vector2(intLabelW, 10),
                interactable.Name, intColor, 1.5f);
        }
    }

    /// <summary>Renders a recognizable object for each interactable type on the tile.</summary>
    private static void RenderInteractableObject(
        ISpriteRenderer renderer, Camera camera, Vector2 pos,
        InteractableType type, Color3 color, double globalTime)
    {
        float time = (float)globalTime;

        switch (type)
        {
            case InteractableType.RepairStation:
                // Workbench with wrench icon
                renderer.DrawRect(camera, pos + new Vector2(0, 4), 20, 6, new Color3(70, 60, 50));
                // Wrench shape (two rects forming angle)
                renderer.DrawRect(camera, pos + new Vector2(-3, -2), 3, 10, new Color3(160, 160, 170));
                renderer.DrawRect(camera, pos + new Vector2(1, -6), 6, 3, new Color3(160, 160, 170));
                break;

            case InteractableType.HealthStation:
                // Medical capsule with cross symbol
                renderer.DrawRect(camera, pos, 16, 18, new Color3(40, 60, 70));
                float medPulse = MathF.Sin(time * 2f) * 0.2f + 0.8f;
                byte mr = (byte)(100 * medPulse);
                byte mg = (byte)(200 * medPulse);
                byte mb = (byte)(255 * medPulse);
                renderer.DrawRect(camera, pos, 8, 2, new Color3(mr, mg, mb));
                renderer.DrawRect(camera, pos, 2, 8, new Color3(mr, mg, mb));
                break;

            case InteractableType.MissionBoard:
                // Screen with scrolling lines
                renderer.DrawRect(camera, pos, 18, 14, new Color3(20, 25, 35));
                renderer.DrawRect(camera, pos, 16, 12, new Color3(30, 45, 70));
                int lineOffset = (int)(time * 2f) % 4;
                for (int i = 0; i < 3; i++)
                {
                    int lw = 8 + ((i + lineOffset) % 3) * 2;
                    renderer.DrawRect(camera, pos + new Vector2(-2, -3 + i * 4), lw, 2,
                        new Color3(80, 160, 220));
                }
                break;

            case InteractableType.ShipCustomization:
                // Ship silhouette on a terminal
                renderer.DrawRect(camera, pos + new Vector2(0, 4), 18, 6, new Color3(50, 55, 65));
                // Mini ship shape
                renderer.DrawRect(camera, pos + new Vector2(0, -2), 8, 4, color);
                renderer.DrawRect(camera, pos + new Vector2(-4, -2), 4, 2, color);
                renderer.DrawRect(camera, pos + new Vector2(4, -2), 4, 2, color);
                break;

            case InteractableType.AvatarCustomization:
                // Mirror / mannequin stand
                renderer.DrawRect(camera, pos + new Vector2(0, 6), 4, 6, new Color3(80, 80, 90));
                renderer.DrawRect(camera, pos + new Vector2(0, -2), 12, 14,
                    new Color3(40, 50, 70));
                // Reflection shimmer
                float shimmer = MathF.Sin(time * 3f) * 0.3f + 0.7f;
                renderer.DrawRect(camera, pos + new Vector2(-2, -4), 2, 8,
                    new Color3((byte)(100 * shimmer), (byte)(110 * shimmer), (byte)(130 * shimmer)));
                break;

            case InteractableType.VehicleCustomization:
                // Vehicle outline on terminal
                renderer.DrawRect(camera, pos + new Vector2(0, 4), 18, 6, new Color3(50, 55, 65));
                // Mini vehicle shape
                renderer.DrawRect(camera, pos + new Vector2(0, -2), 12, 5, color);
                renderer.DrawRect(camera, pos + new Vector2(-5, 2), 3, 3, new Color3(60, 60, 60));
                renderer.DrawRect(camera, pos + new Vector2(5, 2), 3, 3, new Color3(60, 60, 60));
                break;

            case InteractableType.ShipDealer:
                // Display podium with rotating ship
                renderer.DrawRect(camera, pos + new Vector2(0, 5), 20, 4, new Color3(60, 55, 50));
                float rot = time * 1.5f;
                float sx = MathF.Cos(rot) * 4f;
                renderer.DrawRect(camera, pos + new Vector2(sx, -3), 8, 4, new Color3(200, 180, 60));
                renderer.DrawRect(camera, pos + new Vector2(sx - 4, -3), 3, 2, new Color3(200, 180, 60));
                renderer.DrawRect(camera, pos + new Vector2(sx + 4, -3), 3, 2, new Color3(200, 180, 60));
                break;

            case InteractableType.CargoTerminal:
                // Cargo display with moving indicator
                renderer.DrawRect(camera, pos, 16, 14, new Color3(30, 30, 40));
                renderer.DrawRect(camera, pos, 14, 12, new Color3(50, 45, 30));
                // Cargo boxes icon
                renderer.DrawRect(camera, pos + new Vector2(-3, 1), 5, 5, new Color3(140, 110, 50));
                renderer.DrawRect(camera, pos + new Vector2(3, -1), 5, 5, new Color3(120, 100, 45));
                break;

            case InteractableType.ExitDoor:
                // Door frame with arrow
                renderer.DrawRect(camera, pos + new Vector2(-8, 0), 3, 18, new Color3(80, 80, 90));
                renderer.DrawRect(camera, pos + new Vector2(8, 0), 3, 18, new Color3(80, 80, 90));
                renderer.DrawRect(camera, pos + new Vector2(0, -8), 13, 3, new Color3(80, 80, 90));
                // Pulsing exit arrow
                float exitPulse = MathF.Sin(time * 3f) * 0.3f + 0.7f;
                renderer.DrawRect(camera, pos + new Vector2(0, 2), 6, 3,
                    new Color3((byte)(255 * exitPulse), (byte)(80 * exitPulse), (byte)(80 * exitPulse)));
                break;
        }
    }

    /// <summary>Renders the player avatar texture at the given position.</summary>
    private static void RenderPlayerAvatar(ISpriteRenderer renderer, Camera camera,
        Vector2 playerPos, AvatarRenderer avatarRenderer)
    {
        avatarRenderer.Render(renderer, camera, playerPos);
    }

    /// <summary>Renders the dialogue box with NPC info, text, and continue prompt.</summary>
    public static void RenderDialogue(ISpriteRenderer renderer, int w, int h,
        InteriorNpc npc, int dialogueLine)
    {
        float boxW = 600;
        float boxH = 120;
        float boxX = w / 2f - boxW / 2f;
        float boxY = h - boxH - 20;

        // Background
        renderer.DrawRectScreen(boxX - 2, boxY - 2, boxW + 4, boxH + 4, new Color4(60, 60, 100, 200));
        renderer.DrawRectScreen(boxX, boxY, boxW, boxH, new Color4(15, 15, 35, 240));

        // NPC name and role
        renderer.DrawTextScreen(boxX + 15, boxY + 10, npc.Name.ToUpper(),
            npc.Color, 2f);
        renderer.DrawTextScreen(boxX + 15 + renderer.MeasureText(npc.Name + "  ", 2f), boxY + 10,
            npc.Role, new Color3(120, 120, 150), 1.5f);

        // Dialogue line
        if (dialogueLine < npc.DialogueLines.Length)
        {
            string line = npc.DialogueLines[dialogueLine];

            // Word wrap at ~50 chars
            int lineY = 0;
            int charsPerLine = 55;
            for (int i = 0; i < line.Length; i += charsPerLine)
            {
                int end = Math.Min(i + charsPerLine, line.Length);
                if (end < line.Length && end > i)
                {
                    int lastSpace = line.LastIndexOf(' ', end - 1, end - i);
                    if (lastSpace > i) end = lastSpace + 1;
                }
                string segment = line[i..end].TrimEnd();
                renderer.DrawTextScreen(boxX + 15, boxY + 40 + lineY * 18, segment, new Color3(200, 200, 200), 1.5f);
                lineY++;
            }
        }

        // Continue prompt
        string continueText = dialogueLine < npc.DialogueLines.Length - 1
            ? "[ENTER] CONTINUE" : "[ENTER] CLOSE";
        renderer.DrawTextScreen(boxX + boxW - 200, boxY + boxH - 25, continueText, new Color3(100, 200, 100), 1.5f);
    }


    /// <summary>Returns the color associated with an interactable type.</summary>
    public static Color3 GetInteractableColor(InteractableType type) => type switch
    {
        InteractableType.RepairStation => new Color3(100, 255, 100),
        InteractableType.HealthStation => new Color3(100, 200, 255),
        InteractableType.MissionBoard => new Color3(100, 180, 255),
        InteractableType.ShipCustomization => new Color3(100, 220, 255),
        InteractableType.AvatarCustomization => new Color3(100, 255, 180),
        InteractableType.VehicleCustomization => new Color3(255, 180, 80),
        InteractableType.ShipDealer => new Color3(255, 200, 80),
        InteractableType.CargoTerminal => new Color3(255, 180, 50),
        InteractableType.ExitDoor => new Color3(255, 100, 100),
        _ => new Color3(200, 200, 200)
    };
}
