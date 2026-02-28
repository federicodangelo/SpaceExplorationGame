using SDL3;
using Arch.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.Simulation.Base;
using SpaceExplorationGame.UI;

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

    // Simulation coordinator — always ticked, manages all active simulations
    public SimulationCoordinator Coordinator { get; } = new();

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
    public IWorldGenerator WorldGenerator { get; private set; } = null!;

    /// <summary>Cached galaxy data — generated once from the galaxy seed, reused everywhere.</summary>
    public List<StarSystemData> GalaxyData { get; private set; } = [];

    // Game state
    private GameState? _currentState;
    private GameState? _pendingState;

    // Player persistent data
    public PlayerData Player { get; } = new();

    // Global simulation time (never resets, used for deterministic orbit positions)
    public double GlobalTime { get; private set; }

    public float DeltaTime { get; private set; }

    public bool IsRunning { get; private set; }

    // Window title FPS tracking
    private double _fpsTitleAccumTime;
    private int _fpsTitleFrameCount;
    private const double FpsTitleUpdateInterval = 1;

    // Debug overlay
    private readonly DebugOverlay _debugOverlay = new();
    private bool _debugOverlayVisible;
    private readonly DebugTimer _debugTimer = new();

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

        // Enable VSync to cap framerate and avoid screen tearing
        SDL.SetRenderVSync(renderer, 1);

        // ECS world
        EcsWorld = World.Create();

        // Texture manager (procedural pixel art)
        Textures = new TextureManager(Renderer);

        // Sprite renderer
        SpriteRenderer = new SpriteRenderer(Renderer, Textures);

        // Entity renderers
        AvatarRenderer = new AvatarRenderer();
        VehicleRenderer = new VehicleRenderer();
        SpaceshipRenderer = new SpaceshipRenderer();
        StationRenderer = new StationRenderer(Textures);
        AsteroidRenderer = new AsteroidRenderer();
        PlanetRenderer = new PlanetRenderer();
        StarRenderer = new StarRenderer();
        EnemyShipRenderer = new EnemyShipRenderer();

        // Audio
        Audio = new AudioManager(
            masterVolume: GameConfig.AudioMasterVolume,
            musicVolume: GameConfig.AudioMusicVolume,
            sfxVolume: GameConfig.AudioSfxVolume);
        Audio.Initialize();

        // Seed manager
        Seeds = new SeedManager(galaxySeed ?? (ulong)Random.Shared.NextInt64());

        // Generation service + cached galaxy data
        WorldGenerator = new ProceduralWorldGenerator();
        GalaxyData = WorldGenerator.GenerateGalaxy(Seeds);

        IsRunning = true;
    }

    /// <summary>
    /// Regenerate the galaxy with a new seed. Must be called from the main menu state.
    /// </summary>
    public void RegenerateGalaxy(ulong newSeed)
    {
        Seeds = new SeedManager(newSeed);
        GalaxyData = WorldGenerator.GenerateGalaxy(Seeds);
        Player.Reset();
    }

    public void SetWorldGenerator(IWorldGenerator generator, bool regenerateGalaxy = true)
    {
        WorldGenerator = generator;
        if (regenerateGalaxy)
            GalaxyData = WorldGenerator.GenerateGalaxy(Seeds);
    }

    public void UseProceduralWorldGenerator(bool regenerateGalaxy = true)
    {
        SetWorldGenerator(new ProceduralWorldGenerator(), regenerateGalaxy);
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

        _fpsTitleAccumTime = 0;
        _fpsTitleFrameCount = 0;
        SDL.SetWindowTitle(Window, GameConfig.WindowTitle);

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
            Input.ProcessEvents();

            if (Input.QuitRequested)
            {
                IsRunning = false;
                break;
            }

            // Toggle debug overlay
            if (Input.IsKeyPressed(SDL.Scancode.Alpha1))
                _debugOverlayVisible = !_debugOverlayVisible;

            // Apply pending state changes
            ApplyPendingState();

            // Process input once per frame
            _currentState?.UpdateInput(this);

            // Fixed timestep updates (may run multiple times per frame)
            _debugTimer.Begin();
            _debugTimer.PresetAccumulators("Simulation Update", "State Update");
            int steps = 0;
            while (accumulator >= GameConfig.FixedTimeStep && steps < GameConfig.MaxFrameSkip)
            {
                GlobalTime += GameConfig.FixedTimeStep;
                DeltaTime = GameConfig.FixedTimeStep;

                // Always tick all simulations first (physics, AI, combat)
                _debugTimer.TimeAndAccumulate("Simulation Update", () =>
                    Coordinator.Update(new UpdateContext(GameConfig.FixedTimeStep, GlobalTime)));

                // Then let the current state do per-tick post-processing (camera, effects)
                _debugTimer.TimeAndAccumulate("State Update", () => _currentState?.Update(this));

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

            _debugTimer.Time("State Render", () => _currentState?.Render(this));

            if (_debugOverlayVisible)
            {
                _debugOverlay.Render(SpriteRenderer, _currentState, Coordinator, _debugTimer, elapsed * 1000.0);
            }

            SDL.RenderPresent(Renderer);

            _fpsTitleAccumTime += elapsed;
            _fpsTitleFrameCount++;
            if (_fpsTitleAccumTime >= FpsTitleUpdateInterval)
            {
                double avgFps = _fpsTitleFrameCount / _fpsTitleAccumTime;
                SDL.SetWindowTitle(Window, $"{GameConfig.WindowTitle} - AVG FPS: {avgFps:F1}");
                _fpsTitleAccumTime = 0;
                _fpsTitleFrameCount = 0;
            }
        }
    }

    public void Dispose()
    {
        _currentState?.Exit(this);
        Coordinator.DestroyAll();
        Audio.Dispose();
        EcsWorld.Dispose();
        SpriteRenderer.Dispose();
        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(Window);
        SDL.Quit();
        GC.SuppressFinalize(this);
    }
}
