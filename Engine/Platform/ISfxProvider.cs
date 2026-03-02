namespace Engine.Platform;

/// <summary>
/// Provides pre-generated mono PCM buffers for one-shot sound effects, keyed by name.
/// </summary>
public interface ISfxProvider
{
    /// <summary>Try to get the PCM buffer for a named sound effect. Returns false if unknown.</summary>
    bool TryGetBuffer(string sfx, out float[] buffer);
}
