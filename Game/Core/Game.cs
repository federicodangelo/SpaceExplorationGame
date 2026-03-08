using Engine.Network;
using Engine.Network.Client;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI;
using System.Diagnostics;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Main game class. Owns the platform, ECS world, and game state stack.
/// </summary>
public class Game : GameBase
{
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
    public PlayerData Player { get; } = PlayerData.CreateLocal();

    // Autoplay bot
    public AutoplayBot AutoplayBot { get; } = new();

    // Network (null when playing offline)
    public ClientNetworkManager? Network { get; set; }

    // Menu options persistence
    public MenuOptionsPersistence MenuOptions { get; private set; } = null!;

    public string PlayerName { get; set; } = "Player";

    // Global simulation time (never resets, used for deterministic orbit positions)
    public double GlobalTime { get; private set; }

    public float DeltaTime { get; private set; }

    public bool IsRunning { get; private set; }

    public void Quit() => IsRunning = false;

    // Debug overlay
    private readonly DebugOverlay _debugOverlay = new();
    private bool _debugOverlayVisible;
    private readonly DebugTimer _debugTimer = new();

    // Screenshot toast
    private string? _screenshotToastMessage;
    private float _screenshotToastTimer;

    // Loop timing (instance fields so RunOneFrame can be called externally)
    private Stopwatch? _frameSw;
    private double _previousTime;
    private double _accumulator;

    public void Initialize(IPlatform platform, ulong? galaxySeed = null)
    {
        // Platform
        Platform = platform;

        // Menu options persistence (uses platform settings)
        MenuOptions = new MenuOptionsPersistence(platform.Settings);

        PlayerName = MenuOptions.GetPlayerName();

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

        _currentState = _pendingState;
        _pendingState = null;

        _currentState.Enter(this);

        // Full reset so the new state doesn't react to any input
        // (pressed, released, or held-down) from the previous state.
        Input.Reset();
    }

    public void Run()
    {
        InitializeLoop();
        while (IsRunning)
        {
            RunOneFrame();
        }
    }

    /// <summary>
    /// Initialize the game loop timing. Called once before the first frame.
    /// </summary>
    public void InitializeLoop()
    {
        _frameSw = new Stopwatch();
        _frameSw.Start();
        _previousTime = _frameSw.Elapsed.TotalSeconds;
        _accumulator = 0;
        SpriteRenderer.SetTitle(Platform.WindowTitle);
    }

