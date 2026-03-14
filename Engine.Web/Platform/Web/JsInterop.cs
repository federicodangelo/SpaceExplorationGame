using System.Runtime.InteropServices.JavaScript;

namespace Engine.Platform.Web;

/// <summary>
/// Canvas rendering interop — all draw commands are serialized into a binary buffer
/// and replayed in JavaScript via a single <see cref="FlushCommandBuffer"/> interop call.
/// </summary>
internal static partial class JsCanvas
{
    /// <summary>
    /// Passes the entire serialized command buffer to JavaScript for decoding and replay
    /// in a single interop call, eliminating per-draw-call marshaling overhead.
    /// Only the first <paramref name="length"/> bytes of <paramref name="buffer"/> are valid.
    /// <paramref name="cachedCircleTexId"/> is the texture ID for the pre-rasterized circle
    /// (0 when unavailable).
    /// </summary>
    [JSImport("canvas.flushCommandBuffer", "game.js")]
    internal static partial void FlushCommandBuffer(byte[] buffer, int length, int cachedCircleTexId);
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

    [JSImport("input.pollGamepad", "game.js")]
    internal static partial string PollGamepad();
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

    [JSImport("settings.remove", "game.js")]
    internal static partial void Remove(string key);
}

/// <summary>
/// Launch-options interop — reads URL query parameters passed at startup.
/// </summary>
public static partial class JsLaunchOptions
{
    /// <summary>
    /// Returns the value of the named URL query parameter, or <c>null</c> if absent.
    /// Example: for <c>?seed=42&amp;location=planet</c>, GetUrlParam("seed") returns "42".
    /// </summary>
    [JSImport("launchOptions.getUrlParam", "game.js")]
    public static partial string? GetUrlParam(string name);
}
