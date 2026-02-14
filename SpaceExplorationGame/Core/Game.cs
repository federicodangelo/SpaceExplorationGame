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

    // Procedural generation
    public SeedManager Seeds { get; private set; } = null!;

    // Game state
    private GameState? _currentState;
    private GameState? _pendingState;

    // Player persistent data
    public PlayerData Player { get; } = new();

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

            // Fixed timestep updates
            int steps = 0;
            while (accumulator >= GameConfig.FixedTimeStep && steps < GameConfig.MaxFrameSkip)
            {
                _currentState?.Update(this, GameConfig.FixedTimeStep);
                accumulator -= GameConfig.FixedTimeStep;
                steps++;
            }

            // Clear edge-detection input only after Update has consumed it.
            // If no update ran this frame, events survive until the next frame.
            if (steps > 0)
            {
                Input.EndFrame();
            }

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
        SpriteRenderer.Dispose();
        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(Window);
        SDL.Quit();
        GC.SuppressFinalize(this);
    }
}
