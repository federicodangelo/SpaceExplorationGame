using SDL3;
using Engine.Platform.Base;

namespace Engine.Platform.Sdl;

/// <summary>
/// SDL3 implementation of the platform layer.
/// </summary>
public class SdlPlatform : BasePlatform
{
    private nint _window;
    private nint _renderer;

    public SdlPlatform(string windowTitle, int windowWidth, int windowHeight,
        IMusicProvider musicProvider, ISfxProvider sfxProvider,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
        : base(windowTitle)
    {

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
        SaveGame = new SdlSaveGame();
    }

    public override void Dispose()
    {
        base.Dispose();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        SDL.Quit();
    }
}
