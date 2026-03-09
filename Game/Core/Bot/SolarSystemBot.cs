using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Map;
using SpaceExplorationGame.UI.Overlays.Menu;

namespace SpaceExplorationGame.Core.Bot;

/// <summary>
/// Autoplay sub-bot for the solar system state.
/// Manages a randomised visit plan (stations + planets), navigates the ship,
/// handles overlays, and triggers FTL jumps to a random reachable system.
/// </summary>
internal sealed class SolarSystemBot : BotBase
{
    // ── Timing ──────────────────────────────────────────────────────
    private const float SolarSystemFtlDelay = 15.0f;

    // ── Navigation ──────────────────────────────────────────────────
    private const float ShipHoldRadius = 150f;
    private const float ShipBrakingMargin = 1.8f;
    private const float EnemyDetectRange = 800f;

    // ── State ────────────────────────────────────────────────────────
    private enum SolarGoal { FlyToStation, FlyToPlanet, Explore, FTLJump }
    private readonly record struct SolarVisit(SolarGoal Goal, int TargetIndex);

    private SolarGoal _solarGoal = SolarGoal.FlyToStation;
    private int _solarTargetIndex;
    private int _systemsVisited;
    private readonly List<SolarVisit> _solarVisitPlan = [];
    private int _solarVisitPlanIndex;
    private bool _solarPlanBuilt;

    private float _solarSystemTimer;
    private float _actionCooldown;

    internal SolarSystemBot(Random rng) : base(rng) { }

    internal void Reset()
    {
        _solarSystemTimer = 0;
        _actionCooldown = 0;
        _solarGoal = SolarGoal.FlyToStation;
        _solarTargetIndex = 0;
        _systemsVisited = 0;
        _solarVisitPlan.Clear();
        _solarVisitPlanIndex = 0;
        _solarPlanBuilt = false;
        _statusGoal = "";
        _statusAction = "";
    }

