using SDL3;
using System.Numerics;
using Engine.Core;

namespace Engine.Platform.Sdl;

/// <summary>
/// Shared utility for rendering tile-based maps.
/// Used internally by <see cref="SdlSpriteRenderer"/>.
/// </summary>
public class SdlTileMapRenderer
{
    // Reusable buffers for batched tile rendering (avoids per-frame allocs).
    private SDL.Vertex[] _vertexBuf = new SDL.Vertex[1024];
    private int[] _indexBuf = new int[1536];
    private nint _renderer;

    public SdlTileMapRenderer(nint renderer)
    {
        _renderer = renderer;
    }

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
    /// <param name="renderDetail">Optional per-tile detail callback: (x, y, worldPos, hash).</param>
    public void RenderTiles(
        ISpriteRenderer renderer, Camera camera,
        int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor,
        Action<int, int, Vector2, int>? renderDetail = null)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startX = Math.Max(0, (int)(topLeft.X / tileSize) - 1);
        int startY = Math.Max(0, (int)(topLeft.Y / tileSize) - 1);
        int endX = Math.Min(mapWidth - 1, (int)(bottomRight.X / tileSize) + 1);
        int endY = Math.Min(mapHeight - 1, (int)(bottomRight.Y / tileSize) + 1);

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

                var worldPos = new Vector2(
                    x * tileSize + halfTile,
                    y * tileSize + halfTile);
                var screenPos = camera.WorldToScreen(worldPos);

                // Floor both edges independently so adjacent tiles share the same
                // boundary pixel and leave no sub-pixel gaps (black lines).
                float left = MathF.Floor(screenPos.X - halfScaled);
                float top = MathF.Floor(screenPos.Y - halfScaled);
                float right = MathF.Floor(screenPos.X + halfScaled);
                float bottom = MathF.Floor(screenPos.Y + halfScaled);

                var c = color.Value;
                var fcolor = new SDL.FColor
                {
                    R = c.R / 255f,
                    G = c.G / 255f,
                    B = c.B / 255f,
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
            SDL.RenderGeometry(_renderer, nint.Zero, _vertexBuf, vi, _indexBuf, ii);

        // ── Pass 2: render per-tile details ──
        if (renderDetail != null)
        {
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    var color = getColor(x, y);
                    if (color == null) continue;

                    int hash = BaseSpriteRenderer.GetTileHash(x, y);
                    var worldPos = new Vector2(
                        x * tileSize + halfTile,
                        y * tileSize + halfTile);

                    renderDetail(x, y, worldPos, hash);
                }
            }
        }
    }
}
