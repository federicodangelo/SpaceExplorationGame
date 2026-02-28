using System.Numerics;
using SpaceExplorationGame.Audio;

namespace SpaceExplorationGame.Platform;

/// <summary>
/// Abstraction for audio playback — music themes and sound effects.
/// </summary>
public interface IAudioManager : IDisposable
{
    float MasterVolume { get; set; }
    float MusicVolume { get; set; }
    float SfxVolume { get; set; }

    bool Initialize();

    void SetMusicTheme(MusicTheme theme, bool instant = false);

    void PlaySfxAtDistance(SfxType type, Vector2 soundPos, Vector2 relativeToPos,
        float volume = 1f, float maxRange = 800f);

    void PlaySfx(SfxType type, float volume = 1f, float pan = 0f);

    void Update(float dt);
}
