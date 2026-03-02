using System.Numerics;

namespace Engine.Platform;

/// <summary>
/// Abstraction for audio playback — music themes and sound effects.
/// Theme and SFX names are plain strings so the engine has no knowledge
/// of which themes or effects exist — that is defined by the game.
/// </summary>
public interface IAudioManager : IDisposable
{
    float MasterVolume { get; set; }
    float MusicVolume { get; set; }
    float SfxVolume { get; set; }

    bool Initialize();

    void SetMusicTheme(string theme, bool instant = false);

    void PlaySfxAtDistance(string sfx, Vector2 soundPos, Vector2 relativeToPos,
        float volume = 1f, float maxRange = 800f);

    void PlaySfx(string sfx, float volume = 1f, float pan = 0f);

    void Update(float dt);
}
