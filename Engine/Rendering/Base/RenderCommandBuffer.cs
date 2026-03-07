using System.Numerics;
using Engine.Core;

namespace Engine.Rendering.Base;

/// <summary>
/// A single pre-computed textured quad, mapping a rectangular region of a texture
/// to a rectangular area on screen. Used by <see cref="BufferedFontRenderer"/> for
/// glyph batches but applicable to any batched textured-rect draw.
/// </summary>
public struct TexturedQuad
{
    /// <summary>Source coordinates in the source texture (absolute UV, not width/height).</summary>
    public float U0, V0, U1, V1;
    /// <summary>Destination screen coordinates in pixels (absolute, not width/height).</summary>
    public float DstX0, DstY0, DstX1, DstY1;
}


/// <summary>
/// Identifies each drawing or lifecycle command stored in a <see cref="RenderCommandBuffer"/>.
/// Fixed-size commands carry only primitive types (floats, ints, bytes, longs); commands
/// marked "variable" contain one or more length-prefixed fields (e.g. strings).
/// </summary>
public enum RenderCommandType : int
{
    // ── Frame lifecycle ──────────────────────────────────────────────
    Update = 0,   // fixed:    no payload
    BeginFrame = 1,   // fixed:    no payload
    EndFrame = 2,   // fixed:    no payload
    SetTitle = 3,   // variable: string

    // ── Clip ──────────────────────────────────────────────────────────
    SetClipRect = 10,  // fixed:    4 × float (x, y, w, h)
    ClearClipRect = 11,  // fixed:    no payload

    // ── Shapes ────────────────────────────────────────────────────────
    /// <summary>float x, y, w, h; Color4</summary>
    DrawRectScreen = 20,
    /// <summary>float cx, cy, radius; Color4; int segments</summary>
    DrawCircleScreen = 21,
    /// <summary>float cx, cy, radius; Color4; int segments</summary>
    DrawFilledCircleScreen = 22,
    /// <summary>float cx, cy, radius; Color4 inner; Color4 outer; float transitionRadius; int segments</summary>
    DrawFilledCircleScreenGradient = 23,
    /// <summary>float cx, cy, innerRadius, outerRadius; Color4; int segments</summary>
    DrawSolidRingScreen = 24,
    /// <summary>float x1, y1, x2, y2; Color4</summary>
    DrawLineScreen = 25,
    /// <summary>float x1, y1, x2, y2, x3, y3; Color4</summary>
    DrawTriangleScreen = 26,
    /// <summary>float x1, y1, x2, y2, x3, y3; Color4</summary>
    DrawFilledTriangleScreen = 27,

    // ── Textures ──────────────────────────────────────────────────────
    /// <summary>long (nint texture); float x, y, w, h, rotationDeg; byte alpha</summary>
    DrawTextureScreen = 30,
    /// <summary>long (nint texture); Rect dst; byte alpha</summary>
    DrawTextureScreenRect = 31,
    /// <summary>long (nint texture); Rect src; Rect dst; byte alpha</summary>
    DrawTextureScreenSrcDst = 32,
    /// <summary>long (nint texture); float x, y, w, h; Color4; float rotationDeg</summary>
    DrawTextureScreenColor = 33,

    // ── Text / textured quad batches ───────────────────────────────────────
    /// <summary>
    /// long (nint texture); Color4 tint; int quadCount;
    /// quadCount × (float u0, v0, u1, v1, dstX0, dstY0, dstX1, dstY1).
    /// Layout is computed at write time; replay only needs a batched textured draw.
    /// </summary>
    DrawTexturedQuadBatchScreen = 40,

    // ── Tile map ──────────────────────────────────────────────────────
    /// <summary>
    /// float screenX, screenY (center of first tile); float scaledTileSize;
    /// int tilesW, tilesH; int colorCount; Color4[colorCount] (A=0 means empty/skip).
    /// Colors are stored in row-major order: index = tileX * tilesH + tileY.
    /// </summary>
    DrawTileMapScreen = 50,
}



