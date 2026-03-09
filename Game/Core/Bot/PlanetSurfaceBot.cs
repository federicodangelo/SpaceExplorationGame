using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu;

namespace SpaceExplorationGame.Core.Bot;

/// <summary>
/// Actions that the autoplay bot can request from PlanetSurfaceState.
/// </summary>
public enum PlanetSurfaceAction
{
    None,
    TakeOff,
    DisembarkOnFoot,
    DisembarkOnVehicle,
    EnterSettlement,
    BoardShip,
}


/// <summary>
/// Autoplay sub-bot for the planet surface state.
/// Explores the surface, mines rocks, fights pirates, visits settlements, and returns to the ship.
/// </summary>
internal sealed class PlanetSurfaceBot : BotBase
{
    // ── Timing ──────────────────────────────────────────────────────
    private const float SurfaceReturnTimeMin = 30.0f;
    private const float SurfaceReturnTimeMax = 60.0f;
    private const float SurfaceSettleStartTime = 5.0f;
    private const float SurfaceSettleEndTime = 15.0f;

    // ── Navigation ──────────────────────────────────────────────────
    private const float SurfaceWanderDistance = 400f;
    private const float SurfaceEnemyDetectRange = 700f;
    private const float SurfaceRockMineRange = 550f;
    private const float SurfaceEnemyStandoffDistance = 110f;
    private const float SurfaceRockStandoffDistance = 80f;

    // ── State ────────────────────────────────────────────────────────
    private enum SurfaceGoal { Explore, GoToSettlement, GoToShip }
    private SurfaceGoal _surfaceGoal = SurfaceGoal.Explore;

    private enum SurfaceExploreSubGoal { Wander, Enemy, Rock }
    private SurfaceExploreSubGoal _surfaceExploreSubGoal = SurfaceExploreSubGoal.Wander;

    private Vector2 _surfaceWanderTarget;
    private bool _surfaceWanderTargetSet;
    private int _surfaceSettlementsVisited;

    // Pathfinding
    private readonly List<TilePos> _surfacePath = [];
    private TilePos _surfacePathTarget = new(-1, -1);
    private float _surfaceStuckTimer;

    // Randomised per-session timing
    private float _surfaceReturnTime = 40.0f;

    private float _surfaceTimer;
    private float _actionCooldown;
    private bool _starshipMenuWasOpen;

    internal PlanetSurfaceBot(Random rng) : base(rng)
    {
        _surfaceReturnTime = RandRange(SurfaceReturnTimeMin, SurfaceReturnTimeMax);
    }

    internal void Reset()
    {
        _surfaceTimer = 0;
        _actionCooldown = 0;
        _surfaceReturnTime = RandRange(SurfaceReturnTimeMin, SurfaceReturnTimeMax);
        _surfaceGoal = SurfaceGoal.Explore;
        _surfaceWanderTargetSet = false;
        _surfacePath.Clear();
        _surfacePathTarget = new TilePos(-1, -1);
        _surfaceStuckTimer = 0;
        _surfaceSettlementsVisited = 0;
        _starshipMenuWasOpen = false;
        _statusGoal = "";
        _statusAction = "";
    }

    /// <summary>
    /// Called by the solar bot when the player lands on a new planet.
    /// Resets per-planet exploration state.
    /// </summary>
    internal void OnNewPlanetLanding()
    {
        _surfaceSettlementsVisited = 0;
    }

