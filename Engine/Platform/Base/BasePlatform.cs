namespace Engine.Platform.Base;

/// <summary>
/// Abstract base for SDL and Web platform implementations.
/// Owns all subsystem properties and provides default <see cref="Update"/>
/// and <see cref="Dispose"/> implementations that subclasses can extend.
/// </summary>
public abstract class BasePlatform : IPlatform
{
    public string WindowTitle { get; }
    public int WindowWidth => SpriteRenderer.WindowWidth;
    public int WindowHeight => SpriteRenderer.WindowHeight;
    public virtual bool CanQuit => true;

    public ISpriteRenderer SpriteRenderer { get; protected set; } = null!;
    public ITextureManager Textures { get; protected set; } = null!;
    public IInputManager InputManager { get; protected set; } = null!;
    public IAudioManager AudioManager { get; protected set; } = null!;
    public ISettings Settings { get; protected set; } = null!;
    public ISaveGame SaveGame { get; protected set; } = null!;

    protected BasePlatform(string windowTitle)
    {
        WindowTitle = windowTitle;
    }

    public virtual void Update()
    {
        SpriteRenderer.Update();
    }

    public virtual void Dispose()
    {
        SpriteRenderer.Dispose();
        AudioManager.Dispose();
        GC.SuppressFinalize(this);
    }
}
