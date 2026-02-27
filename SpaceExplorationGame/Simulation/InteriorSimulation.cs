using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Simulation for a walkable interior (space station or settlement).
/// Manages player avatar movement, NPC/interactable proximity, and tile collision.
/// Contains NO rendering or audio code.
/// </summary>
public class InteriorSimulation : ISimulation
{
    // ── ECS ─────────────────────────────────────────────────────────
    public World EcsWorld { get; }

    // ── Data ────────────────────────────────────────────────────────
    public InteriorOrigin Origin { get; }
    public StarSystemData StarSystem { get; }
    public SpaceStationData? Station { get; }
    public PlanetData? Planet { get; }
    public SettlementData? Settlement { get; }
    public InteriorData Interior { get; private set; } = null!;

    // ── Players ─────────────────────────────────────────────────────
    private readonly List<SimulationPlayer> _players = [];
    public IReadOnlyList<SimulationPlayer> Players => _players;
    public bool HasPlayers => _players.Count > 0;

    // ── Proximity ───────────────────────────────────────────────────
    public InteriorNpc? NearestNpc { get; private set; }
    public InteriorInteractable? NearestInteractable { get; private set; }
    private const float InteractionRadius = 1.5f; // in tiles

    // ── ECS Systems ─────────────────────────────────────────────────
    private DependentEntityCleanupSystem _dependentEntityCleanupSystem = null!;
    private VelocitySystem _velocitySystem = null!;

    private const float BaseAvatarSpeed = 200f;

    private readonly Game _game;

    public InteriorSimulation(Game game, InteriorOrigin origin, StarSystemData starSystem,
        SpaceStationData? station = null, PlanetData? planet = null, SettlementData? settlement = null)
    {
        EcsWorld = World.Create();
        _game = game;
        Origin = origin;
        StarSystem = starSystem;
        Station = station;
        Planet = planet;
        Settlement = settlement;
    }

    public void Create()
    {
        Interior = Origin switch
        {
            InteriorOrigin.Station => _game.WorldGenerator.GenerateStationInterior(_game.Seeds, StarSystem, Station),
            InteriorOrigin.Settlement => _game.WorldGenerator.GenerateSettlementInterior(_game.Seeds, StarSystem, Planet, Settlement),
            _ => _game.WorldGenerator.GenerateStationInterior(_game.Seeds, StarSystem, Station)
        };

        // Initialize ECS systems
        _dependentEntityCleanupSystem = new DependentEntityCleanupSystem(EcsWorld);
        _dependentEntityCleanupSystem.Initialize();

        _velocitySystem = new VelocitySystem(EcsWorld);
        _velocitySystem.Initialize();
    }

    public void Destroy()
    {
        _players.Clear();
        EcsWorld.Dispose();
    }

    public void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;

        _dependentEntityCleanupSystem.Update(in dt);
        _velocitySystem.Update(in dt);

        // Update proximity for first player
        UpdateProximity();
    }

    public SimulationPlayer AddPlayer(PlayerData player)
    {
        float spawnX = Interior.SpawnPoint.X * GameConfig.TileSize;
        float spawnY = Interior.SpawnPoint.Y * GameConfig.TileSize;

        float avatarSpeed = BaseAvatarSpeed + player.GetCombinedAvatarStats().WalkSpeed;

        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld, spawnX, spawnY, avatarSpeed);
        ref var playerVelocity = ref EcsWorld.Get<Velocity>(avatarEntity);
        playerVelocity.CanMoveTo = newPos =>
        {
            int tileX = (int)(newPos.X / GameConfig.TileSize);
            int tileY = (int)(newPos.Y / GameConfig.TileSize);
            return tileX >= 0 && tileX < Interior.Width &&
                   tileY >= 0 && tileY < Interior.Height &&
                   InteriorGenerator.IsWalkable(Interior.Tiles[tileX, tileY]);
        };

        // Notify mission system
        if (Origin == InteriorOrigin.Settlement && Planet != null)
            player.NotifySettlementEntered(StarSystem.Index, Planet.Index);

        var simPlayer = new SimulationPlayer(player) { Entity = avatarEntity };
        _players.Add(simPlayer);
        return simPlayer;
    }

    public void RemovePlayer(SimulationPlayer player)
    {
        if (EcsWorld.IsAlive(player.Entity))
            EcsWorld.Destroy(player.Entity);
        _players.Remove(player);
    }

    // ── Proximity ───────────────────────────────────────────────────

    private void UpdateProximity()
    {
        NearestNpc = null;
        NearestInteractable = null;

        if (_players.Count == 0) return;
        var player = _players[0];
        if (!EcsWorld.IsAlive(player.Entity)) return;

        ref var avatarTf = ref EcsWorld.Get<Transform>(player.Entity);
        float playerTileX = avatarTf.Position.X / GameConfig.TileSize;
        float playerTileY = avatarTf.Position.Y / GameConfig.TileSize;

        float nearestNpcDist = float.MaxValue;
        foreach (var npc in Interior.Npcs)
        {
            float dx = npc.TilePos.X - playerTileX;
            float dy = npc.TilePos.Y - playerTileY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < InteractionRadius && dist < nearestNpcDist)
            {
                nearestNpcDist = dist;
                NearestNpc = npc;
            }
        }

        float nearestIntDist = float.MaxValue;
        foreach (var interactable in Interior.Interactables)
        {
            float dx = interactable.TilePos.X - playerTileX;
            float dy = interactable.TilePos.Y - playerTileY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < InteractionRadius && dist < nearestIntDist)
            {
                nearestIntDist = dist;
                NearestInteractable = interactable;
            }
        }
    }
}