    /// <summary>
    /// Explores the planet surface.
    /// Returns true if the bot consumed input. Populates <paramref name="action"/> with any
    /// state-level action to trigger.
    /// </summary>
    internal bool Update(
        Game game,
        PlanetSurfaceSimulation sim,
        SimulationPlayer simPlayer,
        StarshipMenuOverlay starshipMenu,
        InGameMenuOverlay inGameMenu,
        bool playerInsideShip,
        bool inVehicle,
        bool anyOverlayOpen,
        out PlanetSurfaceAction action)
    {
        action = PlanetSurfaceAction.None;
        if (!Enabled) return false;

        float dt = game.DeltaTime;
        _surfaceTimer += dt;
        _actionCooldown = Math.Max(0, _actionCooldown - dt);

        if (!sim.LocalPlayerDead && sim.EcsWorld.IsAlive(simPlayer.Entity) && anyOverlayOpen)
        {
            ref var blockedInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
            blockedInput = AvatarInputComponent.Default();
            _statusAction = "Waiting for overlay to close...";
        }

        // ── Close in-game menu if open ──
        if (inGameMenu.IsOpen)
        {
            inGameMenu.Close();
            return true;
        }

        // ── Handle starship menu ──
        if (starshipMenu.IsOpen)
        {
            if (!_starshipMenuWasOpen)
            {
                _actionCooldown = Math.Max(_actionCooldown, ActionDelay);
                _starshipMenuWasOpen = true;
            }

            bool willTakeOff = _surfaceTimer > _surfaceReturnTime + 2.0f || _surfaceGoal == SurfaceGoal.GoToShip;
            StarshipMenuOption preSelect = willTakeOff ? StarshipMenuOption.TakeOff : StarshipMenuOption.DisembarkOnFoot;
            int preSelectIdx = starshipMenu.FindMenuOptionIndex(preSelect);
            if (preSelectIdx >= 0) starshipMenu.MenuSelectedIndex = preSelectIdx;

            if (_actionCooldown <= 0)
            {
                if (willTakeOff)
                {
                    action = PlanetSurfaceAction.TakeOff;
                    _statusAction = "Taking off";
                    _surfaceTimer = 0;
                    _surfaceReturnTime = RandRange(SurfaceReturnTimeMin, SurfaceReturnTimeMax);
                    _surfaceGoal = SurfaceGoal.Explore;
                    _surfaceWanderTargetSet = false;
                }
                else
                {
                    action = PlanetSurfaceAction.DisembarkOnFoot;
                    _statusAction = "Disembarking on foot";
                    _starshipMenuWasOpen = false;
                }
            }
            else
            {
                _statusAction = $"Ship menu open, waiting ({_actionCooldown:F1}s)...";
            }
            return true;
        }
        else
        {
            _starshipMenuWasOpen = false;
        }

        // ── Player dead — wait for respawn ──
        if (sim.LocalPlayerDead || !sim.EcsWorld.IsAlive(simPlayer.Entity))
            return true;

        // ── Inside ship waiting for menu ──
        if (playerInsideShip)
            return true;

        // ── Stop and close if any overlay is open ──
        if (anyOverlayOpen)
        {
            if (sim.EcsWorld.IsAlive(simPlayer.Entity))
            {
                ref var frozenInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
                frozenInput.DesiredVelocity = Vector2.Zero;
                frozenInput.Shoot = false;
            }
            _statusAction = "Waiting for overlay to close...";
            return true;
        }

        var world = sim.EcsWorld;
        ref var avatarTf = ref world.Get<Transform>(simPlayer.Entity);
        Vector2 avatarPos = avatarTf.Position;

        // ── Decide goal ──
        if (_surfaceTimer > _surfaceReturnTime)
            _surfaceGoal = SurfaceGoal.GoToShip;
        else if (_surfaceSettlementsVisited < sim.SurfaceData.Settlements.Count &&
                 _surfaceTimer > SurfaceSettleStartTime && _surfaceTimer < SurfaceSettleEndTime)
            _surfaceGoal = SurfaceGoal.GoToSettlement;
        else
            _surfaceGoal = SurfaceGoal.Explore;

        float _surfaceRemaining = Math.Max(0f, _surfaceReturnTime - _surfaceTimer);
        _statusGoal = _surfaceGoal switch
        {
            SurfaceGoal.Explore => _surfaceExploreSubGoal switch
            {
                SurfaceExploreSubGoal.Enemy => $"HUNTING ENEMY ({_surfaceRemaining:F0}s to return)",
                SurfaceExploreSubGoal.Rock => $"MINING ROCK ({_surfaceRemaining:F0}s to return)",
                _ => $"EXPLORING SURFACE ({_surfaceRemaining:F0}s to return)"
            },
            SurfaceGoal.GoToSettlement => $"HEADING TO SETTLEMENT ({_surfaceRemaining:F0}s to return)",
            SurfaceGoal.GoToShip => "RETURNING TO SHIP",
            _ => "PLANET SURFACE"
        };

        // ── Check proximity interactions ──
        if (_actionCooldown <= 0)
        {
            if (sim.LocalNearSettlement != null &&
                _surfaceGoal == SurfaceGoal.GoToSettlement &&
                sim.LocalNearSettlement.Index == _surfaceSettlementsVisited)
            {
                action = PlanetSurfaceAction.EnterSettlement;
                _statusAction = "Entering settlement";
                _actionCooldown = ActionDelay;
                _surfaceGoal = SurfaceGoal.GoToShip;
                _surfaceSettlementsVisited++;
                return true;
            }

            if (sim.LocalNearShip && _surfaceGoal == SurfaceGoal.GoToShip)
            {
                action = PlanetSurfaceAction.BoardShip;
                _statusAction = "Boarding ship";
                _actionCooldown = ActionDelay;
                return true;
            }
        }

        // ── Write movement input via pathfinding ──
        Vector2 targetPos = GetTargetPosition(sim, avatarPos);
        int goalTileX = (int)(targetPos.X / WindowConfig.TileSize);
        int goalTileY = (int)(targetPos.Y / WindowConfig.TileSize);
        TilePos surfaceGoalTile = new(goalTileX, goalTileY);

        int fromTileX = (int)(avatarPos.X / WindowConfig.TileSize);
        int fromTileY = (int)(avatarPos.Y / WindowConfig.TileSize);
        TilePos surfaceFromTile = new(fromTileX, fromTileY);

        if (surfaceGoalTile != _surfacePathTarget)
        {
            _surfacePath.Clear();
            var newPath = FindSurfacePath(sim.SurfaceData, surfaceFromTile, surfaceGoalTile);
            if (newPath.Count == 0 && _surfaceGoal == SurfaceGoal.GoToShip)
            {
                Console.WriteLine($"[Bot] Warning: no path found from {surfaceFromTile} to ship at {surfaceGoalTile}. Origin tile blocked: {SurfaceTerrainRules.IsBlockedForTraversal(sim.SurfaceData.Tiles[surfaceFromTile.X, surfaceFromTile.Y])}, Goal tile blocked: {SurfaceTerrainRules.IsBlockedForTraversal(sim.SurfaceData.Tiles[surfaceGoalTile.X, surfaceGoalTile.Y])}");
            }
            _surfacePath.AddRange(newPath);
            _surfacePathTarget = surfaceGoalTile;
            _surfaceStuckTimer = 0;
        }

        while (_surfacePath.Count > 0)
        {
            Vector2 wpWorld = TilePosToWorld(_surfacePath[0]);
            if (Vector2.Distance(avatarPos, wpWorld) < WindowConfig.TileSize * 0.2f)
            {
                _surfacePath.RemoveAt(0);
                _surfaceStuckTimer = 0;
            }
            else
                break;
        }

        ref var avatarInput = ref world.Get<AvatarInputComponent>(simPlayer.Entity);

        Vector2 nextTarget = _surfacePath.Count > 0 ? TilePosToWorld(_surfacePath[0]) : targetPos;
        Vector2 toTarget = nextTarget - avatarPos;
        float dist = toTarget.Length();

        // Stuck detection
        _surfaceStuckTimer += game.DeltaTime;
        if (_surfaceStuckTimer > StuckTimeout)
        {
            _surfacePath.Clear();
            _surfacePathTarget = new TilePos(-1, -1);
            _surfaceWanderTargetSet = false;
            _surfaceStuckTimer = 0;
            if (_surfaceGoal == SurfaceGoal.GoToSettlement)
            {
                _surfaceSettlementsVisited++;
                _statusAction = "Got stuck, skipping to next settlement";
            }
            else
            {
                _statusAction = "Got stuck, recalculating path";
            }
        }

        bool inCombatOrMining = _surfaceGoal == SurfaceGoal.Explore &&
            (_surfaceExploreSubGoal == SurfaceExploreSubGoal.Enemy ||
             _surfaceExploreSubGoal == SurfaceExploreSubGoal.Rock);
        float standoffDist = _surfaceExploreSubGoal == SurfaceExploreSubGoal.Enemy
            ? SurfaceEnemyStandoffDistance : SurfaceRockStandoffDistance;
        float distToActualTarget = Vector2.Distance(avatarPos, targetPos);
        bool withinStandoff = inCombatOrMining && distToActualTarget <= standoffDist;

        if (withinStandoff)
        {
            Vector2 toActual = targetPos - avatarPos;
            Vector2 aimDir = toActual.Length() > 0.1f ? Vector2.Normalize(toActual) : Vector2.UnitX;
            avatarInput.DesiredVelocity = Vector2.Zero;
            avatarInput.Shoot = true;
            avatarInput.AimDirection = aimDir;
            _statusAction = _surfaceExploreSubGoal == SurfaceExploreSubGoal.Enemy
                ? $"Shooting enemy (dist:{distToActualTarget:F0})"
                : $"Mining rock (dist:{distToActualTarget:F0})";
            _surfacePath.Clear();
            _surfacePathTarget = new TilePos(-1, -1);
        }
        else if (dist >= WindowConfig.TileSize * 0.2f)
        {
            Vector2 dir = toTarget / dist;
            float speed = game.Player.AvatarWalkSpeed;
            avatarInput.DesiredVelocity = dir * speed;
            _statusAction = _surfacePath.Count > 0
                ? $"Path [{_surfacePath.Count} steps] dist:{dist:F0}"
                : $"Walking (dist: {dist:F0})";

            if (inCombatOrMining)
            {
                Vector2 toActual = targetPos - avatarPos;
                avatarInput.AimDirection = toActual.Length() > 0.1f ? Vector2.Normalize(toActual) : dir;
                avatarInput.Shoot = true;
            }
            else
            {
                avatarInput.AimDirection = dir;
                avatarInput.Shoot = false;
            }
        }
        else
        {
            avatarInput.DesiredVelocity = Vector2.Zero;
            avatarInput.Shoot = false;
            _statusAction = "Looking around...";
            _surfaceWanderTargetSet = false;
            _surfacePath.Clear();
            _surfacePathTarget = new TilePos(-1, -1);
        }

        return true;
    }

