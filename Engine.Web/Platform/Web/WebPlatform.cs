namespace Engine.Platform.Web;

/// <summary>
/// Web platform implementation — owns all subsystems (renderer, input, audio, settings).
/// Window dimensions are driven by the browser canvas size reported from JavaScript.
/// </summary>
public class WebPlatform : IPlatform
{
    public string WindowTitle { get; }
    public int WindowWidth => SpriteRenderer.WindowWidth;
    public int WindowHeight => SpriteRenderer.WindowHeight;

    public ISpriteRenderer SpriteRenderer { get; }
    public ITextureManager Textures { get; }
    public IInputManager InputManager { get; }
    public IAudioManager AudioManager { get; }
    public ISettings Settings { get; }

    public WebPlatform(string windowTitle, int windowWidth, int windowHeight,
        IMusicProvider musicProvider, ISfxProvider sfxProvider,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
    {
        WindowTitle = windowTitle;

        var textures = new WebTextureManager();
        Textures = textures;
        SpriteRenderer = new WebSpriteRenderer(textures);
        InputManager = new WebInputManager(() => (WindowWidth, WindowHeight));
        AudioManager = new WebAudioManager(musicProvider, sfxProvider,
            masterVolume: masterVolume, musicVolume: musicVolume, sfxVolume: sfxVolume);
        AudioManager.Initialize();
        Settings = new WebSettings();
    }

    public void Update()
    {
        SpriteRenderer.Update();
    }

    public void Dispose()
    {
        SpriteRenderer.Dispose();
        AudioManager.Dispose();
    }
}
