using System.Numerics;
using Engine.Core;
using Engine.Rendering.Base;

namespace Engine.Platform;

/// <summary>
/// Abstract sprite renderer that serializes all draw calls into a
/// <see cref="RenderCommandBuffer"/> shared with a paired <see cref="BufferedFontRenderer"/>.
/// Commands are batched in submission order and dispatched to the corresponding
/// <c>*Immediate</c> methods when <see cref="FlushBuffer"/> is called.
/// </summary>
/// <remarks>
/// <para>
/// The buffer is owned by the <see cref="BufferedFontRenderer"/> supplied to the
/// constructor; both renderers write to the same stream, preserving draw-call ordering
/// across text and sprite commands.
/// </para>
/// <para>
/// <see cref="EndFrame"/> automatically flushes the buffer after writing its command.
/// <see cref="Update"/> is always immediate (it never buffers) so that
/// <see cref="BaseSpriteRenderer.WindowWidth"/> / <see cref="BaseSpriteRenderer.WindowHeight"/>
/// are kept current throughout the frame.
/// </para>
/// <para>
/// <see cref="RenderTiles"/> and <see cref="TakeScreenshot"/> trigger an implicit
/// <see cref="FlushBuffer"/> before executing because their delegate callbacks and
/// return values cannot be serialized.
/// </para>
/// <para>
/// Subclasses implement one <c>*Immediate</c> abstract method per draw operation;
/// those methods perform the real platform draw without any further buffering.
/// </para>
/// </remarks>
public abstract class BufferedSpriteRenderer : BaseSpriteRenderer
{
    /// <summary>The command buffer shared with the paired <see cref="BufferedFontRenderer"/>.</summary>
    protected readonly RenderCommandBuffer Buffer = new RenderCommandBuffer();

    // Reused across frames to collect tile colours before writing the batch command.
    private Color4[] _tileColorBuf = [];

    protected BufferedSpriteRenderer(ITextureManager textures)
        : base(new BufferedFontRenderer(textures), textures)
    {
        ((BufferedFontRenderer)_fontRenderer).Buffer = Buffer; // share the buffer with the font renderer
        Buffer.SetFlushCallback(FlushBuffer); // Wire up the auto-flush callback now that FlushBuffer is available.
    }

    /// <summary>
    /// Constructor overload that accepts an externally-created <see cref="BufferedFontRenderer"/>
    /// subclass (e.g. a platform-specific font renderer) instead of creating a plain
    /// <see cref="BufferedFontRenderer"/> internally.
    /// The font renderer's <see cref="BufferedFontRenderer.Buffer"/> is wired to the shared
    /// command buffer automatically.
    /// </summary>
    protected BufferedSpriteRenderer(BufferedFontRenderer fontRenderer, ITextureManager textures)
        : base(fontRenderer, textures)
    {
        fontRenderer.Buffer = Buffer;
        Buffer.SetFlushCallback(FlushBuffer);
    }

    // ── Buffer flush ──────────────────────────────────────────────────

    /// <summary>
    /// Reads every command currently stored in <see cref="Buffer"/> and dispatches
    /// it to the corresponding <c>*Immediate</c> abstract method, then resets the buffer.
    /// </summary>
    /// <remarks>
    /// Override to add pre/post-flush logic; always call <c>base.FlushBuffer()</c>.
    /// </remarks>
    public void FlushBuffer()
    {
        if (Buffer.IsEmpty) return;

        OnFlushBuffer();

        Buffer.Reset();
    }

    protected abstract void OnFlushBuffer();

    // ── BaseSpriteRenderer overrides ──────────────────────────────────

    public override void BeginFrame() => Buffer.WriteBeginFrame();

    /// <inheritdoc/>
    /// <remarks>Writes the EndFrame command then automatically flushes the buffer.</remarks>
    public override void EndFrame()
    {
        Buffer.WriteEndFrame();
        FlushBuffer();
    }

    public override void SetTitle(string title) => Buffer.WriteSetTitle(title);

    /// <inheritdoc/>
    /// <remarks>Flushes the buffer before capturing so the frame is fully rendered.</remarks>
    public override string? TakeScreenshot()
    {
        FlushBuffer();
        return null; // default impl returns null; override to return actual screenshot data
    }

    public override void SetClipRect(float x, float y, float w, float h)
        => Buffer.WriteSetClipRect(x, y, w, h);

    public override void ClearClipRect()
        => Buffer.WriteClearClipRect();

    public override void DrawRectScreen(float x, float y, float w, float h, Color4 color)
        => Buffer.WriteDrawRectScreen(x, y, w, h, color);