/// <summary>
/// Thread-unsafe binary serialization buffer shared between a
/// <see cref="BufferedSpriteRenderer"/> and its <see cref="BufferedFontRenderer"/>.
/// All draw calls are serialized in submission order into a single reused
/// <see cref="MemoryStream"/>; calling <see cref="CreateReader"/> produces a
/// <see cref="BinaryReader"/> for sequential replay.
/// </summary>
/// <remarks>
/// The internal buffer is pre-allocated to <c>initialCapacity</c> bytes and grows
/// automatically when needed, but never shrinks — reusing the instance across frames
/// eliminates per-frame heap allocations.
/// <para>
/// When an <c>onFlushNeeded</c> callback is supplied, <see cref="EnsureAvailable"/> will
/// call it before the buffer would overflow its initial capacity, giving the owner a
/// chance to replay and reset the buffer (e.g. via <c>BufferedSpriteRenderer.FlushBuffer</c>).
/// </para>
/// </remarks>
public sealed class RenderCommandBuffer : IDisposable
{
    public const int DefaultCapacity = 64 * 1024;

    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;
    private Action? _onFlushNeeded;
    private readonly int _initialCapacity;
    private bool _disposed;

    /// <summary>True when no commands have been written since the last <see cref="Reset"/>.</summary>
    public bool IsEmpty => _stream.Position == 0;

    /// <summary>Number of bytes currently written to the buffer.</summary>
    public long Length => _stream.Position;

    /// <summary>
    /// The capacity threshold used by <see cref="EnsureAvailable"/> to decide when to flush.
    /// Equals the <c>initialCapacity</c> passed to the constructor.
    /// </summary>
    public int Capacity => _initialCapacity;

    /// <summary>
    /// Sets or replaces the flush callback after construction. Useful when the owner
    /// (<see cref="Engine.Platform.BufferedSpriteRenderer"/>) cannot supply the delegate
    /// at buffer-creation time due to construction ordering.
    /// </summary>
    internal void SetFlushCallback(Action callback) => _onFlushNeeded = callback;

    /// <param name="initialCapacity">Pre-allocated buffer size in bytes.</param>
    /// <param name="onFlushNeeded">
    /// Optional callback invoked by <see cref="EnsureAvailable"/> when the requested bytes
    /// would exceed <see cref="Capacity"/>. The callback is responsible for replaying and
    /// resetting the buffer (by calling <see cref="Reset"/> or equivalent).
    /// </param>
    public RenderCommandBuffer(int initialCapacity = DefaultCapacity, Action? onFlushNeeded = null)
    {
        _initialCapacity = initialCapacity;
        _onFlushNeeded = onFlushNeeded;
        _stream = new MemoryStream(initialCapacity);
        _writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
    }

    // ── Buffer management ─────────────────────────────────────────────

    /// <summary>Resets the write cursor to the beginning without freeing memory.</summary>
    public void Reset()
    {
        _writer.Flush();
        _stream.SetLength(0);
    }

    /// <summary>
    /// Ensures at least <paramref name="bytes"/> bytes are available before the buffer
    /// reaches its initial capacity. If the available space is insufficient and an
    /// <c>onFlushNeeded</c> callback was provided, it is invoked so the owner can replay
    /// and reset the buffer. After the callback the buffer is guaranteed to have a reset
    /// position of 0; if the capacity is still insufficient the buffer will grow naturally.
    /// </summary>
    /// <remarks>
    /// Call this at the start of any write sequence whose total size is known in advance.
    /// </remarks>
    public void EnsureAvailable(int bytes)
    {
        if (_onFlushNeeded != null && _stream.Position + bytes > _initialCapacity)
            _onFlushNeeded();
    }

