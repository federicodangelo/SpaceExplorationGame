using SDL3;
using Arch.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

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
    public Camera Camera { get; private set; } = null!;
    public InputManager Input { get; } = new();
    public SpriteRenderer SpriteRenderer { get; private set; } = null!;
    public TextureManager Textures { get; private set; } = null!;

    // Procedural generation
    public SeedManager Seeds { get; private set; } = null!;

    // Game state
    private GameState? _currentState;
    private GameState? _pendingState;

    // Player persistent data
    public PlayerData Player { get; } = new();

    // Global simulation time (never resets, used for deterministic orbit positions)
    public double GlobalTime { get; private set; }

    public bool IsRunning { get; private set; }

    public void Initialize(ulong? galaxySeed = null)
    {
        // Init SDL
        if (!SDL.Init(SDL.InitFlags.Video))
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

        // Camera
        Camera = new Camera(GameConfig.WindowWidth, GameConfig.WindowHeight);

        // Sprite renderer
        SpriteRenderer = new SpriteRenderer(Renderer);

        // Texture manager (procedural pixel art)
        Textures = new TextureManager(Renderer);

        // Seed manager
        Seeds = new SeedManager(galaxySeed ?? (ulong)Random.Shared.NextInt64());

        IsRunning = true;
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
            _currentState?.UpdateInput(this);

            // Fixed timestep updates (may run multiple times per frame)
            int steps = 0;
            while (accumulator >= GameConfig.FixedTimeStep && steps < GameConfig.MaxFrameSkip)
            {
                GlobalTime += GameConfig.FixedTimeStep;
                _currentState?.Update(this, GameConfig.FixedTimeStep);
                accumulator -= GameConfig.FixedTimeStep;
                steps++;
            }

            // Clear edge-detection input after UpdateInput/Update have consumed it.
            Input.EndFrame();

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
        EcsWorld.Dispose();
        Textures.Dispose();
        SpriteRenderer.Dispose();
        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(Window);
        SDL.Quit();
        GC.SuppressFinalize(this);
    }
}
