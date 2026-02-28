using SDL3;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Platform;

public class Platform
{
    private nint _window;
    private nint _renderer;

    public SpriteRenderer SpriteRenderer { get; private set; }
    public TextureManager Textures { get; private set; }
    public InputManager InputManager { get; private set; }
    public AudioManager AudioManager { get; private set; }
    public TileMapRenderer TileMapRenderer { get; private set; }

    public Platform()
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

        Textures = new TextureManager(renderer);
        SpriteRenderer = new SpriteRenderer(window, renderer, Textures);
        TileMapRenderer = new TileMapRenderer();
        InputManager = new InputManager();
        AudioManager = new AudioManager(
            masterVolume: GameConfig.AudioMasterVolume,
            musicVolume: GameConfig.AudioMusicVolume,
            sfxVolume: GameConfig.AudioSfxVolume
        );
        AudioManager.Initialize();
    }

    public void Dispose()
    {
        //Textures.Dispose();
        SpriteRenderer.Dispose();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        SDL.Quit();
    }
}