    /// <summary>
    /// Creates a <see cref="BinaryReader"/> positioned at the start of the current buffer
    /// content. Dispose the reader when done. Do not write to the buffer while the reader
    /// is alive.
    /// </summary>
    public BinaryReader CreateReader()
    {
        _writer.Flush();
        return new BinaryReader(
            new MemoryStream(_stream.GetBuffer(), 0, (int)_stream.Position, writable: false),
            System.Text.Encoding.UTF8,
            leaveOpen: false);
    }

    /// <summary>
    /// Returns the underlying byte array and the number of valid bytes written.
    /// The array may be larger than <paramref name="length"/>; only bytes [0, length)
    /// contain valid command data. The array is owned by this buffer — do not retain it
    /// across frames or modify it.
    /// </summary>
    public void GetRawBuffer(out byte[] buffer, out int length)
    {
        _writer.Flush();
        buffer = _stream.GetBuffer();
        length = (int)_stream.Position;
    }

    // ── Write helpers ─────────────────────────────────────────────────

    private void Cmd(RenderCommandType cmd) => _writer.Write((int)cmd);

    private void Write(Color4 c) { _writer.Write(c.R); _writer.Write(c.G); _writer.Write(c.B); _writer.Write(c.A); }
    private void Write(Rect r) { _writer.Write(r.X); _writer.Write(r.Y); _writer.Write(r.W); _writer.Write(r.H); }

    // ── Read helpers (static, used by BufferedSpriteRenderer.FlushBuffer) ─

    public static Color4 ReadColor4(BinaryReader r) =>
        new(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());

    public static Rect ReadRect(BinaryReader r) =>
        new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    // ── Frame lifecycle ───────────────────────────────────────────────

    public void WriteUpdate() { EnsureAvailable(4); Cmd(RenderCommandType.Update); }
    public void WriteBeginFrame() { EnsureAvailable(4); Cmd(RenderCommandType.BeginFrame); }
    public void WriteEndFrame() { EnsureAvailable(4); Cmd(RenderCommandType.EndFrame); }

    public void WriteSetTitle(string title)
    // cmd(4) + 7-bit-encoded length(≤5) + UTF-8 content(≤4 bytes/char)
    { EnsureAvailable(9 + title.Length * 4); Cmd(RenderCommandType.SetTitle); _writer.Write(title); }

    // ── Clip ─────────────────────────────────────────────────────────

    // cmd(4) + 4 floats(16) = 20
    public void WriteSetClipRect(float x, float y, float w, float h)
    { EnsureAvailable(20); Cmd(RenderCommandType.SetClipRect); _writer.Write(x); _writer.Write(y); _writer.Write(w); _writer.Write(h); }

    public void WriteClearClipRect() { EnsureAvailable(4); Cmd(RenderCommandType.ClearClipRect); }

    // ── Shapes ───────────────────────────────────────────────────────

    // cmd(4) + x,y,w,h(16) + Color4(4) = 24
    public void WriteDrawRectScreen(float x, float y, float w, float h, Color4 color)
    { EnsureAvailable(24); Cmd(RenderCommandType.DrawRectScreen); _writer.Write(x); _writer.Write(y); _writer.Write(w); _writer.Write(h); Write(color); }

    // cmd(4) + cx,cy,radius(12) + Color4(4) + segments(4) = 24
    public void WriteDrawCircleScreen(float cx, float cy, float radius, Color4 color, int segments)
    { EnsureAvailable(24); Cmd(RenderCommandType.DrawCircleScreen); _writer.Write(cx); _writer.Write(cy); _writer.Write(radius); Write(color); _writer.Write(segments); }

