namespace Engine.Platform;

/// <summary>
/// Base class for the game, owned by the engine layer.
/// Holds the platform reference and provides convenience accessors
/// for the subsystems (input, renderer, textures, audio).
/// </summary>
public abstract class GameBase : IDisposable
{
    public IPlatform Platform { get; protected set; } = null!;

    public IInputManager Input => Platform.InputManager;
    public ISpriteRenderer SpriteRenderer => Platform.SpriteRenderer;
    public ITextureManager Textures => Platform.Textures;
    public IAudioManager Audio => Platform.AudioManager;
    public ISettings Settings => Platform.Settings;
    public ISaveGame SaveGame => Platform.SaveGame;

    public abstract void Dispose();
}
