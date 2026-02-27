using Arch.Core;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Context passed to every simulation on each fixed-timestep tick.
/// </summary>
public readonly record struct UpdateContext(float Dt, double GlobalTime);

/// <summary>
/// Base interface for all game simulations (solar system, planet surface, interior).
/// Simulations manage ECS entities and systems independently of rendering.
/// They are always updated by the SimulationCoordinator, never paused.
/// If a simulation needs a reference to <see cref="Game"/>, it should receive it via its constructor.
/// </summary>
public interface ISimulation
{
    /// <summary>The ECS world owned by this simulation.</summary>
    World EcsWorld { get; }

    /// <summary>Whether any players are currently present in this simulation.</summary>
    bool HasPlayers { get; }

    /// <summary>
    /// Optional parent simulation. When a simulation has players, all ancestors in the
    /// parent chain are kept alive (their empty-timers are reset) so that upper-level
    /// simulations are never destroyed while a player is in a lower-level one.
    /// </summary>
    ISimulation? Parent { get; }

    /// <summary>Initialize the simulation: generate world content, create entities, set up ECS systems.</summary>
    void Create();

    /// <summary>Tear down the simulation: dispose the ECS world and release resources.</summary>
    void Destroy();

    /// <summary>
    /// Advance the simulation by one tick. Called every fixed timestep by the SimulationCoordinator.
    /// Must not contain any rendering or audio code.
    /// </summary>
    void Update(UpdateContext ctx);

    /// <summary>
    /// Add a player to this simulation. Creates the player's entity and returns a SimulationPlayer
    /// that the calling state can use for input and rendering. The returned entity is the player's
    /// primary entity (ship in solar system, avatar on planet surface, etc.).
    /// </summary>
    SimulationPlayer AddPlayer(PlayerData player);

    /// <summary>
    /// Remove a player from this simulation. Destroys the player's entity.
    /// If no players remain, the SimulationCoordinator will eventually destroy this simulation
    /// after a timeout period.
    /// </summary>
    void RemovePlayer(SimulationPlayer player);
}
