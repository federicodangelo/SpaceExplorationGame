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
    protected readonly DebugInfo _debugInfo = new();

    /// <summary>Called just before the game state is serialized to a save file.
    /// States should capture any live ECS/simulation positions into PlayerData here.</summary>
    public virtual void CapturePositionsForSave(Game game) { }

    /// <summary>Returns a human-readable description of the current location for save game display.</summary>
    public virtual string GetLocationDescription(string systemName) => systemName;

    /// <summary>Called when this state becomes active.</summary>
    public abstract void Enter(Game game);

    /// <summary>Called when this state is being left.</summary>
    public abstract void Exit(Game game);

    /// <summary>Called once per frame for input handling.</summary>
    public abstract void UpdateInput(Game game);

    /// <summary>Fixed timestep update for simulation (can run multiple times per frame).</summary>
    public abstract void Update(Game game);

    /// <summary>Render the game world for the current frame (world-space content).</summary>
    public abstract void RenderGame(Game game);

    /// <summary>Render the HUD for the current frame (screen-space content).</summary>
    public abstract void RenderHud(Game game);

    /// <summary>Render the current frame (game world followed by HUD).</summary>
    public void Render(Game game)
    {
        RenderGame(game);
        RenderHud(game);
    }

    /// <inheritdoc />
    public virtual IReadOnlyList<DebugTimingEntry>? GetDebugTimings() => _debugTimer.Entries;

    /// <inheritdoc />
    public virtual IReadOnlyList<string>? GetDebugInfo() => _debugInfo.Entries;
}
