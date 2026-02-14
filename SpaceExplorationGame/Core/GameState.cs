namespace SpaceExplorationGame.Core;

/// <summary>
/// Represents the current high-level game state / scene.
/// </summary>
public enum GameStateType
{
    MainMenu,
    GalaxyMap,
    SolarSystem,
    SpaceStation,
    PlanetLanding,
    PlanetSurface,
    Interior
}

/// <summary>
/// Base class for all game states. Each state manages its own ECS entities and systems.
/// </summary>
public abstract class GameState
{
    public abstract GameStateType Type { get; }

    /// <summary>Called when this state becomes active.</summary>
    public abstract void Enter(Game game);

    /// <summary>Called when this state is being left.</summary>
    public abstract void Exit(Game game);

    /// <summary>Fixed timestep update for logic.</summary>
    public abstract void Update(Game game, float dt);

    /// <summary>Render the current frame.</summary>
    public abstract void Render(Game game);

    /// <summary>Handle SDL events (input, etc).</summary>
    public abstract void HandleEvent(Game game, SDL3.SDL.Event e);
}
