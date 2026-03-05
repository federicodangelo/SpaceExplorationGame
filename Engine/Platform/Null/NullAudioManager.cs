using System.Numerics;

namespace Engine.Platform.Null;

/// <summary>
/// No-op audio manager for headless/server use. All calls are silently discarded.
/// </summary>
public sealed class NullAudioManager : IAudioManager
{
    public float MasterVolume { get; set; }
    public float MusicVolume { get; set; }
    public float SfxVolume { get; set; }

    public bool Initialize() => true;
    public void SetMusicTheme(string theme, bool instant = false) { }
    public void PlaySfxAtDistance(string sfx, Vector2 soundPos, Vector2 relativeToPos, float volume = 1f, float maxRange = 800f) { }
    public void PlaySfx(string sfx, float volume = 1f, float pan = 0f) { }
    public void Update(float dt) { }
    public void Dispose() { }
}
