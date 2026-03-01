using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders planet surface visuals: terrain tiles with per-tile detail overlays,
/// height-based shading, animated effects, terrain transitions, and decorations.
/// Background stars are rendered separately via <see cref="StarsBackgroundRenderer"/>.
/// </summary>
public static class PlanetSurfaceRenderer
{
    // ── Public rendering API ─────────────────────────────────────────────────

    /// <summary>Renders the terrain tiles with full visual detail.</summary>
    public static void RenderTerrain(ISpriteRenderer renderer, Camera camera,
        PlanetSurfaceData surfaceData, double globalTime, PlanetType planetType)
    {
        int w = surfaceData.Width;
        int h = surfaceData.Height;
        var tiles = surfaceData.Tiles;
        var heightMap = surfaceData.HeightMap;
        float time = (float)globalTime;

        renderer.RenderTiles(camera, w, h,
            (x, y) =>
            {
                var terrain = tiles[x, y];
                if (terrain == TerrainType.Void) return null;
                var baseColor = PlanetSurfaceGenerator.GetTerrainColor(terrain);

                // Per-tile color variation using hash
                int tileHash = (x * 374761393 + y * 668265263) ^ (x * 17 + y * 31);
                int variation = ((tileHash >> 4) & 0xF) - 8; // -8 to +7
                byte cr = (byte)Math.Clamp(baseColor.R + variation, 0, 255);
                byte cg = (byte)Math.Clamp(baseColor.G + variation, 0, 255);
                byte cb = (byte)Math.Clamp(baseColor.B + variation, 0, 255);

                // Height-based shading: darken low areas, brighten high
                float height = heightMap[x, y];
                float shade = (height - 0.5f) * 0.15f; // +/-7.5% brightness
                cr = (byte)Math.Clamp(cr + (int)(cr * shade), 0, 255);
                cg = (byte)Math.Clamp(cg + (int)(cg * shade), 0, 255);
                cb = (byte)Math.Clamp(cb + (int)(cb * shade), 0, 255);

                return new Color3(cr, cg, cb);
            },
            800f,
            (x, y, worldPos, hash) =>
            {
                var terrain = tiles[x, y];
                if (terrain == TerrainType.Void) return;

                int ts = GameConfig.TileSize;
                float height = heightMap[x, y];

                // Terrain transition edges
                RenderTerrainEdges(renderer, camera, tiles, x, y, w, h,
                    terrain, worldPos, ts);

                // Per-terrain detail overlays
                switch (terrain)
                {
                    case TerrainType.Grass:
                        RenderGrassDetail(renderer, camera, worldPos,
                            hash, time, x, y, ts);
                        break;
                    case TerrainType.Rock:
                        RenderRockDetail(renderer, camera, worldPos,
                            hash, height, ts);
                        break;
                    case TerrainType.Sand:
                        RenderSandDetail(renderer, camera, worldPos,
                            hash, time, ts);
                        break;
                    case TerrainType.Water:
                        RenderWaterDetail(renderer, camera, worldPos,
                            hash, time, ts);
                        break;
                    case TerrainType.Ice:
                        RenderIceDetail(renderer, camera, worldPos,
                            hash, time, ts);
                        break;
                    case TerrainType.Lava:
                        RenderLavaDetail(renderer, camera, worldPos,
                            hash, time, ts);
                        break;
                    case TerrainType.Metal:
                        RenderMetalDetail(renderer, camera, worldPos,
                            hash, height, ts);
                        break;
                }

                // Biome-specific decorative objects (sparse)
                RenderBiomeDecoration(renderer, camera, worldPos,
                    hash, time, terrain, planetType, ts);
            });
    }