    private Vector2 GetTargetPosition(PlanetSurfaceSimulation sim, Vector2 avatarPos)
    {
        var world = sim.EcsWorld;

        switch (_surfaceGoal)
        {
            case SurfaceGoal.GoToShip:
                if (world.IsAlive(sim.LocalShipEntity))
                    return world.Get<Transform>(sim.LocalShipEntity).Position;
                break;

            case SurfaceGoal.GoToSettlement:
                if (sim.SurfaceData.Settlements.Count > 0)
                {
                    var settlement = sim.SurfaceData.Settlements[_surfaceSettlementsVisited % sim.SurfaceData.Settlements.Count];
                    float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * WindowConfig.TileSize;
                    float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * WindowConfig.TileSize;
                    return new Vector2(sx, sy);
                }
                break;
        }

        // Explore: priority 1 — nearby pirate enemy
        if (FindNearestSurfacePirate(sim, avatarPos, SurfaceEnemyDetectRange) is { } enemyPos)
        {
            if (_surfaceExploreSubGoal != SurfaceExploreSubGoal.Enemy)
                _surfaceWanderTargetSet = false;
            _surfaceExploreSubGoal = SurfaceExploreSubGoal.Enemy;
            return enemyPos;
        }

        // Explore: priority 2 — nearby mineable rock
        if (FindNearestSurfaceRock(sim, avatarPos, SurfaceRockMineRange) is { } rockPos)
        {
            if (_surfaceExploreSubGoal != SurfaceExploreSubGoal.Rock)
                _surfaceWanderTargetSet = false;
            _surfaceExploreSubGoal = SurfaceExploreSubGoal.Rock;
            return rockPos;
        }

        // Explore: fallback — random wander
        if (_surfaceExploreSubGoal != SurfaceExploreSubGoal.Wander)
            _surfaceWanderTargetSet = false;
        _surfaceExploreSubGoal = SurfaceExploreSubGoal.Wander;
        if (!_surfaceWanderTargetSet)
        {
            float angle = _rng.NextSingle() * MathF.PI * 2f;
            _surfaceWanderTarget = avatarPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * SurfaceWanderDistance;
            _surfaceWanderTargetSet = true;
        }
        return _surfaceWanderTarget;
    }

