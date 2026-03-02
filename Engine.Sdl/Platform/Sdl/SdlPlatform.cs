using SDL3;

namespace Engine.Platform.Sdl;

/// <summary>
/// SDL3 implementation of the platform layer.
/// </summary>
public class SdlPlatform : IPlatform
{
    private nint _window;
    private nint _renderer;

    public string WindowTitle { get; }
    public int WindowWidth => SpriteRenderer.WindowWidth;
    public int WindowHeight => SpriteRenderer.WindowHeight;

    public ISpriteRenderer SpriteRenderer { get; private set; }
    public ITextureManager Textures { get; private set; }
    public IInputManager InputManager { get; private set; }
    public IAudioManager AudioManager { get; private set; }
    public ISettings Settings { get; private set; }

    public SdlPlatform(string windowTitle, int windowWidth, int windowHeight,
        IMusicProvider musicProvider, ISfxProvider sfxProvider,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
    {
        WindowTitle = windowTitle;

        // Init SDL
        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Audio | SDL.InitFlags.Gamepad))
        {
            throw new Exception($"SDL init failed: {SDL.GetError()}");
        }

        if (!SDL.CreateWindowAndRenderer(
                windowTitle,
                windowWidth,
                windowHeight,
                SDL.WindowFlags.Resizable,
                out var window,
                out var renderer))
        {
            throw new Exception($"Window creation failed: {SDL.GetError()}");
        }

        _window = window;
        _renderer = renderer;

        // Enable VSync to cap framerate and avoid screen tearing
        SDL.SetRenderVSync(renderer, 1);

        Textures = new SdlTextureManager(renderer);
        SpriteRenderer = new SdlSpriteRenderer(window, renderer, (SdlTextureManager)Textures);
        InputManager = new SdlInputManager(() => (WindowWidth, WindowHeight));
        AudioManager = new SdlAudioManager(musicProvider, sfxProvider,
            masterVolume: masterVolume,
            musicVolume: musicVolume,
            sfxVolume: sfxVolume
        );
        AudioManager.Initialize();
        Settings = new SdlSettings();
    }

    public void Update()
    {
        SpriteRenderer.Update();
    }

    public void Dispose()
    {
        SpriteRenderer.Dispose();
        AudioManager.Dispose();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        SDL.Quit();
    }
}
