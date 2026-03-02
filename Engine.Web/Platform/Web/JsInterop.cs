using System.Runtime.InteropServices.JavaScript;

namespace Engine.Platform.Web;

/// <summary>
/// Canvas rendering interop — calls into JavaScript for 2D drawing operations.
/// </summary>
internal static partial class JsCanvas
{
    [JSImport("canvas.beginFrame", "game.js")]
    internal static partial void BeginFrame(int width, int height);

    [JSImport("canvas.endFrame", "game.js")]
    internal static partial void EndFrame();

    [JSImport("canvas.setClipRect", "game.js")]
    internal static partial void SetClipRect(float x, float y, float w, float h);

    [JSImport("canvas.clearClipRect", "game.js")]
    internal static partial void ClearClipRect();

    [JSImport("canvas.fillRect", "game.js")]
    internal static partial void FillRect(float x, float y, float w, float h, int r, int g, int b, int a);

    [JSImport("canvas.drawLine", "game.js")]
    internal static partial void DrawLine(float x1, float y1, float x2, float y2, int r, int g, int b, int a);

    [JSImport("canvas.strokeCircle", "game.js")]
    internal static partial void StrokeCircle(float cx, float cy, float radius, int r, int g, int b, int a);

    [JSImport("canvas.fillCircle", "game.js")]
    internal static partial void FillCircle(float cx, float cy, float radius, int r, int g, int b, int a);

    [JSImport("canvas.fillCircleGradient", "game.js")]
    internal static partial void FillCircleGradient(float cx, float cy, float radius,
        int ir, int ig, int ib, int ia, int or_, int og, int ob, int oa, float transitionRadius);

    [JSImport("canvas.fillRing", "game.js")]
    internal static partial void FillRing(float cx, float cy, float innerR, float outerR, int r, int g, int b, int a);

    [JSImport("canvas.drawTexture", "game.js")]
    internal static partial void DrawTexture(int texId, float x, float y, float w, float h, float rotDeg, int alpha);

    [JSImport("canvas.drawTextureTinted", "game.js")]
    internal static partial void DrawTextureTinted(int texId, float x, float y, float w, float h,
        int r, int g, int b, int a, float rotDeg);

    [JSImport("canvas.drawTextureRect", "game.js")]
    internal static partial void DrawTextureRect(int texId, float dx, float dy, float dw, float dh, int alpha);

    [JSImport("canvas.drawTextureSrcDst", "game.js")]
    internal static partial void DrawTextureSrcDst(int texId,
        float sx, float sy, float sw, float sh,
        float dx, float dy, float dw, float dh, int alpha);

    [JSImport("canvas.drawTextureSrcDstTinted", "game.js")]
    internal static partial void DrawTextureSrcDstTinted(int texId,
        float sx, float sy, float sw, float sh,
        float dx, float dy, float dw, float dh,
        int r, int g, int b, int a);

    [JSImport("canvas.strokeTriangle", "game.js")]
    internal static partial void StrokeTriangle(float x1, float y1, float x2, float y2, float x3, float y3,
        int r, int g, int b, int a);

    [JSImport("canvas.fillTriangle", "game.js")]
    internal static partial void FillTriangle(float x1, float y1, float x2, float y2, float x3, float y3,
        int r, int g, int b, int a);

    [JSImport("canvas.setTitle", "game.js")]
    internal static partial void SetTitle(string title);
}

/// <summary>
/// Texture management interop — create/destroy textures backed by OffscreenCanvas.
/// </summary>
internal static partial class JsTexture
{
    [JSImport("texture.create", "game.js")]
    internal static partial int Create(byte[] pixels, int width, int height, int scaleMode);

    [JSImport("texture.destroy", "game.js")]
    internal static partial void Destroy(int id);
}

/// <summary>
/// Input interop — poll mouse/keyboard/gamepad state from JavaScript.
/// </summary>
internal static partial class JsInput
{
    [JSImport("input.getMouseX", "game.js")]
    internal static partial float GetMouseX();

    [JSImport("input.getMouseY", "game.js")]
    internal static partial float GetMouseY();

    [JSImport("input.getMouseWheel", "game.js")]
    internal static partial float GetMouseWheel();

    [JSImport("input.flushEvents", "game.js")]
    internal static partial string FlushEvents();

    [JSImport("input.getCanvasWidth", "game.js")]
    internal static partial int GetCanvasWidth();

    [JSImport("input.getCanvasHeight", "game.js")]
    internal static partial int GetCanvasHeight();

    [JSImport("input.getTextInput", "game.js")]
    internal static partial string GetTextInput();
}

/// <summary>
/// Audio interop — push PCM data to Web Audio API.
/// </summary>
internal static partial class JsAudio
{
    [JSImport("audio.init", "game.js")]
    internal static partial bool Init(int sampleRate);

    [JSImport("audio.pushChunk", "game.js")]
    internal static partial void PushChunk(double[] buffer, int frames);

    [JSImport("audio.getBufferedDuration", "game.js")]
    internal static partial double GetBufferedDuration();
}

/// <summary>
/// Settings interop — localStorage persistence.
/// </summary>
internal static partial class JsSettings
{
    [JSImport("settings.save", "game.js")]
    internal static partial void Save(string key, string value);

    [JSImport("settings.load", "game.js")]
    internal static partial string? Load(string key);
}
