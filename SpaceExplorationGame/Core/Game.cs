using SDL3;
using Arch.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Main game class. Owns the SDL window/renderer, ECS world, and game state stack.
/// </summary>
public class Game : IDisposable
{
    // SDL
    public nint Window { get; private set; }
    public nint Renderer { get; private set; }

    // ECS
    public World EcsWorld { get; private set; } = null!;

    // Core systems
    public InputManager Input { get; } = new();
    public SpriteRenderer SpriteRenderer { get; private set; } = null!;
    public TextureManager Textures { get; private set; } = null!;

    // Entity renderers (own their textures)
    public AvatarRenderer AvatarRenderer { get; private set; } = null!;
    public VehicleRenderer VehicleRenderer { get; private set; } = null!;
    public SpaceshipRenderer SpaceshipRenderer { get; private set; } = null!;
    public StationRenderer StationRenderer { get; private set; } = null!;
    public AsteroidRenderer AsteroidRenderer { get; private set; } = null!;
    public PlanetRenderer PlanetRenderer { get; private set; } = null!;
    public StarRenderer StarRenderer { get; private set; } = null!;
    public EnemyShipRenderer EnemyShipRenderer { get; private set; } = null!;

    // Audio
    public AudioManager Audio { get; private set; } = null!;

    // Procedural generation
    public SeedManager Seeds { get; private set; } = null!;

    /// <summary>Cached galaxy data — generated once from the galaxy seed, reused everywhere.</summary>
    public List<StarSystemData> GalaxyData { get; private set; } = [];

    // Game state
    private GameState? _currentState;
    private GameState? _pendingState;

    // Player persistent data
    public PlayerData Player { get; } = new();

    // Global simulation time (never resets, used for deterministic orbit positions)
    public double GlobalTime { get; private set; }

    public float DeltaTime {get; private set; }

    public bool IsRunning { get; private set; }

    public void Initialize(ulong? galaxySeed = null)
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

        Window = window;
        Renderer = renderer;

        // ECS world
        EcsWorld = World.Create();

        // Texture manager (procedural pixel art)
        Textures = new TextureManager(Renderer);

        // Sprite renderer
        SpriteRenderer = new SpriteRenderer(Renderer, Textures);

        // Entity renderers
        AvatarRenderer = new AvatarRenderer(Textures);
        VehicleRenderer = new VehicleRenderer(Textures);
        SpaceshipRenderer = new SpaceshipRenderer(Textures);
        StationRenderer = new StationRenderer(Textures);
        AsteroidRenderer = new AsteroidRenderer(Textures);
        PlanetRenderer = new PlanetRenderer(Textures);
        StarRenderer = new StarRenderer(Textures);
        EnemyShipRenderer = new EnemyShipRenderer(Textures);

        // Audio
        Audio = new AudioManager(
            masterVolume: GameConfig.AudioMasterVolume,
            musicVolume: GameConfig.AudioMusicVolume,
            sfxVolume: GameConfig.AudioSfxVolume);
        Audio.Initialize();

        // Seed manager
        Seeds = new SeedManager(galaxySeed ?? (ulong)Random.Shared.NextInt64());

        // Generate and cache galaxy data once
        GalaxyData = GalaxyGenerator.Generate(Seeds.GetGalaxyRandom());

        IsRunning = true;
    }

    /// <summary>
    /// Regenerate the galaxy with a new seed. Must be called from the main menu state.
    /// </summary>
    public void RegenerateGalaxy(ulong newSeed)
    {
        Seeds = new SeedManager(newSeed);
        GalaxyData = GalaxyGenerator.Generate(Seeds.GetGalaxyRandom());
        Player.Reset();
    }

    public void ChangeState(GameState newState)
    {
        _pendingState = newState;
    }

    private void ApplyPendingState()
    {
        if (_pendingState == null) return;

        _currentState?.Exit(this);

        // Destroy all entities when changing states
        EcsWorld.Dispose();
        EcsWorld = World.Create();

        _currentState = _pendingState;
        _pendingState = null;

        _currentState.Enter(this);

        // Full reset so the new state doesn't react to any input
        // (pressed, released, or held-down) from the previous state.
        Input.Reset();
    }

    public void Run()
    {
        var previousTime = SDL.GetPerformanceCounter();
        var frequency = (double)SDL.GetPerformanceFrequency();
        double accumulator = 0;

        while (IsRunning)
        {
            var currentTime = SDL.GetPerformanceCounter();
            var elapsed = (currentTime - previousTime) / frequency;
            previousTime = currentTime;

            // Cap max elapsed to avoid spiral of death
            if (elapsed > 0.25) elapsed = 0.25;
            accumulator += elapsed;

            DeltaTime = (float)elapsed;

            // Process events
            Input.BeginFrame();
            while (SDL.PollEvent(out var e))
            {
                Input.ProcessEvent(e);
                _currentState?.HandleEvent(this, e);
            }

            if (Input.QuitRequested)
            {
                IsRunning = false;
                break;
            }

            // Apply pending state changes
            ApplyPendingState();

            // Process input once per frame
            _currentState?.UpdateInput(this, (float)elapsed);

            // Fixed timestep updates (may run multiple times per frame)
            int steps = 0;
            while (accumulator >= GameConfig.FixedTimeStep && steps < GameConfig.MaxFrameSkip)
            {
                GlobalTime += GameConfig.FixedTimeStep;
                DeltaTime = GameConfig.FixedTimeStep;
                _currentState?.Update(this, GameConfig.FixedTimeStep);
                accumulator -= GameConfig.FixedTimeStep;
                steps++;
            }

            // Clear edge-detection input after UpdateInput/Update have consumed it.
            Input.EndFrame();

            // Audio — keep playback buffer topped up
            Audio.Update((float)elapsed);

            // Render
            SDL.SetRenderDrawColor(Renderer, 0, 0, 0, 255);
            SDL.RenderClear(Renderer);

            _currentState?.Render(this);

            SDL.RenderPresent(Renderer);
        }
    }

    public void Dispose()
    {
        _currentState?.Exit(this);
        Audio.Dispose();
        EcsWorld.Dispose();
        AvatarRenderer.Dispose();
        VehicleRenderer.Dispose();
        SpaceshipRenderer.Dispose();
        StationRenderer.Dispose();
        AsteroidRenderer.Dispose();
        PlanetRenderer.Dispose();
        StarRenderer.Dispose();
        EnemyShipRenderer.Dispose();
        Textures.Dispose();
        SpriteRenderer.Dispose();
        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(Window);
        SDL.Quit();
        GC.SuppressFinalize(this);
    }
}