    /// <summary>
    /// Renders a soft atmosphere halo at the planet disc boundary, masking the
    /// jagged tile edge and providing a glow that fades into space.
    /// Call this <em>after</em> <see cref="RenderTerrain"/> so it overlays the terrain.
    /// </summary>
    public static void RenderAtmosphere(ISpriteRenderer renderer, Camera camera,
        PlanetSurfaceData surfaceData, PlanetType planetType, double globalTime)
    {
        int w = surfaceData.Width;
        int h = surfaceData.Height;
        float ts = GameConfig.TileSize;

        // Planet disc geometry in world space (tile centre = x*ts + ts/2, y*ts + ts/2)
        var center = new Vector2(w * ts * 0.5f, h * ts * 0.5f);
        float radius = (MathF.Min(w, h) * 0.5f - 2f) * ts;

        var atmColor = GetAtmosphereBaseColor(planetType);
        float pulse = 0.94f + 0.06f * MathF.Sin((float)globalTime * 0.7f);
        int seg = 64;

        // Layer 1 – inner-edge tint: subtly darkens the outermost terrain tiles
        //           so the boundary row blends rather than hard-cuts.
        renderer.DrawSolidRing(camera, center,
            radius - ts * 2.5f, radius,
            atmColor.WithAlpha((byte)Math.Clamp((int)(38 * pulse), 0, 255)), seg);

        // Layer 2 – core glow: straddles the actual disc boundary; the thick
        //           band visually masks the pixel-jagged tile steps.
        renderer.DrawSolidRing(camera, center,
            radius - ts * 0.5f, radius + ts * 1.5f,
            atmColor.WithAlpha((byte)Math.Clamp((int)(135 * pulse), 0, 255)), seg);

        // Layer 3 – mid haze: atmosphere extending into space.
        renderer.DrawSolidRing(camera, center,
            radius + ts * 1.5f, radius + ts * 3.2f,
            atmColor.WithAlpha((byte)Math.Clamp((int)(72 * pulse), 0, 255)), seg);

        // Layer 4 – outer fade: thin translucent fringe.
        renderer.DrawSolidRing(camera, center,
            radius + ts * 3.2f, radius + ts * 5.8f,
            atmColor.WithAlpha((byte)Math.Clamp((int)(32 * pulse), 0, 255)), seg);
    }

    private static Color4 GetAtmosphereBaseColor(PlanetType type) => type switch
    {
        PlanetType.Terrestrial => new Color4(115, 185, 255, 255),
        PlanetType.Ocean => new Color4(75, 155, 255, 255),
        PlanetType.Desert => new Color4(215, 175, 100, 255),
        PlanetType.Volcanic => new Color4(255, 105, 40, 255),
        PlanetType.Frozen => new Color4(195, 225, 255, 255),
        PlanetType.Rocky => new Color4(165, 155, 148, 255),
        PlanetType.GasGiant => new Color4(230, 200, 140, 255),
        PlanetType.IceGiant => new Color4(160, 210, 255, 255),
        _ => new Color4(160, 160, 175, 255),
    };

    #region Terrain Details

