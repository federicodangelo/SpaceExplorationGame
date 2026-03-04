
namespace SpaceExplorationGame.Audio;

/// <summary>
/// Adapts <see cref="SfxGenerator"/> to the engine's <see cref="ISfxProvider"/> interface.
/// Pre-generates all SFX buffers at construction and serves them by name.
/// </summary>
public sealed class GameSfxProvider : ISfxProvider
{
    private readonly Dictionary<string, float[]> _buffers;

    public GameSfxProvider(int sampleRate)
    {
        _buffers = SfxGenerator.GenerateAll(sampleRate);
    }

    public bool TryGetBuffer(string sfx, out float[] buffer)
    {
        return _buffers.TryGetValue(sfx, out buffer!);
    }
}