    /// <summary>
    /// Flies the ship around the solar system, docks at stations, lands on planets, and jumps to new systems.
    /// <paramref name="onPlanetLanded"/> is invoked (before the landing delegate) so the coordinator can
    /// notify other sub-bots of the landing.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    internal bool Update(
        Game game,
        SolarSystemSimulation sim,
        SimulationPlayer simPlayer,
        SpaceStationOverlay stationOverlay,
        PlanetLandingOverlay landingOverlay,
        GalaxyMapOverlay galaxyMapOverlay,
        InGameMenuOverlay inGameMenuOverlay,
        StarSystemData starSystem,
        bool anyOverlayOpen,
        Action<Game, SpaceStationData> beginDocking,
        Action<Game, LandingSelectionRequest> beginLanding,
        Action onPlanetLanded)
    {
        if (!Enabled) return false;

        float dt = game.DeltaTime;
        _solarSystemTimer += dt;
        _actionCooldown = Math.Max(0, _actionCooldown - dt);

        // ── Zero ship input whenever an overlay is blocking the player ──
        if (!sim.LocalPlayerDead && sim.EcsWorld.IsAlive(simPlayer.Entity) && anyOverlayOpen)
        {
            ref var blockedInput = ref sim.EcsWorld.Get<ShipInputComponent>(simPlayer.Entity);
            blockedInput = ShipInputComponent.Default();
            _statusAction = "Waiting for overlay to close...";
        }

        // ── Handle open overlays first ──
        if (inGameMenuOverlay.IsOpen)
        {
            inGameMenuOverlay.Close();
            return true;
        }

        // ── Decide goal first (needed so overlay handlers see an up-to-date goal) ──
        DecideGoal(sim);

        if (stationOverlay.IsOpen)
            return HandleStationOverlay(game, sim, stationOverlay, beginDocking);

        if (landingOverlay.IsOpen)
        {
            // Close the overlay — we handle landing directly in the proximity check
            landingOverlay.Close();
            return true;
        }

        if (galaxyMapOverlay.IsOpen)
        {
            // Close it — we handle FTL directly
            galaxyMapOverlay.Close(game);
            return true;
        }

        // ── Player dead — wait for respawn ──
        if (sim.LocalPlayerDead || !sim.EcsWorld.IsAlive(simPlayer.Entity))
            return true;

        var world = sim.EcsWorld;
        ref var shipTransform = ref world.Get<Transform>(simPlayer.Entity);
        ref var shipVelocity = ref world.Get<Velocity>(simPlayer.Entity);
        ref var shipInput = ref world.Get<ShipInputComponent>(simPlayer.Entity);
        ref var shipComp = ref world.Get<ShipComponent>(simPlayer.Entity);

        Vector2 shipPos = shipTransform.Position;

        // ── Update status ──
        _statusGoal = _solarGoal switch
        {
            SolarGoal.FlyToStation => $"FLY TO STATION [{_solarVisitPlanIndex + 1}/{_solarVisitPlan.Count}]",
            SolarGoal.FlyToPlanet => $"FLY TO PLANET  [{_solarVisitPlanIndex + 1}/{_solarVisitPlan.Count}]",
            SolarGoal.Explore => $"EXPLORING SYSTEM ({Math.Max(0f, SolarSystemFtlDelay - _solarSystemTimer):F0}s to FTL)",
            SolarGoal.FTLJump => "PREPARING FTL JUMP",
            _ => "SOLAR SYSTEM"
        };

        // ── Fire at nearby enemies ──
        bool enemyNearby = HasNearbyEnemy(sim, shipPos, EnemyDetectRange);
        shipInput.Shoot = enemyNearby;
        if (enemyNearby) _statusAction = "Firing at enemy!";

        // ── Check proximity interactions ──
        float shipSpeed = shipVelocity.Linear.Length();
        bool shipStopped = shipSpeed < StoppedSpeed;

        if (_actionCooldown <= 0)
        {
            if (sim.LocalNearbySpaceStationIndex >= 0 &&
                _solarGoal == SolarGoal.FlyToStation &&
                _solarTargetIndex == sim.LocalNearbySpaceStationIndex)
            {
                if (!shipStopped)
                {
                    _statusAction = $"Stopping before dock (spd:{shipSpeed:F0})";
                }
                else
                {
                    _actionCooldown = ActionDelay;
                    _statusAction = "Opening station overlay";
                    stationOverlay.Open(starSystem, sim.SpaceStations[sim.LocalNearbySpaceStationIndex], game);
                    return true;
                }
            }

            if (sim.LocalNearbyPlanetIndex >= 0 &&
                _solarGoal == SolarGoal.FlyToPlanet &&
                sim.Planets[sim.LocalNearbyPlanetIndex].HasSolidSurface &&
                _solarTargetIndex == sim.LocalNearbyPlanetIndex)
            {
                if (!shipStopped)
                {
                    _statusAction = $"Stopping before landing (spd:{shipSpeed:F0})";
                }
                else
                {
                    _actionCooldown = ActionDelay;
                    var planet = sim.Planets[sim.LocalNearbyPlanetIndex];
                    int centerTile = WorldConfig.PlanetSurfaceWidth / 2;
                    var landing = new LandingSelectionRequest(
                        starSystem, planet,
                        centerTile, centerTile,
                        IsMoon: false, MoonPlanetIndex: -1, MoonIndex: -1);
                    _statusAction = "Landing on planet";
                    onPlanetLanded();
                    beginLanding(game, landing);
                    _solarVisitPlanIndex++;
                    return true;
                }
            }
        }

        // ── Navigate toward goal ──
        Vector2 targetPos = GetTargetPosition(sim, starSystem, game);
        NavigateShipToTarget(ref shipInput, ref shipVelocity, ref shipTransform, ref shipComp,
            targetPos, shipPos, dt, enemyNearby);

        // ── FTL jump ──
        if (_solarGoal == SolarGoal.FTLJump && _actionCooldown <= 0)
        {
            _statusAction = "Jumping to new system!";
            TryFTLJump(game);
        }

        return true;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private void BuildVisitPlan(SolarSystemSimulation sim)
    {
        _solarVisitPlan.Clear();
        _solarVisitPlanIndex = 0;

        for (int i = 0; i < sim.SpaceStations.Count; i++)
            _solarVisitPlan.Add(new SolarVisit(SolarGoal.FlyToStation, i));

        foreach (var planet in sim.Planets.Where(p => p.HasSolidSurface))
            _solarVisitPlan.Add(new SolarVisit(SolarGoal.FlyToPlanet, planet.Index));

        // Fisher-Yates shuffle
        for (int i = _solarVisitPlan.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_solarVisitPlan[i], _solarVisitPlan[j]) = (_solarVisitPlan[j], _solarVisitPlan[i]);
        }

        // Randomly trim: visit between 1 and all available destinations
        if (_solarVisitPlan.Count > 1)
        {
            int keep = _rng.Next(1, _solarVisitPlan.Count + 1);
            _solarVisitPlan.RemoveRange(keep, _solarVisitPlan.Count - keep);
        }

        _solarPlanBuilt = true;
    }

    private void DecideGoal(SolarSystemSimulation sim)
    {
        if (!_solarPlanBuilt)
            BuildVisitPlan(sim);

        if (_solarVisitPlanIndex < _solarVisitPlan.Count)
        {
            var visit = _solarVisitPlan[_solarVisitPlanIndex];
            _solarGoal = visit.Goal;
            _solarTargetIndex = visit.TargetIndex;
        }
        else if (_solarSystemTimer > SolarSystemFtlDelay)
        {
            _solarGoal = SolarGoal.FTLJump;
        }
        else
        {
            _solarGoal = SolarGoal.Explore;
        }
    }

    private Vector2 GetTargetPosition(SolarSystemSimulation sim, StarSystemData starSystem, Game game)
    {
        var world = sim.EcsWorld;

        switch (_solarGoal)
        {
            case SolarGoal.FlyToStation:
                if (_solarTargetIndex >= 0 && _solarTargetIndex < sim.SpaceStationEntities.Count
                    && world.IsAlive(sim.SpaceStationEntities[_solarTargetIndex]))
                    return world.Get<Transform>(sim.SpaceStationEntities[_solarTargetIndex]).Position;
                break;

            case SolarGoal.FlyToPlanet:
                if (_solarTargetIndex >= 0 && _solarTargetIndex < sim.PlanetEntities.Count
                    && world.IsAlive(sim.PlanetEntities[_solarTargetIndex]))
                    return world.Get<Transform>(sim.PlanetEntities[_solarTargetIndex]).Position;
                break;

            case SolarGoal.FTLJump:
                float cx = WorldConfig.SolarSystemWidth * WindowConfig.TileSize / 2f;
                float cy = WorldConfig.SolarSystemHeight * WindowConfig.TileSize / 2f;
                return new Vector2(cx, cy);
        }

        // Explore: orbit the centre
        float starCX = WorldConfig.SolarSystemWidth * WindowConfig.TileSize / 2f;
        float starCY = WorldConfig.SolarSystemHeight * WindowConfig.TileSize / 2f;
        float angle = (float)game.GlobalTime * 0.1f;
        float radius = 3000f;
        return new Vector2(starCX + MathF.Cos(angle) * radius, starCY + MathF.Sin(angle) * radius);
    }

    private bool HandleStationOverlay(Game game, SolarSystemSimulation sim,
        SpaceStationOverlay overlay, Action<Game, SpaceStationData> beginDocking)
    {
        if (_solarGoal != SolarGoal.FlyToStation)
        {
            overlay.Close();
            return true;
        }

        int disembarkIdx = overlay.FindMenuOptionIndex(StationMenuOption.Disembark);
        if (disembarkIdx >= 0) overlay.MenuSelectedIndex = disembarkIdx;

        if (_actionCooldown > 0) return true;

        int stationIdx = sim.LocalNearbySpaceStationIndex;
        if (stationIdx >= 0 && stationIdx < sim.SpaceStations.Count)
        {
            var station = sim.SpaceStations[stationIdx];
            game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
            game.Player.ReturnSpaceStationIndex = station.Index;
            overlay.Close();
            _solarVisitPlanIndex++;
            beginDocking(game, station);
        }
        else
        {
            overlay.Close();
            _solarVisitPlanIndex++;
        }
        _actionCooldown = ActionDelay;
        return true;
    }

    private static bool HasNearbyEnemy(SolarSystemSimulation sim, Vector2 pos, float range)
    {
        foreach (var enemy in sim.EnemyEntities)
        {
            if (!sim.EcsWorld.IsAlive(enemy)) continue;
            var enemyPos = sim.EcsWorld.Get<Transform>(enemy).Position;
            if (Vector2.Distance(pos, enemyPos) < range)
                return true;
        }
        return false;
    }

    private void TryFTLJump(Game game)
    {
        int currentIdx = game.Player.CurrentStarSystemIndex;
        var galaxyData = game.GalaxyData;

        var reachable = new List<int>();
        for (int i = 0; i < galaxyData.Count; i++)
        {
            if (i == currentIdx) continue;
            float distance = (galaxyData[currentIdx].GalaxyPosition - galaxyData[i].GalaxyPosition).Length();
            float fuelCost = distance * ShipConfig.FuelPerDistanceUnit;
            if (distance <= ShipConfig.FtlMaxRange && game.Player.ShipFuel >= fuelCost)
                reachable.Add(i);
        }

        if (reachable.Count == 0)
        {
            _solarGoal = SolarGoal.Explore;
            return;
        }

        int targetIdx = reachable[_rng.Next(reachable.Count)];
        float jumpDist = (galaxyData[currentIdx].GalaxyPosition - galaxyData[targetIdx].GalaxyPosition).Length();
        float jumpFuel = jumpDist * ShipConfig.FuelPerDistanceUnit;

        game.Player.TrySpendFuel(jumpFuel);
        game.Player.CurrentStarSystemIndex = targetIdx;

        var sourceSystem = galaxyData[currentIdx];
        var targetSystem = galaxyData[targetIdx];

        _solarSystemTimer = 0;
        _solarVisitPlan.Clear();
        _solarVisitPlanIndex = 0;
        _solarPlanBuilt = false;
        _systemsVisited++;
        _actionCooldown = ActionDelay;

        game.ChangeState(new FTLTransitionState(sourceSystem, targetSystem));
    }

    /// <summary>
    /// Two-phase Newtonian flight controller: accelerate toward target while far,
    /// flip and brake when the braking distance equals the remaining range.
    /// </summary>
    private void NavigateShipToTarget(
        ref ShipInputComponent shipInput,
        ref Velocity shipVelocity,
        ref Transform shipTransform,
        ref ShipComponent ship,
        Vector2 targetPos,
        Vector2 shipPos,
        float dt,
        bool suppressStatus)
    {
        const float HoldRadius = ShipHoldRadius;
        const float SafetyMargin = ShipBrakingMargin;

        Vector2 toTarget = targetPos - shipPos;
        float dist = toTarget.Length();
        float speed = shipVelocity.Linear.Length();
        float maxAccel = ship.MaxAcceleration > 0f ? ship.MaxAcceleration : 100f;
        float maxRot = ship.MaxRotationSpeed > 0f ? ship.MaxRotationSpeed : 200f;

        float brakingDist = speed * speed / (2f * maxAccel) * SafetyMargin;

        bool isAtTarget = dist < HoldRadius && speed < StoppedSpeed;
        bool shouldBrake = !isAtTarget && speed > StoppedSpeed && dist <= brakingDist;

        if (!suppressStatus)
        {
            if (isAtTarget)
                _statusAction = $"Holding (dist:{dist:F0})";
            else if (shouldBrake)
                _statusAction = $"Braking (dist:{dist:F0}, spd:{speed:F0})";
            else
                _statusAction = $"Approaching (dist:{dist:F0}, spd:{speed:F0})";
        }

        if (isAtTarget)
        {
            shipInput.AccelerationDirection = Vector2.Zero;
            shipInput.RotationSpeed = 0f;
            return;
        }

        Vector2 thrustDir;
        if (shouldBrake)
        {
            thrustDir = speed > 0.1f
                ? -Vector2.Normalize(shipVelocity.Linear)
                : -(toTarget / dist);
        }
        else
        {
            thrustDir = toTarget / dist;
        }

        shipInput.AccelerationDirection = thrustDir;

        float targetRotDeg = MathF.Atan2(thrustDir.Y, thrustDir.X) * 180f / MathF.PI;
        float rotDelta = MathHelper.DiffRotation(shipTransform.Rotation, targetRotDeg);
        shipInput.RotationSpeed = Math.Clamp(rotDelta / Math.Max(dt, 0.001f), -maxRot, maxRot);
    }
}
