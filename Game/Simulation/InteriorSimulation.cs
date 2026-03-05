using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
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
    public SpaceStationData? SpaceStation { get; }
    public PlanetData? Planet { get; }
    public SettlementData? Settlement { get; }
    public InteriorData Interior { get; private set; } = null!;

    // ── Per-player state ─────────────────────────────────────────────
    private readonly Dictionary<SimulationPlayer, InteriorPlayerState> _interiorStates = new();

    /// <summary>Get the interior-specific per-player state.</summary>
    public InteriorPlayerState GetInteriorState(SimulationPlayer player) => _interiorStates[player];

    private InteriorPlayerState? LocalInteriorState =>
        LocalPlayer != null && _interiorStates.TryGetValue(LocalPlayer, out var s) ? s : null;

    // ── Proximity (convenience, delegates to local player) ───────────
    public InteriorNpc? NearestNpc => LocalInteriorState?.NearestNpc;
    public InteriorInteractable? NearestInteractable => LocalInteriorState?.NearestInteractable;
    /// <summary>True when the local player avatar is close enough to board the landed ship.</summary>
    public bool NearShip => LocalInteriorState?.NearShip ?? false;
    private const float InteractionRadius = 1.5f; // in tiles

    // ── ECS Systems ─────────────────────────────────────────────────
    private DependentEntityCleanupSystem _dependentEntityCleanupSystem = null!;
    private VelocitySystem _velocitySystem = null!;
    private AvatarSystem _avatarSystem = null!;



    public InteriorSimulation(Game game, InteriorOrigin origin, StarSystemData starSystem,
        SpaceStationData? spaceStation = null, PlanetData? planet = null, SettlementData? settlement = null,
        ISimulation? parent = null)
        : base(game, parent)
    {
        Origin = origin;
        StarSystem = starSystem;
        SpaceStation = spaceStation;
        Planet = planet;
        Settlement = settlement;
    }

    public override void Create()
    {
        Interior = Origin switch
        {
            InteriorOrigin.SpaceStation => _game.UniverseGenerator.GenerateStationInterior(StarSystem, SpaceStation),
            InteriorOrigin.Settlement => _game.UniverseGenerator.GenerateSettlementInterior(StarSystem, Planet, Settlement),
            _ => _game.UniverseGenerator.GenerateStationInterior(StarSystem, SpaceStation)
        };

        // Initialize ECS systems
        _dependentEntityCleanupSystem = new DependentEntityCleanupSystem(EcsWorld);
        _dependentEntityCleanupSystem.Initialize();

        _velocitySystem = new VelocitySystem(EcsWorld);
        _velocitySystem.Initialize();

        _avatarSystem = new AvatarSystem(EcsWorld);
        _avatarSystem.Initialize();
    }

    public override void Destroy()
    {
        _interiorStates.Clear();
        base.Destroy();
    }

    protected override void OnPlayerAdded(SimulationPlayer player)
    {
        _interiorStates[player] = new InteriorPlayerState();
    }

    protected override void OnPlayerRemoved(SimulationPlayer player)
    {
        _interiorStates.Remove(player);
    }

    public override void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;
        var t = _debugTimer;
        t.Begin();

        t.Time("Cleanup", () => _dependentEntityCleanupSystem.Update(in dt));
        t.Time("Avatars", () => _avatarSystem.Update(in dt));
        t.Time("Physics", () => _velocitySystem.Update(in dt));
        t.Time("Proximity", UpdateProximity);
    }

    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _debugInfo.Begin();
        _debugInfo.Add($"Origin: {Origin}  NPCs: {Interior.Npcs.Count}");
        _debugInfo.Add($"Players: {Players.Count}");
        return _debugInfo.Entries;
    }

    protected override Entity CreatePlayerEntity(PlayerData player, AddContext ctx)
    {
        float spawnX = ctx.LandingTileX * WindowConfig.TileSize;
        float spawnY = ctx.LandingTileY * WindowConfig.TileSize;

        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld, spawnX, spawnY, player.AvatarWalkSpeed, canMoveTo: pos =>
        {
            int tileX = (int)(pos.X / WindowConfig.TileSize);
            int tileY = (int)(pos.Y / WindowConfig.TileSize);
            return tileX >= 0 && tileX < Interior.Width &&
                   tileY >= 0 && tileY < Interior.Height &&
                   InteriorGenerator.IsWalkable(Interior.Tiles[tileX, tileY]);
        });

        // Notify mission system
        if (Origin == InteriorOrigin.Settlement && Planet != null)
            player.Missions.NotifySettlementEntered(StarSystem.Index, Planet.Index);

        return avatarEntity;
    }

    // ── Proximity ───────────────────────────────────────────────────

    private void UpdateProximity()
    {
        foreach (var player in Players)
        {
            var ps = GetInteriorState(player);
            ps.NearestNpc = null;
            ps.NearestInteractable = null;
            ps.NearShip = false;

            if (!EcsWorld.IsAlive(player.Entity)) continue;

            ref var avatarTf = ref EcsWorld.Get<Transform>(player.Entity);
            float playerTileX = avatarTf.Position.X / WindowConfig.TileSize;
            float playerTileY = avatarTf.Position.Y / WindowConfig.TileSize;

            float nearestNpcDist = float.MaxValue;
            foreach (var npc in Interior.Npcs)
            {
                float dx = npc.TilePos.X - playerTileX;
                float dy = npc.TilePos.Y - playerTileY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < InteractionRadius && dist < nearestNpcDist)
                {
                    nearestNpcDist = dist;
                    ps.NearestNpc = npc;
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
                    ps.NearestInteractable = interactable;
                }
            }

            // Check proximity to the ship on the landing pad
            if (Interior.LandingPadTilePos.HasValue)
            {
                float padX = Interior.LandingPadTilePos.Value.X;
                float padY = Interior.LandingPadTilePos.Value.Y;
                float dx = padX - playerTileX;
                float dy = padY - playerTileY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < InteractionRadius)
                    ps.NearShip = true;
            }
        }
    }
}
