using Arch.Core;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Encapsulates a player's presence within a simulation.
/// Holds the player's persistent data and the entity created for them in the simulation's ECS world.
/// Designed for future multiplayer support — each connected player gets their own SimulationPlayer instance.
/// </summary>
public class SimulationPlayer
{
    /// <summary>Persistent player data (credits, inventory, stats, etc.).</summary>
    public PlayerData Data { get; }

    /// <summary>Shorthand for <see cref="Data"/>.<see cref="PlayerData.Type"/>.</summary>
    public PlayerType Type => Data.Type;

    /// <summary>The player's primary entity in this simulation (ship, avatar, etc.).</summary>
    public Entity Entity { get; set; }

    public SimulationPlayer(PlayerData data)
    {
        Data = data;
    }
}
