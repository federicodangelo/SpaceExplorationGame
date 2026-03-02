using SDL3;

namespace SpaceExplorationGame.Platform.Sdl;

/// <summary>
/// SDL3 implementation of the platform layer.
/// </summary>
public class SdlPlatform : IPlatform
{
    private nint _window;
    private nint _renderer;

    public string WindowTitle { get; }
    public int WindowWidth { get; private set; }
    public int WindowHeight { get; private set; }

    public ISpriteRenderer SpriteRenderer { get; private set; }
    public ITextureManager Textures { get; private set; }
    public IInputManager InputManager { get; private set; }
    public IAudioManager AudioManager { get; private set; }

    public SdlPlatform(string windowTitle, int windowWidth, int windowHeight,
        IMusicProvider musicProvider, ISfxProvider sfxProvider,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
    {
        WindowTitle = windowTitle;
        WindowWidth = windowWidth;
        WindowHeight = windowHeight;

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
        InputManager = new SdlInputManager();
        AudioManager = new SdlAudioManager(musicProvider, sfxProvider,
            masterVolume: masterVolume,
            musicVolume: musicVolume,
            sfxVolume: sfxVolume
        );
        AudioManager.Initialize();
    }

    public void Update()
    {
        // Update window size in case of resize
        SDL.GetWindowSize(_window, out var width, out var height);
        WindowWidth = width;
        WindowHeight = height;
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
