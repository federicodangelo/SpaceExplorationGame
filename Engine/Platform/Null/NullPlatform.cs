namespace Engine.Platform.Null;

/// <summary>
/// Headless platform implementation for dedicated servers.
/// All subsystems are no-op stubs — no window, no rendering, no audio, no real input.
/// </summary>
public sealed class NullPlatform : IPlatform
{
    public string WindowTitle { get; }
    public int WindowWidth => SpriteRenderer.WindowWidth;
    public int WindowHeight => SpriteRenderer.WindowHeight;
    public bool CanQuit => false;

    public ISpriteRenderer SpriteRenderer { get; }
    public ITextureManager Textures { get; }
    public IInputManager InputManager { get; }
    public IAudioManager AudioManager { get; }
    public ISettings Settings { get; }
    public ISaveGame SaveGame { get; }

    public NullPlatform(string windowTitle = "Server", int width = 800, int height = 600)
    {
        WindowTitle = windowTitle;
        SpriteRenderer = new NullSpriteRenderer(width, height);
        Textures = new NullTextureManager();
        InputManager = new NullInputManager();
        AudioManager = new NullAudioManager();
        Settings = new NullSettings();
        SaveGame = new NullSaveGame();
    }

    public void Update() { }

    public void Dispose()
    {
        AudioManager.Dispose();
        SpriteRenderer.Dispose();
    }
}
