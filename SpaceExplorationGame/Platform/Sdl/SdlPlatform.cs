using SDL3;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Platform.Sdl;

/// <summary>
/// SDL3 implementation of the platform layer.
/// </summary>
public class SdlPlatform : IPlatform
{
    private nint _window;
    private nint _renderer;

    public ISpriteRenderer SpriteRenderer { get; private set; }
    public ITextureManager Textures { get; private set; }
    public IInputManager InputManager { get; private set; }
    public IAudioManager AudioManager { get; private set; }

    public SdlPlatform()
    {
        // Init SDL
        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Audio | SDL.InitFlags.Gamepad))
        {
            throw new Exception($"SDL init failed: {SDL.GetError()}");
        }

        if (!SDL.CreateWindowAndRenderer(
                GameConfig.WindowTitle,
                GameConfig.WindowWidth,
                GameConfig.WindowHeight,
                0,
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
        AudioManager = new SdlAudioManager(
            masterVolume: GameConfig.AudioMasterVolume,
            musicVolume: GameConfig.AudioMusicVolume,
            sfxVolume: GameConfig.AudioSfxVolume
        );
        AudioManager.Initialize();
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