    private static void RenderGrassDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float time, int tx, int ty, int ts)
    {
        // Grass tufts
        if ((hash & 0x7) == 0)
        {
            float sway = MathF.Sin(time * 1.4f + tx * 3 + ty * 5) * 1.5f;
            float ox = ((hash >> 8) & 0xF) - 8;
            float oy = ((hash >> 12) & 0xF) - 8;
            byte g = (byte)Math.Clamp(170 + ((hash >> 16) & 0x1F) - 16, 0, 255);
            renderer.DrawRect(camera, pos + new Vector2(ox + sway, oy),
                6, 6, new Color3(50, g, 45));
        }
        // Occasional flower dot
        if ((hash & 0x3F) == 0)
        {
            float ox = ((hash >> 6) & 0xF) - 8;
            float oy = ((hash >> 10) & 0xF) - 8;
            int flowerType = (hash >> 14) & 3;
            var color = flowerType switch
            {
                0 => new Color3(220, 180, 50),  // yellow
                1 => new Color3(200, 80, 100),  // pink
                2 => new Color3(150, 100, 200), // purple
                _ => new Color3(220, 220, 200)  // white
            };
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                3, 3, color);
        }
    }

    private static void RenderRockDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float height, int ts)
    {
        // Highlight patches on high rock
        if ((hash & 0xF) == 0)
        {
            float ox = ((hash >> 8) & 0xF) - 8;
            float oy = ((hash >> 12) & 0xF) - 8;
            byte brightness = (byte)Math.Clamp(148 + height * 30, 0, 255);
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                4, 4, new Color3(brightness,
                    (byte)(brightness - 12), (byte)(brightness - 20)));
        }
        // Cracks
        if ((hash & 0x1F) == 1)
        {
            float ox = ((hash >> 5) & 0xF) - 8;
            float oy = ((hash >> 9) & 0xF) - 8;
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                8, 1, new Color3(100, 85, 72));
            renderer.DrawRect(camera, pos + new Vector2(ox + 2, oy + 1),
                1, 4, new Color3(100, 85, 72));
        }
    }

    private static void RenderSandDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float time, int ts)
    {
        // Ripple lines
        if ((hash & 0x7) < 2)
        {
            float oy = ((hash >> 8) & 0xF) - 8;
            byte sandBright = (byte)Math.Clamp(
                220 + ((hash >> 12) & 0xF) - 8, 0, 255);
            renderer.DrawRect(camera, pos + new Vector2(0, oy),
                ts - 4, 1, new Color3(sandBright,
                    (byte)(sandBright - 15), (byte)(sandBright - 55)));
        }
        // Pebble dot
        if ((hash & 0x1F) == 3)
        {
            float ox = ((hash >> 5) & 0xF) - 8;
            float oy = ((hash >> 9) & 0xF) - 8;
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                2, 2, new Color3(180, 160, 120));
        }
    }

    private static void RenderWaterDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float time, int ts)
    {
        // Animated wave shimmer
        float wavePhase = time * 1.2f + (hash & 0xFF) * 0.05f;
        float waveAlpha = (MathF.Sin(wavePhase) + 1f) * 0.5f;
        byte wa = (byte)(50 + waveAlpha * 60);
        float oy = ((hash >> 8) & 0x7) - 4;
        float ox = MathF.Sin(wavePhase * 0.7f) * 3f;
        renderer.DrawRect(camera, pos + new Vector2(ox, oy),
            10, 2, new Color4(80, 120, 220, wa));

        // Secondary smaller wave
        if ((hash & 0x3) == 0)
        {
            float phase2 = time * 0.8f + (hash >> 4) * 0.03f;
            float oy2 = ((hash >> 12) & 0x7) - 4;
            float ox2 = MathF.Sin(phase2) * 2f;
            renderer.DrawRect(camera, pos + new Vector2(ox2, oy2),
                6, 1, new Color4(100, 140, 240,
                    (byte)(30 + waveAlpha * 40)));
        }

        // Deep spots
        if ((hash & 0xF) == 0)
        {
            renderer.DrawRect(camera, pos, 4, 4,
                new Color3(30, 60, 150));
        }
    }

    private static void RenderIceDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float time, int ts)
    {
        // Frost crystal sparkle
        if ((hash & 0x7) == 0)
        {
            float sparkle = MathF.Sin(time * 3f + (hash & 0xFF) * 0.1f);
            if (sparkle > 0.6f)
            {
                float ox = ((hash >> 8) & 0xF) - 8;
                float oy = ((hash >> 12) & 0xF) - 8;
                byte bright = (byte)(220 + sparkle * 35);
                renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                    2, 2, new Color3(bright, bright, 255));
            }
        }
        // Crack lines
        if ((hash & 0x1F) == 2)
        {
            float ox = ((hash >> 5) & 0xF) - 8;
            float oy = ((hash >> 9) & 0xF) - 8;
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                6, 1, new Color3(180, 200, 235));
            renderer.DrawRect(camera, pos + new Vector2(ox + 3, oy),
                1, 5, new Color3(180, 200, 235));
        }
        // Snow mound
        if ((hash & 0x3F) == 4)
        {
            float ox = ((hash >> 6) & 0xF) - 8;
            float oy = ((hash >> 10) & 0xF) - 8;
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                8, 4, new Color3(230, 240, 250));
        }
    }

    private static void RenderLavaDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float time, int ts)
    {
        // Pulsing glow
        float pulse = MathF.Sin(time * 2f + (hash & 0xFF) * 0.02f)
            * 0.5f + 0.5f;
        byte glowR = (byte)(240 + pulse * 15);
        byte glowG = (byte)(60 + pulse * 50);
        byte glowB = (byte)(pulse * 30);
        renderer.DrawRect(camera, pos, ts - 8, ts - 8,
            new Color4(glowR, glowG, glowB, (byte)(40 + pulse * 30)));

        // Bright veins / fissures
        if ((hash & 0x7) < 2)
        {
            float ox = ((hash >> 8) & 0xF) - 8;
            float oy = ((hash >> 12) & 0xF) - 8;
            float brightness = (MathF.Sin(time * 3f + hash * 0.01f)
                + 1f) * 0.5f;
            byte vr = (byte)(255 * brightness);
            byte vg = (byte)(180 * brightness);
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                6, 2, new Color3(vr, vg, 20));
        }

        // Floating ember particles
        if ((hash & 0xF) == 0)
        {
            float emberY = MathF.Sin(time * 1.5f + hash * 0.03f) * 6f - 8f;
            float emberX = MathF.Cos(time * 0.9f + hash * 0.02f) * 4f;
            renderer.DrawRect(camera, pos + new Vector2(emberX, emberY),
                2, 2, new Color3(255, 200, 60));
        }
    }

    private static void RenderMetalDetail(ISpriteRenderer renderer, Camera camera,
        Vector2 pos, int hash, float height, int ts)
    {
        // Panel lines
        if ((hash & 0x3) == 0)
        {
            renderer.DrawRect(camera,
                pos + new Vector2(0, -(ts / 2 - 1)),
                ts, 1, new Color3(145, 145, 155));
        }
        if ((hash & 0x3) == 1)
        {
            renderer.DrawRect(camera,
                pos + new Vector2(-(ts / 2 - 1), 0),
                1, ts, new Color3(145, 145, 155));
        }
        // Rivets
        if ((hash & 0xF) == 0)
        {
            float ox = ((hash >> 4) & 0xF) - 8;
            float oy = ((hash >> 8) & 0xF) - 8;
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                2, 2, new Color3(180, 180, 190));
        }
        // Rust stain on high metal
        if ((hash & 0x1F) == 5 && height > 0.7f)
        {
            float ox = ((hash >> 5) & 0xF) - 8;
            float oy = ((hash >> 9) & 0xF) - 8;
            renderer.DrawRect(camera, pos + new Vector2(ox, oy),
                5, 4, new Color3(140, 110, 80));
        }
    }

    #endregion

    #region Terrain Edges

    /// <summary>
    /// Draws subtle transition edges where terrain types change.
    /// </summary>
    private static void RenderTerrainEdges(ISpriteRenderer renderer,
        Camera camera, TerrainType[,] tiles, int x, int y, int w, int h,
        TerrainType terrain, Vector2 worldPos, int ts)
    {
        if (x > 0 && tiles[x - 1, y] != terrain
            && tiles[x - 1, y] != TerrainType.Void)
        {
            var nc = PlanetSurfaceGenerator.GetTerrainColor(tiles[x - 1, y]);
            renderer.DrawRect(camera,
                worldPos + new Vector2(-(ts / 2 - 1), 0),
                3, ts, new Color4(nc.R, nc.G, nc.B, 60));
        }
        if (x < w - 1 && tiles[x + 1, y] != terrain
            && tiles[x + 1, y] != TerrainType.Void)
        {
            var nc = PlanetSurfaceGenerator.GetTerrainColor(tiles[x + 1, y]);
            renderer.DrawRect(camera,
                worldPos + new Vector2(ts / 2 - 2, 0),
                3, ts, new Color4(nc.R, nc.G, nc.B, 60));
        }
        if (y > 0 && tiles[x, y - 1] != terrain
            && tiles[x, y - 1] != TerrainType.Void)
        {
            var nc = PlanetSurfaceGenerator.GetTerrainColor(tiles[x, y - 1]);
            renderer.DrawRect(camera,
                worldPos + new Vector2(0, -(ts / 2 - 1)),
                ts, 3, new Color4(nc.R, nc.G, nc.B, 60));
        }
        if (y < h - 1 && tiles[x, y + 1] != terrain
            && tiles[x, y + 1] != TerrainType.Void)
        {
            var nc = PlanetSurfaceGenerator.GetTerrainColor(tiles[x, y + 1]);
            renderer.DrawRect(camera,
                worldPos + new Vector2(0, ts / 2 - 2),
                ts, 3, new Color4(nc.R, nc.G, nc.B, 60));
        }
    }

    #endregion

    #region Biome Decorations

    /// <summary>
    /// Renders sparse biome-specific decorative objects on the terrain.
    /// </summary>
    private static void RenderBiomeDecoration(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int hash, float time,
        TerrainType terrain, PlanetType planetType, int ts)
    {
        int decoHash = (hash * 31 + 12345) ^ (hash >> 8);
        if ((decoHash & 0x3F) != 0) return; // ~1.5% of tiles

        float ox = ((decoHash >> 6) & 0xF) - 8;
        float oy = ((decoHash >> 10) & 0xF) - 8;
        int variant = (decoHash >> 14) & 3;
        float sway = MathF.Sin(time * 1.0f + decoHash * 0.01f) * 2f;

        switch (planetType)
        {
            case PlanetType.Terrestrial:
                if (terrain == TerrainType.Grass)
                    RenderTree(renderer, camera,
                        pos + new Vector2(ox, oy), sway, variant);
                else if (terrain == TerrainType.Rock)
                    RenderBoulder(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                break;

            case PlanetType.Desert:
                if (terrain == TerrainType.Sand)
                    RenderCactus(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                else if (terrain == TerrainType.Rock)
                    RenderMesa(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                break;

            case PlanetType.Frozen:
                if (terrain == TerrainType.Ice)
                    RenderIceCrystal(renderer, camera,
                        pos + new Vector2(ox, oy), time, variant);
                else if (terrain == TerrainType.Rock)
                    RenderFrozenBoulder(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                break;

            case PlanetType.Volcanic:
                if (terrain == TerrainType.Rock)
                    RenderVolcanicVent(renderer, camera,
                        pos + new Vector2(ox, oy), time, variant);
                else if (terrain == TerrainType.Metal)
                    RenderScrapPile(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                break;

            case PlanetType.Ocean:
                if (terrain == TerrainType.Sand)
                    RenderShell(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                else if (terrain == TerrainType.Grass)
                    RenderPalmTree(renderer, camera,
                        pos + new Vector2(ox, oy), sway, variant);
                break;

            case PlanetType.Rocky:
                if (terrain is TerrainType.Rock or TerrainType.Sand)
                    RenderBoulder(renderer, camera,
                        pos + new Vector2(ox, oy), variant);
                break;
        }
    }

    // ── Decoration sprites ──────────────────────────────────

    private static void RenderTree(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, float sway, int variant)
    {
        // Trunk
        renderer.DrawRect(camera, pos + new Vector2(0, 4),
            3, 10, new Color3(80, 55, 30));
        int canopySize = 8 + variant * 2;
        renderer.DrawRect(camera,
            pos + new Vector2(sway * 0.5f, -6),
            canopySize, canopySize - 2,
            new Color3(35, (byte)(100 + variant * 15), 30));
        renderer.DrawRect(camera,
            pos + new Vector2(sway * 0.7f, -9),
            canopySize - 3, canopySize - 4,
            new Color3(45, (byte)(120 + variant * 10), 40));
    }

    private static void RenderPalmTree(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, float sway, int variant)
    {
        renderer.DrawRect(camera, pos + new Vector2(1, 3),
            2, 12, new Color3(100, 75, 40));
        renderer.DrawRect(camera, pos + new Vector2(2, 0),
            2, 4, new Color3(100, 75, 40));
        renderer.DrawRect(camera,
            pos + new Vector2(sway - 5, -5),
            10, 3, new Color3(50, 130, 40));
        renderer.DrawRect(camera,
            pos + new Vector2(sway + 3, -4),
            8, 3, new Color3(40, 110, 35));
        renderer.DrawRect(camera,
            pos + new Vector2(sway - 3, -7),
            6, 2, new Color3(55, 140, 45));
    }

    private static void RenderCactus(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int variant)
    {
        var green = new Color3(55, (byte)(100 + variant * 10), 45);
        renderer.DrawRect(camera, pos, 4, 14, green);
        if (variant != 2)
        {
            renderer.DrawRect(camera,
                pos + new Vector2(-4, -2), 4, 3, green);
            renderer.DrawRect(camera,
                pos + new Vector2(-4, -5), 3, 4, green);
        }
        if (variant != 1)
        {
            renderer.DrawRect(camera,
                pos + new Vector2(4, 0), 4, 3, green);
            renderer.DrawRect(camera,
                pos + new Vector2(4, -3), 3, 4, green);
        }
    }

    private static void RenderBoulder(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int variant)
    {
        int size = 6 + variant * 2;
        renderer.DrawRect(camera, pos, size, (int)(size * 0.7f),
            new Color3(100, 90, 80));
        renderer.DrawRect(camera, pos + new Vector2(-1, -1),
            size - 2, (int)(size * 0.4f),
            new Color3(120, 110, 95));
    }

    private static void RenderMesa(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int variant)
    {
        int bw = 10 + variant * 2;
        renderer.DrawRect(camera, pos, bw, 4,
            new Color3(150, 120, 80));
        renderer.DrawRect(camera, pos + new Vector2(0, -3),
            bw - 2, 3, new Color3(140, 110, 75));
    }

    private static void RenderIceCrystal(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, float time, int variant)
    {
        float sparkle = (MathF.Sin(time * 4f + variant * 100)
            + 1f) * 0.5f;
        byte brightness = (byte)(200 + sparkle * 55);
        int crystalH = 8 + variant * 3;
        renderer.DrawRect(camera, pos, 3, crystalH,
            new Color3(brightness, brightness, 255));
        renderer.DrawRect(camera, pos + new Vector2(0, -2),
            crystalH - 4, 2,
            new Color3((byte)(brightness - 20),
                (byte)(brightness - 10), 250));
    }

    private static void RenderFrozenBoulder(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int variant)
    {
        int size = 7 + variant * 2;
        renderer.DrawRect(camera, pos, size, (int)(size * 0.7f),
            new Color3(130, 130, 140));
        renderer.DrawRect(camera, pos + new Vector2(0, -2),
            size - 2, 3, new Color3(190, 210, 240));
    }

    private static void RenderVolcanicVent(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, float time, int variant)
    {
        renderer.DrawRect(camera, pos, 6, 4,
            new Color3(40, 30, 25));
        renderer.DrawRect(camera, pos, 4, 3,
            new Color3(160, 60, 20));
        float smokeY = MathF.Sin(time * 1.5f + variant) * 3f - 8f;
        float smokeX = MathF.Cos(time * 0.8f + variant) * 2f;
        renderer.DrawRect(camera,
            pos + new Vector2(smokeX, smokeY),
            4, 4, new Color4(100, 90, 80, 40));
        renderer.DrawRect(camera,
            pos + new Vector2(smokeX * 0.5f, smokeY - 5),
            3, 3, new Color4(80, 75, 70, 25));
    }

    private static void RenderScrapPile(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int variant)
    {
        renderer.DrawRect(camera, pos, 8, 5,
            new Color3(130, 125, 120));
        renderer.DrawRect(camera, pos + new Vector2(-2, -2),
            4, 3, new Color3(150, 140, 130));
        renderer.DrawRect(camera, pos + new Vector2(3, 1),
            3, 3, new Color3(110, 105, 100));
    }

    private static void RenderShell(ISpriteRenderer renderer,
        Camera camera, Vector2 pos, int variant)
    {
        var shellColor = variant switch
        {
            0 => new Color3(220, 200, 170),
            1 => new Color3(200, 180, 190),
            2 => new Color3(190, 210, 200),
            _ => new Color3(210, 190, 160)
        };
        renderer.DrawRect(camera, pos, 4, 3, shellColor);
        renderer.DrawRect(camera, pos + new Vector2(0, -1),
            3, 2, new Color3((byte)(shellColor.R - 20),
                (byte)(shellColor.G - 20),
                (byte)(shellColor.B - 10)));
    }

    #endregion
}
