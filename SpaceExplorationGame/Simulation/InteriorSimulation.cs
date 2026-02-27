using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation.Base;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Simulation for a walkable interior (space station or settlement).
/// Manages player avatar movement, NPC/interactable proximity, and tile collision.
/// Contains NO rendering or audio code.
/// </summary>
public class InteriorSimulation : SimulationBase
{
    // ── Data ────────────────────────────────────────────────────────
    public InteriorOrigin Origin { get; }
    public StarSystemData StarSystem { get; }
    public SpaceStationData? Station { get; }
    public PlanetData? Planet { get; }
    public SettlementData? Settlement { get; }
    public InteriorData Interior { get; private set; } = null!;

    // ── Proximity ───────────────────────────────────────────────────
    public InteriorNpc? NearestNpc { get; private set; }
    public InteriorInteractable? NearestInteractable { get; private set; }
    private const float InteractionRadius = 1.5f; // in tiles

    // ── ECS Systems ─────────────────────────────────────────────────
    private DependentEntityCleanupSystem _dependentEntityCleanupSystem = null!;
    private VelocitySystem _velocitySystem = null!;

    public InteriorSimulation(Game game, InteriorOrigin origin, StarSystemData starSystem,
        SpaceStationData? station = null, PlanetData? planet = null, SettlementData? settlement = null,
        ISimulation? parent = null)
        : base(game, parent)
    {
        Origin = origin;
        StarSystem = starSystem;
        Station = station;
        Planet = planet;
        Settlement = settlement;
    }

    public override void Create()
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

    public override void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;

        _dependentEntityCleanupSystem.Update(in dt);
        _velocitySystem.Update(in dt);

        // Update proximity for first player
        UpdateProximity();
    }

    protected override Entity CreatePlayerEntity(PlayerData player, AddContext ctx)
    {
        float spawnX = Interior.SpawnPoint.X * GameConfig.TileSize;
        float spawnY = Interior.SpawnPoint.Y * GameConfig.TileSize;

        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld, spawnX, spawnY, player.AvatarWalkSpeed, canMoveTo: pos =>
        {
            int tileX = (int)(pos.X / GameConfig.TileSize);
            int tileY = (int)(pos.Y / GameConfig.TileSize);
            return tileX >= 0 && tileX < Interior.Width &&
                   tileY >= 0 && tileY < Interior.Height &&
                   InteriorGenerator.IsWalkable(Interior.Tiles[tileX, tileY]);
        });

        // Notify mission system
        if (Origin == InteriorOrigin.Settlement && Planet != null)
            player.NotifySettlementEntered(StarSystem.Index, Planet.Index);

        return avatarEntity;
    }

    // ── Proximity ───────────────────────────────────────────────────

    private void UpdateProximity()
    {
        NearestNpc = null;
        NearestInteractable = null;

        if (LocalPlayer is not { } local) return;
        if (!EcsWorld.IsAlive(local.Entity)) return;

        ref var avatarTf = ref EcsWorld.Get<Transform>(local.Entity);
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
