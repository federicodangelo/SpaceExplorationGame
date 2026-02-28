namespace SpaceExplorationGame.Platform;

/// <summary>
/// Abstraction over the platform layer (window, renderer, input, audio).
/// </summary>
public interface IPlatform : IDisposable
{
    ISpriteRenderer SpriteRenderer { get; }
    ITextureManager Textures { get; }
    IInputManager InputManager { get; }
    IAudioManager AudioManager { get; }
}