    // cmd(4) + cx,cy,radius(12) + Color4(4) + segments(4) = 24
    public void WriteDrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments)
    { EnsureAvailable(24); Cmd(RenderCommandType.DrawFilledCircleScreen); _writer.Write(cx); _writer.Write(cy); _writer.Write(radius); Write(color); _writer.Write(segments); }

    // cmd(4) + cx,cy,radius(12) + innerColor(4) + outerColor(4) + transitionRadius(4) + segments(4) = 32
    public void WriteDrawFilledCircleScreenGradient(float cx, float cy, float radius, Color4 innerColor, Color4 outerColor, float transitionRadius, int segments)
    { EnsureAvailable(32); Cmd(RenderCommandType.DrawFilledCircleScreenGradient); _writer.Write(cx); _writer.Write(cy); _writer.Write(radius); Write(innerColor); Write(outerColor); _writer.Write(transitionRadius); _writer.Write(segments); }

    // cmd(4) + cx,cy,innerRadius,outerRadius(16) + Color4(4) + segments(4) = 28
    public void WriteDrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius, Color4 color, int segments)
    { EnsureAvailable(28); Cmd(RenderCommandType.DrawSolidRingScreen); _writer.Write(cx); _writer.Write(cy); _writer.Write(innerRadius); _writer.Write(outerRadius); Write(color); _writer.Write(segments); }

    // cmd(4) + x1,y1,x2,y2(16) + Color4(4) = 24
    public void WriteDrawLineScreen(float x1, float y1, float x2, float y2, Color4 color)
    { EnsureAvailable(24); Cmd(RenderCommandType.DrawLineScreen); _writer.Write(x1); _writer.Write(y1); _writer.Write(x2); _writer.Write(y2); Write(color); }

    // cmd(4) + x1,y1,x2,y2,x3,y3(24) + Color4(4) = 32
    public void WriteDrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
    { EnsureAvailable(32); Cmd(RenderCommandType.DrawTriangleScreen); _writer.Write(x1); _writer.Write(y1); _writer.Write(x2); _writer.Write(y2); _writer.Write(x3); _writer.Write(y3); Write(color); }

    // cmd(4) + x1,y1,x2,y2,x3,y3(24) + Color4(4) = 32
    public void WriteDrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
    { EnsureAvailable(32); Cmd(RenderCommandType.DrawFilledTriangleScreen); _writer.Write(x1); _writer.Write(y1); _writer.Write(x2); _writer.Write(y2); _writer.Write(x3); _writer.Write(y3); Write(color); }

    // ── Textures ─────────────────────────────────────────────────────

    // cmd(4) + texture(8) + x,y,w,h(16) + rotationDeg(4) + alpha(1) = 33
    public void WriteDrawTextureScreen(nint texture, float x, float y, float w, float h, float rotationDeg, byte alpha)
    { EnsureAvailable(33); Cmd(RenderCommandType.DrawTextureScreen); _writer.Write((long)texture); _writer.Write(x); _writer.Write(y); _writer.Write(w); _writer.Write(h); _writer.Write(rotationDeg); _writer.Write(alpha); }

    // cmd(4) + texture(8) + Rect dst(16) + alpha(1) = 29
    public void WriteDrawTextureScreenRect(nint texture, Rect dst, byte alpha)
    { EnsureAvailable(29); Cmd(RenderCommandType.DrawTextureScreenRect); _writer.Write((long)texture); Write(dst); _writer.Write(alpha); }

    // cmd(4) + texture(8) + Rect src(16) + Rect dst(16) + alpha(1) = 45
    public void WriteDrawTextureScreenSrcDst(nint texture, Rect src, Rect dst, byte alpha)
    { EnsureAvailable(45); Cmd(RenderCommandType.DrawTextureScreenSrcDst); _writer.Write((long)texture); Write(src); Write(dst); _writer.Write(alpha); }

    // cmd(4) + texture(8) + x,y,w,h(16) + Color4(4) + rotationDeg(4) = 36
    public void WriteDrawTextureScreenColor(nint texture, float x, float y, float w, float h, Color4 color, float rotationDeg)
    { EnsureAvailable(36); Cmd(RenderCommandType.DrawTextureScreenColor); _writer.Write((long)texture); _writer.Write(x); _writer.Write(y); _writer.Write(w); _writer.Write(h); Write(color); _writer.Write(rotationDeg); }

    // ── Text / textured quad batches ─────────────────────────────────

    // cmd(4) + texture(8) + Color4(4) + atlasW(4) + atlasH(4) + count(4) + count×(8 floats×4 = 32) = 28 + count*32
    public void WriteDrawTexturedQuadBatchScreen(nint texture, Color4 color, int atlasWidth, int atlasHeight, TexturedQuad[] quads, int count)
    {
        EnsureAvailable(28 + count * 32);
        Cmd(RenderCommandType.DrawTexturedQuadBatchScreen);
        _writer.Write((long)texture);
        Write(color);
        _writer.Write(atlasWidth);
        _writer.Write(atlasHeight);
        _writer.Write(count);
        for (int i = 0; i < count; i++)
        {
            ref var q = ref quads[i];
            _writer.Write(q.U0); _writer.Write(q.V0); _writer.Write(q.U1); _writer.Write(q.V1);
            _writer.Write(q.DstX0); _writer.Write(q.DstY0); _writer.Write(q.DstX1); _writer.Write(q.DstY1);
        }
    }

    /// <summary>
    /// Reads a <see cref="RenderCommandType.DrawTexturedQuadBatchScreen"/> command.
    /// <paramref name="quadBuf"/> is grown as needed and reused to avoid allocations.
    /// </summary>
    public static void ReadDrawTexturedQuadBatchScreen(
        BinaryReader r,
        out nint texture, out Color4 color, out int atlasWidth, out int atlasHeight, out int count,
        ref TexturedQuad[]? quadBuf)
    {
        texture = (nint)r.ReadInt64();
        color = ReadColor4(r);
        atlasWidth = r.ReadInt32();
        atlasHeight = r.ReadInt32();
        count = r.ReadInt32();
        if (quadBuf == null || quadBuf.Length < count)
            quadBuf = new TexturedQuad[count];
        for (int i = 0; i < count; i++)
        {
            ref var q = ref quadBuf[i];
            q.U0 = r.ReadSingle(); q.V0 = r.ReadSingle(); q.U1 = r.ReadSingle(); q.V1 = r.ReadSingle();
            q.DstX0 = r.ReadSingle(); q.DstY0 = r.ReadSingle(); q.DstX1 = r.ReadSingle(); q.DstY1 = r.ReadSingle();
        }
    }

    /// <summary>
    /// Reads a <see cref="RenderCommandType.DrawTileMapScreen"/> command.
    /// <paramref name="colorBuf"/> is grown as needed and reused to avoid allocations.
    /// </summary>
    public static void ReadDrawTileMapScreen(
        BinaryReader r,
        out float screenX, out float screenY, out float scaledTileSize,
        out int tilesW, out int tilesH, out int colorCount,
        ref Color4[]? colorBuf)
    {
        screenX = r.ReadSingle(); screenY = r.ReadSingle();
        scaledTileSize = r.ReadSingle();
        tilesW = r.ReadInt32(); tilesH = r.ReadInt32();
        colorCount = r.ReadInt32();
        if (colorBuf == null || colorBuf.Length < colorCount)
            colorBuf = new Color4[colorCount];
        for (int i = 0; i < colorCount; i++)
            colorBuf[i] = ReadColor4(r);
    }

    // ── Tile map ─────────────────────────────────────────────────────

    // cmd(4) + screenX,screenY(8) + scaledTileSize(4) + tilesW,tilesH(8) + colorCount(4) + colorCount×4 = 28 + colorCount×4
    public void WriteDrawTileMapScreen(float screenX, float screenY, float scaledTileSize,
        int tilesW, int tilesH, Color4[] colors, int colorCount)
    {
        EnsureAvailable(28 + colorCount * 4);
        Cmd(RenderCommandType.DrawTileMapScreen);
        _writer.Write(screenX); _writer.Write(screenY);
        _writer.Write(scaledTileSize);
        _writer.Write(tilesW); _writer.Write(tilesH);
        _writer.Write(colorCount);
        for (int i = 0; i < colorCount; i++)
            Write(colors[i]);
    }

    // ── IDisposable ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
        _stream.Dispose();
    }
}
