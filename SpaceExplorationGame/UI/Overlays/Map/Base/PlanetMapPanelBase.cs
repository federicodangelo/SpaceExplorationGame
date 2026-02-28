using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.UI.Overlays.Map.Base;

/// <summary>
/// Shared base class for planet map panels (landing-site selection and surface navigation).
/// Encapsulates terrain texture creation/rendering, settlement marker rendering,
/// camera clamping, and common lifecycle stubs so subclasses only implement
/// panel-specific input, selection, and info-panel logic.
/// </summary>
public abstract class PlanetMapPanelBase : MapPanelBase
{
    protected TextureManager _textures = null!;
    protected StarSystemData _starSystem = null!;
    protected PlanetData _planet = null!;
    protected PlanetSurfaceData _surfaceData = null!;
    protected nint _terrainTexture { get; private set; }

    /// <summary>Shared animation timer for the selection reticle (incremented at 3× dt).</summary>
    protected float _selectionPulse;

    /// <summary>Name of the planet/moon being viewed.</summary>
    public string PlanetName => _planet?.Name ?? "UNKNOWN";

    public PlanetMapPanelBase(TextureManager textures)
    {
        _textures = textures;
    }

    /// <summary>
    /// Validates that a tile is selectable: it must be inside the safe inner bounds,
    /// and there must be no Void tiles in a square neighborhood with the provided radius.
    /// </summary>
    protected bool IsTileSelectableWithMargin(int tileX, int tileY, int marginTiles, out string? failureReason)
    {
        failureReason = null;

        if (_surfaceData == null)
        {
            failureReason = "INVALID TARGET";
            return false;
        }

        if (tileX < 0 || tileY < 0
            || tileX >= _surfaceData.Width
            || tileY >= _surfaceData.Height)
        {
            failureReason = "OUTSIDE WORLD";
            return false;
        }

        if (SurfaceTerrainRules.IsVoid(_surfaceData.Tiles[tileX, tileY]))
        {
            failureReason = "OUTSIDE WORLD";
            return false;
        }

        if (tileX < marginTiles || tileY < marginTiles
            || tileX >= _surfaceData.Width - marginTiles
            || tileY >= _surfaceData.Height - marginTiles)
        {
            failureReason = "TOO CLOSE TO BORDER";
            return false;
        }

        for (int x = tileX - marginTiles; x <= tileX + marginTiles; x++)
        {
            for (int y = tileY - marginTiles; y <= tileY + marginTiles; y++)
            {
                if (SurfaceTerrainRules.IsVoid(_surfaceData.Tiles[x, y]))
                {
                    failureReason = "TOO CLOSE TO BORDER";
                    return false;
                }
            }
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Not used directly; call the specific Open method on each subclass.</summary>
    public override void Open(Game game) { }

    public override void Close(Game game) { }

    /// <summary>Destroy the cached terrain texture.</summary>
    public void Cleanup()
    {
        _textures.DestroyTexture(_terrainTexture);
        _terrainTexture = nint.Zero;
    }

    // ─────────────────────────────────────────────────────────────
    //  CAMERA
    // ─────────────────────────────────────────────────────────────

    /// <summary>Initial camera center position in tile coordinates.
    /// Default is the centre of the terrain; override to start elsewhere.</summary>
    protected virtual Vector2 GetInitialCameraPosition() =>
        new(_surfaceData.Width / 2f, _surfaceData.Height / 2f);

    public override void SetupCamera(Game game)
    {
        if (_surfaceData == null) return;

        float worldW = _surfaceData.Width;
        float worldH = _surfaceData.Height;

        float fitZoom = Math.Min(MapW / worldW, MapH / worldH);
        Camera.Zoom = fitZoom;
        Camera.ZoomMin = fitZoom;
        Camera.ZoomMax = fitZoom * 8f;
        Camera.Position = GetInitialCameraPosition();
        Camera.ClampZoom();
        ClampCameraPosition();
    }

    /// <summary>Camera movement + clamping. Override in subclasses that need
    /// different key bindings (e.g. WASD-only with arrows for cursor).</summary>
    public override void Update(Game game)
    {
        _selectionPulse += game.DeltaTime * 3f;
        base.Update(game);
        ClampCameraPosition();
    }

    /// <summary>Keep the camera within the terrain bounds so no empty space is visible.</summary>
    protected void ClampCameraPosition()
    {
        if (_surfaceData == null) return;

        float worldW = _surfaceData.Width;
        float worldH = _surfaceData.Height;

        float halfViewW = MapW / (2f * Camera.Zoom);
        float halfViewH = MapH / (2f * Camera.Zoom);

        float minX = halfViewW;
        float maxX = worldW - halfViewW;
        float minY = halfViewH;
        float maxY = worldH - halfViewH;

        float cx = (minX <= maxX) ? Math.Clamp(Camera.Position.X, minX, maxX) : worldW / 2f;
        float cy = (minY <= maxY) ? Math.Clamp(Camera.Position.Y, minY, maxY) : worldH / 2f;

        Camera.Position = new Vector2(cx, cy);
    }

    // ─────────────────────────────────────────────────────────────
    //  TERRAIN TEXTURE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Create a 1-pixel-per-tile terrain overview texture.</summary>
    protected void CreateTerrainTexture(Game game)
    {
        _textures = game.Textures;
        int w = _surfaceData.Width;
        int h = _surfaceData.Height;
        var pixels = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var terrain = _surfaceData.Tiles[x, y];
                var color = PlanetSurfaceGenerator.GetTerrainColor(terrain);

                var variationColor = TileMapRenderer.GetColorVariation(color, x, y, 800f);

                int idx = (y * w + x) * 4;
                pixels[idx + 0] = variationColor.R;
                pixels[idx + 1] = variationColor.G;
                pixels[idx + 2] = variationColor.B;
                pixels[idx + 3] = 255;
            }
        }

        // Mark settlement tiles
        foreach (var s in _surfaceData.Settlements)
        {
            for (int sx = s.TileRect.X; sx < s.TileRect.X + s.TileRect.Width && sx < w; sx++)
            {
                for (int sy = s.TileRect.Y; sy < s.TileRect.Y + s.TileRect.Height && sy < h; sy++)
                {
                    int idx = (sy * w + sx) * 4;
                    pixels[idx + 0] = 100;
                    pixels[idx + 1] = 100;
                    pixels[idx + 2] = 120;
                    pixels[idx + 3] = 255;
                }
            }
        }

        _terrainTexture = game.Textures.CreateTextureFromPixels(pixels, w, h, TextureScaleMode.Nearest);
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED RENDERING HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Blit the terrain texture to the map area. Returns tile-screen scale factors
    /// needed for subsequent marker positioning.</summary>
    protected (float tileScreenW, float tileScreenH) RenderTerrainTexture(Game game)
    {
        float worldW = _surfaceData.Width;
        float worldH = _surfaceData.Height;

        var topLeft = Camera.WorldToScreen(Vector2.Zero);
        var bottomRight = Camera.WorldToScreen(new Vector2(worldW, worldH));
        var dstRect = new SDL.FRect
        {
            X = topLeft.X,
            Y = topLeft.Y,
            W = bottomRight.X - topLeft.X,
            H = bottomRight.Y - topLeft.Y
        };
        game.SpriteRenderer.DrawTextureScreen(_terrainTexture, in dstRect);

        float tileScreenW = (bottomRight.X - topLeft.X) / worldW;
        float tileScreenH = (bottomRight.Y - topLeft.Y) / worldH;
        return (tileScreenW, tileScreenH);
    }

    /// <summary>Render a single settlement: outline rectangle + label above it.</summary>
    protected static void RenderSettlementMarker(SpriteRenderer renderer, Camera camera,
        SettlementData settlement, float tileScreenW, float tileScreenH, Color4 outlineColor)
    {
        float cx = settlement.TileRect.X + settlement.TileRect.Width / 2f;
        float cy = settlement.TileRect.Y + settlement.TileRect.Height / 2f;
        var settScreen = camera.WorldToScreen(new Vector2(cx, cy));

        float sw = settlement.TileRect.Width * tileScreenW;
        float sh = settlement.TileRect.Height * tileScreenH;

        // Outline
        renderer.DrawRectScreen(settScreen.X - sw / 2f - 1, settScreen.Y - sh / 2f - 1,
            sw + 2, sh + 2, outlineColor);

        // Label
        float labelScale = 1f;
        float textW = renderer.MeasureText(settlement.Name, labelScale);
        renderer.DrawRectScreen(settScreen.X - textW / 2f - 2, settScreen.Y - sh / 2f - 14 * labelScale,
            textW + 4, 12 * labelScale, new Color4(0, 0, 0, 160));
        renderer.DrawTextScreen(settScreen.X - textW / 2f, settScreen.Y - sh / 2f - 13 * labelScale,
            settlement.Name, new Color3(255, 220, 100), labelScale);
    }

    /// <summary>Render an animated selection reticle (pulsing cross + corner brackets) at a screen position.</summary>
    protected static void RenderSelectionReticle(SpriteRenderer renderer, Vector2 screenPos,
        float pulse, Color3 crossColor, Color3 bracketColor)
    {
        float pulseSize = 6f + MathF.Sin(pulse) * 2f;
        float outerSize = pulseSize + 4f;
        byte alpha = (byte)(180 + 40 * MathF.Sin(pulse));

        // Reticle cross
        var crossC = new Color4(crossColor.R, crossColor.G, crossColor.B, alpha);
        renderer.DrawRectScreen(screenPos.X - outerSize, screenPos.Y - 1, outerSize * 2, 2, crossC);
        renderer.DrawRectScreen(screenPos.X - 1, screenPos.Y - outerSize, 2, outerSize * 2, crossC);

        // Corner brackets
        float bLen = 5f;
        renderer.DrawRectScreen(screenPos.X - outerSize, screenPos.Y - outerSize, bLen, 2, bracketColor);
        renderer.DrawRectScreen(screenPos.X - outerSize, screenPos.Y - outerSize, 2, bLen, bracketColor);
        renderer.DrawRectScreen(screenPos.X + outerSize - bLen, screenPos.Y - outerSize, bLen, 2, bracketColor);
        renderer.DrawRectScreen(screenPos.X + outerSize, screenPos.Y - outerSize, 2, bLen, bracketColor);
        renderer.DrawRectScreen(screenPos.X - outerSize, screenPos.Y + outerSize, bLen, 2, bracketColor);
        renderer.DrawRectScreen(screenPos.X - outerSize, screenPos.Y + outerSize - bLen, 2, bLen, bracketColor);
        renderer.DrawRectScreen(screenPos.X + outerSize - bLen, screenPos.Y + outerSize, bLen, 2, bracketColor);
        renderer.DrawRectScreen(screenPos.X + outerSize, screenPos.Y + outerSize - bLen, 2, bLen, bracketColor);
    }

    /// <summary>Render basic planet info lines (name, type, size, settlements).
    /// Returns the Y position after the last line for subclasses to continue rendering.</summary>
    protected float RenderPlanetInfoBlock(SpriteRenderer renderer, float px, float py)
    {
        renderer.DrawTextScreen(px, py, "NAME", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(px, py + 16, _planet.Name.ToUpper(), new Color3(200, 220, 255), 1.8f);

        renderer.DrawRectScreen(px, py + 42, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        renderer.DrawTextScreen(px, py + 52, "TYPE", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(px, py + 68, _planet.Type.ToString().ToUpper(), new Color3(200, 200, 200), 1.8f);

        renderer.DrawRectScreen(px, py + 94, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        renderer.DrawTextScreen(px, py + 104, "SIZE", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(px, py + 120, $"{_surfaceData.Width} x {_surfaceData.Height} TILES",
            new Color3(200, 200, 200), 1.8f);

        renderer.DrawRectScreen(px, py + 146, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        int settlementCount = _surfaceData.Settlements.Count;
        renderer.DrawTextScreen(px, py + 156, "SETTLEMENTS", new Color3(100, 120, 160), 1.3f);
        string settText = settlementCount > 0 ? settlementCount.ToString() : "NONE";
        byte settR = settlementCount > 0 ? (byte)255 : (byte)120;
        byte settG = settlementCount > 0 ? (byte)220 : (byte)120;
        byte settB = settlementCount > 0 ? (byte)100 : (byte)120;
        renderer.DrawTextScreen(px, py + 172, settText, new Color3(settR, settG, settB), 1.8f);

        return py + 198;
    }

    /// <summary>Render the settlement name list. Returns the Y position after.</summary>
    protected float RenderSettlementList(SpriteRenderer renderer, float px, float nextY)
    {
        if (_surfaceData.Settlements.Count == 0) return nextY;

        renderer.DrawRectScreen(px, nextY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        float settListY = nextY + 10;
        foreach (var s in _surfaceData.Settlements)
        {
            renderer.DrawTextScreen(px + 4, settListY, $"> {s.Name}", new Color3(255, 220, 100), 1.3f);
            settListY += 16;
        }
        return settListY + 6;
    }
}
