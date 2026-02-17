using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays.Base;

/// <summary>
/// Base class for all overlays. Provides a consistent lifecycle with separate
/// input handling (once per frame) and simulation updates (fixed timestep).
/// </summary>
public abstract class OverlayBase
{
    public bool IsOpen { get; protected set; }

    /// <summary>
    /// Handle input once per frame. Returns true if the overlay consumed input
    /// (blocks underlying state/overlay controls).
    /// </summary>
    public virtual bool UpdateInput(Game game) => IsOpen;

    /// <summary>
    /// Fixed timestep update for simulation (can run multiple times per frame).
    /// </summary>
    public virtual void Update(Game game, float dt) { }

    /// <summary>Render the overlay.</summary>
    public abstract void Render(Game game);

    /// <summary>Close the overlay.</summary>
    public virtual void Close()
    {
        IsOpen = false;
    }

    // ── Shared sci-fi frame rendering ──

    /// <summary>
    /// Draw a sci-fi styled frame: outer border, dark fill, and corner bracket accents.
    /// </summary>
    public static void DrawFrame(SpriteRenderer renderer, float x, float y, float w, float h,
        byte bgAlpha = 230)
    {
        var borderColor = new Color4(60, 80, 140, 220);
        var bgColor = new Color4(10, 12, 30, bgAlpha);
        var cornerColor = new Color4(80, 120, 200, 200);
        const float cornerLen = 14f;
        const float cornerThk = 2f;

        // Outer border
        renderer.DrawRectScreen(x - 2, y - 2, w + 4, h + 4, borderColor);
        // Inner background
        renderer.DrawRectScreen(x, y, w, h, bgColor);

        // Corner accents
        // Top-left
        renderer.DrawRectScreen(x - 2, y - 2, cornerLen, cornerThk, cornerColor);
        renderer.DrawRectScreen(x - 2, y - 2, cornerThk, cornerLen, cornerColor);
        // Top-right
        renderer.DrawRectScreen(x + w + 2 - cornerLen, y - 2, cornerLen, cornerThk, cornerColor);
        renderer.DrawRectScreen(x + w + 2 - cornerThk, y - 2, cornerThk, cornerLen, cornerColor);
        // Bottom-left
        renderer.DrawRectScreen(x - 2, y + h + 2 - cornerThk, cornerLen, cornerThk, cornerColor);
        renderer.DrawRectScreen(x - 2, y + h + 2 - cornerLen, cornerThk, cornerLen, cornerColor);
        // Bottom-right
        renderer.DrawRectScreen(x + w + 2 - cornerLen, y + h + 2 - cornerThk, cornerLen, cornerThk, cornerColor);
        renderer.DrawRectScreen(x + w + 2 - cornerThk, y + h + 2 - cornerLen, cornerThk, cornerLen, cornerColor);
    }

    /// <summary>
    /// Draw a sci-fi styled frame with a header strip and title.
    /// </summary>
    public static void DrawFrameWithHeader(SpriteRenderer renderer, float x, float y, float w, float h,
        string title, float titleScale = 1.8f, byte bgAlpha = 230)
    {
        const float headerH = 30f;

        DrawFrame(renderer, x, y, w, h, bgAlpha);

        // Header strip
        renderer.DrawRectScreen(x, y, w, headerH, new Color4(30, 40, 70, 240));
        // Header separator line
        renderer.DrawRectScreen(x, y + headerH - 1, w, 1, new Color4(60, 80, 140, 200));
        // Header label (centered)
        float labelW = renderer.MeasureText(title, titleScale);
        renderer.DrawTextScreen(x + w / 2f - labelW / 2f, y + 6, title, new Color3(140, 170, 220), titleScale);
    }
}
