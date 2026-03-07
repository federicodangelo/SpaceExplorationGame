using System.Numerics;
using Arch.Core;
using Engine.Network;
using Engine.Network.Client;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Context passed to every simulation on each fixed-timestep tick.
/// </summary>
public readonly record struct UpdateContext(float Dt, double GlobalTime);

/// <summary>
/// Context passed to <see cref="ISimulation.AddPlayer"/> when a player joins a simulation.
/// Contains per-player, per-join data such as landing coordinates that may differ each time.
/// </summary>
public readonly record struct AddContext(int LandingTileX = -1, int LandingTileY = -1);

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

    /// <summary>>Get the local player in this simulation, or null if the local player is not present.</summary
    SimulationPlayer? GetLocalPlayer();

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
    SimulationPlayer AddPlayer(PlayerData player, AddContext ctx = default);

    /// <summary>
    /// Remove a player from this simulation. Destroys the player's entity.
    /// If no players remain, the SimulationCoordinator will eventually destroy this simulation
    /// after a timeout period.
    /// </summary>
    void RemovePlayer(SimulationPlayer player);

    /// <summary>
    /// Sync remote player states from the server. Called by the SimulationCoordinator every tick if the game is connected to a multiplayer server. Each simulation should update the states of any remote players it
    /// </summary>
    /// <param name="net"></param>
    void SyncRemotePlayers(ClientNetworkManager net);

    /// <summary>
    /// Gets the compact per-tick state of a player entity that should be sent to the server and relayed to other clients. Called by the SimulationCoordinator every tick if the game is connected to a multiplayer server, after which the resulting states are sent to the server and relayed back to all clients, which then call ApplyNetPlayerState on each simulation with the received state. Each simulation should return a non-null state for its local player if it has one, or null if it doesn't (e.g. if the local player is in a different simulation or hasn't fully spawned yet). The returned state will be applied to the local player on all clients, including this one, so it should contain the latest input and position data for this player.
    /// </summary>
    NetPlayerState GetNetPlayerState(SimulationPlayer player);

    /// <summary>
    /// Applies the latest per-tick state of the local player received from the server. Called by the SimulationCoordinator every tick if the game is connected to a multiplayer server, after GetLocalPlayerNetPlayerState has been called on all simulations and the resulting states have been sent to the server and relayed back to all clients. Each simulation should apply the state to its local player if it matches this simulation's location, or ignore it otherwise.
    /// </summary>
    /// <param name="state"></param>
    void ApplyNetPlayerState(SimulationPlayer player, NetPlayerState state);

    /// <summary>
    /// Gets the net player location that represents this simulation
    /// </summary>
    NetPlayerLocation GetNetPlayerLocation();

    /// <summary> 
    /// Gets the default spawn coordinates for players joining this simulation. Used when the player is joining for the first time and has no previous location to return to. 
    /// </summary>
    Vector2 GetDefaultSpawnCoordinates();
}
