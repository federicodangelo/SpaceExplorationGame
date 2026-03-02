namespace Engine.Platform;

/// <summary>
/// Provides real-time stereo PCM music generation.
/// The audio manager calls <see cref="Generate"/> each audio tick,
/// and <see cref="SetTheme"/> when the game requests a theme change.
/// </summary>
public interface IMusicProvider
{
    /// <summary>The name of the currently active music theme (empty string = silence).</summary>
    string CurrentTheme { get; }

    /// <summary>Switch to a new theme by name. Implementations decide how to map names to audio.</summary>
    void SetTheme(string theme);

    /// <summary>
    /// Generate <paramref name="frames"/> stereo frames, <b>adding</b> into <paramref name="buffer"/>
    /// (interleaved L/R float samples). Caller must clear the buffer beforehand if a clean mix is desired.
    /// </summary>
    void Generate(float[] buffer, int frames);
}
