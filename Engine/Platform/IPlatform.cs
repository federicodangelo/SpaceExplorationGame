namespace Engine.Platform;

/// <summary>
/// Abstraction over the platform layer (window, renderer, input, audio).
/// </summary>
public interface IPlatform : IDisposable
{
    string WindowTitle { get; }
    int WindowWidth { get; }
    int WindowHeight { get; }

    ISpriteRenderer SpriteRenderer { get; }
    ITextureManager Textures { get; }
    IInputManager InputManager { get; }
    IAudioManager AudioManager { get; }
    ISettings Settings { get; }
    void Update();
}
