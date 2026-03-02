using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Audio;

/// <summary>
/// Adapts <see cref="MusicGenerator"/> to the engine's <see cref="IMusicProvider"/> interface.
/// Maps string theme names to the internal generator.
/// </summary>
public sealed class GameMusicProvider : IMusicProvider
{
    private readonly MusicGenerator _generator;

    public string CurrentTheme { get; private set; } = AudioThemes.None;

    public GameMusicProvider(int sampleRate)
    {
        _generator = new MusicGenerator(sampleRate);
    }

    public void SetTheme(string theme)
    {
        CurrentTheme = theme;
        _generator.SetTheme(theme);
    }

    public void Generate(float[] buffer, int frames)
    {
        _generator.Generate(buffer, frames);
    }
}