    public override void DrawCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32)
        => Buffer.WriteDrawCircleScreen(cx, cy, radius, color, segments);

    public override void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32)
        => Buffer.WriteDrawFilledCircleScreen(cx, cy, radius, color, segments);

    public override void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32)
        => Buffer.WriteDrawFilledCircleScreenGradient(cx, cy, radius, innerColor, outerColor, transitionStartRadius, segments);

    public override void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius, Color4 color, int segments = 48)
        => Buffer.WriteDrawSolidRingScreen(cx, cy, innerRadius, outerRadius, color, segments);

    public override void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color)
        => Buffer.WriteDrawLineScreen(x1, y1, x2, y2, color);

    public override void DrawTextureScreen(nint texture, float x, float y, float w, float h, float rotationDeg = 0f, byte alpha = 255)
        => Buffer.WriteDrawTextureScreen(texture, x, y, w, h, rotationDeg, alpha);

    public override void DrawTextureScreen(nint texture, Rect dst, byte alpha = 255)
        => Buffer.WriteDrawTextureScreenRect(texture, dst, alpha);

    public override void DrawTextureScreen(nint texture, Rect src, Rect dst, byte alpha = 255)
        => Buffer.WriteDrawTextureScreenSrcDst(texture, src, dst, alpha);

    public override void DrawTextureScreen(nint texture, float x, float y, float w, float h, Color4 color, float rotationDeg = 0f)
        => Buffer.WriteDrawTextureScreenColor(texture, x, y, w, h, color, rotationDeg);

    public override void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
        => Buffer.WriteDrawTriangleScreen(x1, y1, x2, y2, x3, y3, color);

    public override void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
        => Buffer.WriteDrawFilledTriangleScreen(x1, y1, x2, y2, x3, y3, color);

    /// <inheritdoc/>
    /// <remarks>
    /// Delegate callbacks cannot be serialized; flushes the buffer first so ordering is
    /// preserved, then calls <see cref="RenderTilesImmediate"/> directly.
    /// </remarks>
    public override void RenderTiles(Camera camera, int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor, Action<int, int, Vector2, int>? renderDetail = null)
    {
        // Pass 1: collect all visible tile colours into the shared flat array, then write
        //         a single DrawTileMapScreen command instead of N individual DrawRectScreen calls.
        // Pass 2: invoke renderDetail callbacks through this renderer so they are appended
        //         to the same buffer after the tile batch, preserving draw ordering.

        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startX = Math.Max(0, (int)(topLeft.X / tileSize) - 1);
        int startY = Math.Max(0, (int)(topLeft.Y / tileSize) - 1);
        int endX = Math.Min(mapWidth - 1, (int)(bottomRight.X / tileSize) + 1);
        int endY = Math.Min(mapHeight - 1, (int)(bottomRight.Y / tileSize) + 1);

        int tilesW = endX - startX + 1;
        int tilesH = endY - startY + 1;
        int colorCount = tilesW * tilesH;

        if (_tileColorBuf.Length < colorCount)
            _tileColorBuf = new Color4[colorCount];

        float halfTile = tileSize / 2f;

        for (int tx = 0; tx < tilesW; tx++)
        {
            int x = startX + tx;
            for (int ty = 0; ty < tilesH; ty++)
            {
                int y = startY + ty;
                var color = getColor(x, y);
                // Null → Color4(0,0,0,0): A=0 signals an empty tile during replay.
                _tileColorBuf[tx * tilesH + ty] = color.HasValue ? (Color4)color.Value : default;
            }
        }

        float scaledTileSize = tileSize * camera.Zoom;
        var tilesTopLeft = camera.WorldToScreen(new Vector2(startX * tileSize, startY * tileSize));
        Buffer.WriteDrawTileMapScreen(tilesTopLeft.X, tilesTopLeft.Y, scaledTileSize, tilesW, tilesH, _tileColorBuf, colorCount);

        if (renderDetail != null)
        {
            for (int tx = 0; tx < tilesW; tx++)
            {
                int x = startX + tx;
                for (int ty = 0; ty < tilesH; ty++)
                {
                    if (_tileColorBuf[tx * tilesH + ty].A == 0) continue;
                    int y = startY + ty;
                    int hash = (x * 374761393 + y * 668265263) ^ (x * y);
                    var worldPos = new Vector2(x * tileSize + halfTile, y * tileSize + halfTile);
                    renderDetail(x, y, worldPos, hash);
                }
            }
        }
    }
}