    /// <summary>
    /// Execute a single frame of the game loop (input, update, render).
    /// Used by non-blocking hosts (e.g. browser requestAnimationFrame).
    /// Call <see cref="InitializeLoop"/> once before the first call.
    /// </summary>
    public void RunOneFrame()
    {
        if (!IsRunning) return;

        Platform.Update();

        var currentTime = _frameSw!.Elapsed.TotalSeconds;
        var elapsed = currentTime - _previousTime;
        _previousTime = currentTime;

        // Cap max elapsed to avoid spiral of death
        if (elapsed > 0.25) elapsed = 0.25;
        _accumulator += elapsed;

        DeltaTime = (float)elapsed;

        // Process events
        Input.BeginFrame();
        Input.ProcessEvents();

        if (Input.QuitRequested)
        {
            IsRunning = false;
            return;
        }

        // Toggle debug overlay
        if (Input.IsActionPressed(InputAction.DebugToggle))
            _debugOverlayVisible = !_debugOverlayVisible;

        // Screenshot
        if (Input.IsActionPressed(InputAction.Screenshot))
        {
            var fileName = SpriteRenderer.TakeScreenshot();
            _screenshotToastMessage = fileName != null
                ? $"Screenshot saved: {fileName}"
                : "Screenshot failed";
            _screenshotToastTimer = 3f;
        }

        // Apply pending state changes
        ApplyPendingState();

        // Process network messages (state-agnostic — always drain even between states)
        Network?.ProcessMessages();

        // Detect unexpected server disconnect — return to main menu
        if (Network is { IsJoined: true } && !Network.IsConnected)
        {
            Console.WriteLine("[Net] Disconnected from server — returning to main menu.");
            Network?.Dispose();
            Network = null;
            ChangeState(new SpaceExplorationGame.States.MainMenuState());
        }

        // ── Network: sync remote entities + send local state──
        if (Network is { IsJoined: true } net)
        {
            SyncRemotePlayersInSimulations(net, Coordinator.Simulations);
            SyncNpcStatesInSimulations(net, Coordinator.Simulations);
            SendLocalPlayerStateToServer(net, Coordinator.Simulations);
        }

        // Process input once per frame
        _currentState?.UpdateInput(this);

        // Fixed timestep updates (may run multiple times per frame)
        _debugTimer.Begin();
        _debugTimer.PresetAccumulators("Simulation Update", "State Update");
        int steps = 0;
        while (_accumulator >= WindowConfig.FixedTimeStep && steps < WindowConfig.MaxFrameSkip)
        {
            GlobalTime += WindowConfig.FixedTimeStep;
            DeltaTime = WindowConfig.FixedTimeStep;

            // Always tick all simulations first (physics, AI, combat)
            _debugTimer.TimeAndAccumulate("Simulation Update", () =>
                Coordinator.Update(new UpdateContext(WindowConfig.FixedTimeStep, GlobalTime)));

            // Then let the current state do per-tick post-processing (camera, effects)
            _debugTimer.TimeAndAccumulate("State Update", () => _currentState?.Update(this));

            _accumulator -= WindowConfig.FixedTimeStep;
            steps++;
        }

        // Clear edge-detection input after UpdateInput/Update have consumed it.
        Input.EndFrame();

        // Audio — keep playback buffer topped up
        Audio.Update((float)elapsed);

        // Render
        SpriteRenderer.BeginFrame();

        _debugTimer.Time("State Render", () => _currentState?.Render(this));

        AutoplayBot.RenderStatus(SpriteRenderer);

        if (_debugOverlayVisible)
        {
            _debugOverlay.Render(SpriteRenderer, _currentState, Coordinator, _debugTimer, elapsed * 1000.0);
        }

        // Screenshot toast
        if (_screenshotToastTimer > 0f && _screenshotToastMessage != null)
        {
            _screenshotToastTimer -= DeltaTime;
            float alpha = _screenshotToastTimer < 1f ? _screenshotToastTimer : 1f;
            byte a = (byte)(alpha * 220);
            float scale = 1.5f;
            float textW = SpriteRenderer.MeasureText(_screenshotToastMessage, scale);
            float padding = 8f;
            float tw = textW + padding * 2f;
            float th = 20f * scale + padding * 2f;
            float tx = SpriteRenderer.WindowWidth - tw - 10f;
            float ty = SpriteRenderer.WindowHeight - th - 10f;
            SpriteRenderer.DrawRectScreen(tx, ty, tw, th, new Engine.Core.Color4(0, 0, 0, a));
            SpriteRenderer.DrawTextScreen(tx + padding, ty + padding, _screenshotToastMessage, new Engine.Core.Color4(255, 255, 255, a), scale);
        }

        SpriteRenderer.EndFrame();
    }


    private void SyncRemotePlayersInSimulations(ClientNetworkManager net, IEnumerable<ISimulation> simulations)
    {
        foreach (var entry in simulations)
            entry.SyncRemotePlayers(net);
    }

    private void SyncNpcStatesInSimulations(ClientNetworkManager net, IEnumerable<ISimulation> simulations)
    {
        foreach (var entry in simulations)
        {
            if (entry is SpaceExplorationGame.Simulation.Base.CombatSimulationBase combatSim)
                combatSim.SyncNpcStates(net);
        }
    }

    private NetPlayerLocation lastSentLocalPlayerLocation = NetPlayerLocation.ForUnknown();

    private void SendLocalPlayerStateToServer(ClientNetworkManager net, IEnumerable<ISimulation> simulations)
    {
        var localPlayers = simulations.Select(s => s.GetLocalPlayer()).Where(p => p != null).ToList();
        if (localPlayers.Count > 1)
        {
            Console.WriteLine("[Net] Warning: multiple local players found in simulations, sending state for the first one only");
        }
        var localPlayer = localPlayers.FirstOrDefault();
        if (localPlayer == null) return;
        var simulation = localPlayer.Simulation;
        var location = simulation.GetNetPlayerLocation();
        if (location != lastSentLocalPlayerLocation)
        {
            net.SendLocationChanged(location);
            lastSentLocalPlayerLocation = location;
        }
        var state = localPlayer.Simulation.GetNetPlayerState(localPlayer);
        net.SendLocalState(state);
    }

    public override void Dispose()
    {
        _currentState?.Exit(this);
        Network?.Dispose();
        Coordinator.DestroyAll();
        Platform.Dispose();
        GC.SuppressFinalize(this);
    }
}