    private static Vector2? FindNearestSurfacePirate(PlanetSurfaceSimulation sim, Vector2 avatarPos, float maxRange)
    {
        Vector2? best = null;
        float bestDistSq = maxRange * maxRange;
        var query = new QueryDescription().WithAll<Transform, SurfaceAI, Health>();
        sim.EcsWorld.Query(in query, (ref Transform tf, ref SurfaceAI ai, ref Health hp) =>
        {
            if (hp.IsDead) return;
            if (ai.Config.Faction != Faction.Pirate) return;
            float dSq = Vector2.DistanceSquared(tf.Position, avatarPos);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = tf.Position;
            }
        });
        return best;
    }

    private static Vector2? FindNearestSurfaceRock(PlanetSurfaceSimulation sim, Vector2 avatarPos, float maxRange)
    {
        Vector2? best = null;
        float bestDistSq = maxRange * maxRange;
        var query = new QueryDescription().WithAll<Transform, AsteroidField, Health>();
        sim.EcsWorld.Query(in query, (ref Transform tf, ref Health hp) =>
        {
            if (hp.IsDead) return;
            float dSq = Vector2.DistanceSquared(tf.Position, avatarPos);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = tf.Position;
            }
        });
        return best;
    }

    private static List<TilePos> FindSurfacePath(PlanetSurfaceData surface, TilePos from, TilePos to,
        int maxNodes = 4000) =>
        AStarTilePath(surface.Width, surface.Height,
            (x, y) => SurfaceTerrainRules.IsTraversable(surface.Tiles[x, y]),
            from, to, maxNodes);

}
