using Arch.Core;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Per-player state tracked by combat simulations (solar system, planet surface).
/// Each player in a simulation gets their own instance.
/// </summary>
public class CombatPlayerState
{
    public bool Dead;
    public float RespawnTimer;
    public string? CombatMessage;
    public float CombatMessageTimer;
    public float CombatMusicTimer;
}

/// <summary>
/// Per-player state for <see cref="SolarSystemSimulation"/>.
/// Extends combat state with proximity detection and mining tracking.
/// </summary>
public class SolarPlayerState : CombatPlayerState
{
    public int NearbyPlanetIndex = -1;
    public int NearbySpaceStationIndex = -1;
    public int NearbyMoonPlanetIndex = -1;
    public int NearbyMoonIndex = -1;

    public Entity LastHitAsteroid;
    public float MiningHudTimer;
    public string? MiningMessage;
    public float MiningMessageTimer;

    public int RespawnSpaceStationIndex = -1;
}

/// <summary>
/// Per-player state for <see cref="PlanetSurfaceSimulation"/>.
/// Extends combat state with vehicle/ship ownership and surface proximity.
/// </summary>
public class SurfacePlayerState : CombatPlayerState
{
    public Entity ShipEntity;
    public Entity VehicleEntity;
    public bool VehicleDeployed;

    public SettlementData? NearSettlement;
    public bool NearShip;
    public bool NearVehicle;
}

/// <summary>
/// Per-player state for <see cref="InteriorSimulation"/>.
/// Tracks proximity to NPCs, interactables, and the landing pad ship.
/// </summary>
public class InteriorPlayerState
{
    public InteriorNpc? NearestNpc;
    public InteriorInteractable? NearestInteractable;
    public bool NearShip;
}
