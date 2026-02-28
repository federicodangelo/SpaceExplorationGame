namespace SpaceExplorationGame.Core;

/// <summary>
/// Represents the current high-level game state / scene.
/// </summary>
public enum GameStateType
{
    MainMenu,
    SolarSystem,
    PlanetSurface,
    Interior
}

/// <summary>
/// Base class for all game states. Each state manages its own ECS entities and systems.
/// </summary>
public abstract class GameState : IDebugInfoProvider
{
    public abstract GameStateType Type { get; }

    // ── Debug ───────────────────────────────────────────────────────
    protected readonly DebugTimer _debugTimer = new();

    /// <summary>Called when this state becomes active.</summary>
    public abstract void Enter(Game game);

    /// <summary>Called when this state is being left.</summary>
    public abstract void Exit(Game game);

    /// <summary>Called once per frame for input handling.</summary>
    public abstract void UpdateInput(Game game);

    /// <summary>Fixed timestep update for simulation (can run multiple times per frame).</summary>
    public abstract void Update(Game game);

    /// <summary>Render the current frame.</summary>
    public abstract void Render(Game game);

    /// <summary>Handle SDL events (input, etc).</summary>
    public abstract void HandleEvent(Game game, SDL3.SDL.Event e);

    /// <inheritdoc />
    public virtual IReadOnlyList<DebugTimingEntry>? GetDebugTimings() => _debugTimer.Entries;

    /// <inheritdoc />
    public virtual IReadOnlyList<string>? GetDebugInfo() => null;
}
