using Arch.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI;
using SpaceExplorationGame.Platform;
using System.Diagnostics;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Main game class. Owns the platform, ECS world, and game state stack.
/// </summary>
public class Game : GameBase
{
    // ECS
    public World EcsWorld { get; private set; } = null!;

    // Simulation coordinator — always ticked, manages all active simulations
    public SimulationCoordinator Coordinator { get; } = new();

    // Entity renderers (own their textures)
    public AvatarRenderer AvatarRenderer { get; private set; } = null!;
    public VehicleRenderer VehicleRenderer { get; private set; } = null!;
    public SpaceshipRenderer SpaceshipRenderer { get; private set; } = null!;
    public SpaceStationRenderer SpaceStationRenderer { get; private set; } = null!;
    public AsteroidRenderer AsteroidRenderer { get; private set; } = null!;
    public PlanetRenderer PlanetRenderer { get; private set; } = null!;
    public StarRenderer StarRenderer { get; private set; } = null!;
    public EnemyShipRenderer EnemyShipRenderer { get; private set; } = null!;

    // Procedural generation
    public IUniverseGenerator UniverseGenerator { get; private set; } = null!;
    public SeedManager Seeds => UniverseGenerator.Seeds;

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

    public void Initialize(IPlatform platform, ulong? galaxySeed = null)
    {
        // Platform
        Platform = platform;

        // Sync GameConfig with platform window dimensions
        GameConfig.WindowWidth = Platform.WindowWidth;
        GameConfig.WindowHeight = Platform.WindowHeight;

        // ECS world
        EcsWorld = World.Create();

        // Entity renderers
        AvatarRenderer = new AvatarRenderer();
        VehicleRenderer = new VehicleRenderer();
        SpaceshipRenderer = new SpaceshipRenderer();
        SpaceStationRenderer = new SpaceStationRenderer(Textures);
        AsteroidRenderer = new AsteroidRenderer();
        PlanetRenderer = new PlanetRenderer();
        StarRenderer = new StarRenderer();
        EnemyShipRenderer = new EnemyShipRenderer();

        // Generation service + cached galaxy data
        UniverseGenerator = new ProceduralUniverseGenerator(new SeedManager(galaxySeed ?? (ulong)Random.Shared.NextInt64()));
        GalaxyData = UniverseGenerator.GenerateGalaxy();

        IsRunning = true;
    }

    /// <summary>
    /// Regenerate the galaxy with a new seed. Must be called from the main menu state.
    /// </summary>
    public void RegenerateGalaxy(ulong newSeed)
    {
        UniverseGenerator = new ProceduralUniverseGenerator(new SeedManager(newSeed));
        GalaxyData = UniverseGenerator.GenerateGalaxy();
        Player.Reset();
    }

    public void SetUniverseGenerator(IUniverseGenerator generator, bool regenerateGalaxy = true)
    {
        UniverseGenerator = generator;
        if (regenerateGalaxy)
            GalaxyData = UniverseGenerator.GenerateGalaxy();
    }

    public void UseProceduralUniverseGenerator(bool regenerateGalaxy = true)
    {
        SetUniverseGenerator(new ProceduralUniverseGenerator(UniverseGenerator.Seeds), regenerateGalaxy);
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
        var sw = new Stopwatch();
        sw.Start();
        double previousTime = sw.Elapsed.TotalSeconds;
        double accumulator = 0;

        _fpsTitleAccumTime = 0;
        _fpsTitleFrameCount = 0;
        SpriteRenderer.SetTitle(Platform.WindowTitle);

        while (IsRunning)
        {
            Platform.Update();

            // Sync GameConfig with platform window dimensions
            GameConfig.WindowWidth = Platform.WindowWidth;
            GameConfig.WindowHeight = Platform.WindowHeight;

            var currentTime = sw.Elapsed.TotalSeconds;
            var elapsed = currentTime - previousTime;
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
            if (Input.IsActionPressed(InputAction.DebugToggle))
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
            SpriteRenderer.BeginFrame();

            _debugTimer.Time("State Render", () => _currentState?.Render(this));

            if (_debugOverlayVisible)
            {
                _debugOverlay.Render(SpriteRenderer, _currentState, Coordinator, _debugTimer, elapsed * 1000.0);
            }

            SpriteRenderer.EndFrame();

            _fpsTitleAccumTime += elapsed;
            _fpsTitleFrameCount++;
            if (_fpsTitleAccumTime >= FpsTitleUpdateInterval)
            {
                double avgFps = _fpsTitleFrameCount / _fpsTitleAccumTime;
                SpriteRenderer.SetTitle($"{Platform.WindowTitle} - AVG FPS: {avgFps:F1}");
                _fpsTitleAccumTime = 0;
                _fpsTitleFrameCount = 0;
            }
        }
    }

    public override void Dispose()
    {
        _currentState?.Exit(this);
        Coordinator.DestroyAll();
        EcsWorld.Dispose();
        Platform.Dispose();
        GC.SuppressFinalize(this);
    }
}
