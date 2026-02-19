using SDL3;
using System.Numerics;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Rendering.Base;

/// <summary>
/// Shared utility for rendering tile-based maps with per-tile color variation.
/// Used by PlanetSurfaceState and InteriorState.
/// </summary>
public static class TileMapRenderer
{
    // Reusable buffers for batched tile rendering (avoids per-frame allocs).
    private static SDL.Vertex[] _vertexBuf = new SDL.Vertex[1024];
    private static int[] _indexBuf = new int[1536];

    /// <summary>
    /// Renders visible tiles with deterministic per-tile brightness variation.
    /// Background tiles are drawn in a single batched SDL.RenderGeometry call,
    /// then detail callbacks are invoked in a second pass.
    /// </summary>
    /// <param name="renderer">Sprite renderer.</param>
    /// <param name="camera">Current camera.</param>
    /// <param name="mapWidth">Width of the tile map.</param>
    /// <param name="mapHeight">Height of the tile map.</param>
    /// <param name="getColor">Returns (R, G, B) for the tile at (x, y), or null to skip.</param>
    /// <param name="variationDivisor">Controls brightness variation strength (higher = subtler).</param>
    /// <param name="renderDetail">Optional per-tile detail callback: (x, y, worldPos, hash).</param>
    public static void RenderTiles(
        SpriteRenderer renderer, Camera camera,
        int mapWidth, int mapHeight,
        Func<int, int, Color3?> getColor,
        float variationDivisor = 800f,
        Action<int, int, Vector2, int>? renderDetail = null)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startX = Math.Max(0, (int)(topLeft.X / GameConfig.TileSize) - 1);
        int startY = Math.Max(0, (int)(topLeft.Y / GameConfig.TileSize) - 1);
        int endX = Math.Min(mapWidth - 1, (int)(bottomRight.X / GameConfig.TileSize) + 1);
        int endY = Math.Min(mapHeight - 1, (int)(bottomRight.Y / GameConfig.TileSize) + 1);

        float tileSize = GameConfig.TileSize;
        float halfTile = tileSize / 2f;
        float scaledSize = tileSize * camera.Zoom;
        float halfScaled = scaledSize / 2f;

        // ── Pass 1: batch all background tiles into a single draw call ──
        int maxTiles = (endX - startX + 1) * (endY - startY + 1);
        int requiredVerts = maxTiles * 4;
        int requiredIndices = maxTiles * 6;
        if (_vertexBuf.Length < requiredVerts)
            _vertexBuf = new SDL.Vertex[requiredVerts];
        if (_indexBuf.Length < requiredIndices)
            _indexBuf = new int[requiredIndices];

        int vi = 0; // vertex write index
        int ii = 0; // index write index

        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                var color = getColor(x, y);
                if (color == null) continue;

                var variationColor = GetColorVariation(color.Value, x, y, variationDivisor);

                var worldPos = new Vector2(
                    x * tileSize + halfTile,
                    y * tileSize + halfTile);
                var screenPos = camera.WorldToScreen(worldPos);

                float left = screenPos.X - halfScaled;
                float top = screenPos.Y - halfScaled;
                float right = left + scaledSize;
                float bottom = top + scaledSize;

                var fcolor = new SDL.FColor
                {
                    R = variationColor.R / 255f,
                    G = variationColor.G / 255f,
                    B = variationColor.B / 255f,
                    A = 1f
                };

                int baseVertex = vi;

                // Top-left
                _vertexBuf[vi++] = new SDL.Vertex
                {
                    Position = new SDL.FPoint { X = left, Y = top },
                    Color = fcolor
                };
                // Top-right
                _vertexBuf[vi++] = new SDL.Vertex
                {
                    Position = new SDL.FPoint { X = right, Y = top },
                    Color = fcolor
                };
                // Bottom-right
                _vertexBuf[vi++] = new SDL.Vertex
                {
                    Position = new SDL.FPoint { X = right, Y = bottom },
                    Color = fcolor
                };
                // Bottom-left
                _vertexBuf[vi++] = new SDL.Vertex
                {
                    Position = new SDL.FPoint { X = left, Y = bottom },
                    Color = fcolor
                };

                // Two triangles: 0-1-2, 0-2-3
                _indexBuf[ii++] = baseVertex;
                _indexBuf[ii++] = baseVertex + 1;
                _indexBuf[ii++] = baseVertex + 2;
                _indexBuf[ii++] = baseVertex;
                _indexBuf[ii++] = baseVertex + 2;
                _indexBuf[ii++] = baseVertex + 3;
            }
        }

        if (vi > 0)
            renderer.DrawGeometryScreen(_vertexBuf, vi, _indexBuf, ii);

        // ── Pass 2: render per-tile details ──
        if (renderDetail != null)
        {
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    var color = getColor(x, y);
                    if (color == null) continue;

                    int hash = GetTileHash(x, y);
                    var worldPos = new Vector2(
                        x * tileSize + halfTile,
                        y * tileSize + halfTile);

                    renderDetail(x, y, worldPos, hash);
                }
            }
        }
    }

    public static int GetTileHash(int x, int y)
    {
        return (x * 374761393 + y * 668265263) ^ (x * y);
    }

    public static Color3 GetColorVariation(Color3 baseColor, int x, int y, float variationDivisor)
    {
        int hash = GetTileHash(x,y);
        float variation = ((hash & 0xFF) - 128) / variationDivisor;
        byte vr = (byte)Math.Clamp(baseColor.R + baseColor.R * variation, 0, 255);
        byte vg = (byte)Math.Clamp(baseColor.G + baseColor.G * variation, 0, 255);
        byte vb = (byte)Math.Clamp(baseColor.B + baseColor.B * variation, 0, 255);
        return new Color3(vr, vg, vb);
    }
}
