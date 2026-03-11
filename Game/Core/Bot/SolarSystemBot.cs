using System.Diagnostics;
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
    private const float CombatHoldRadius = 425f;
    private const float ShipBrakingMargin = 1.8f;
    private const float EnemyDetectRange = 800f;
    private const float RetreatHullThreshold = 0.35f;
    private const float CombatTargetMemoryDuration = 10.0f;
    private const float AttackEnterRangeFactor = 0.92f;
    private const float AttackExitRangeFactor = 1.08f;
    private const float CombatCloseThresholdMultiplier = 0.7f;
    private const float CombatFarThresholdMultiplier = 1.3f;
    private const float CombatChaseThrustMultiplier = 1.0f;
    private const float CombatStrafeThrustMultiplier = 0.3f;
    private const float CombatStrafeFrequency = 0.8f;
    private const float CombatBackoffThrustMultiplier = 1.0f;
    private const float CombatCloseInThrustMultiplier = 1.0f;
    private const float CombatAimToleranceDegrees = 10f;
    private const float RetreatThrustMultiplier = 1.0f;
    private const float RetreatTargetBlend = 1.0f;
    private const float AttackDistanceCapFactor = 1.1f;

    // ── State ────────────────────────────────────────────────────────
    private enum SolarGoal { FlyToStation, FlyToPlanet, Explore, FTLJump }
    private readonly record struct SolarVisit(SolarGoal Goal, int TargetIndex);
    private readonly record struct EnemyContact(Entity Entity, Vector2 Position, Vector2 Velocity, float Distance);

    private SolarGoal _solarGoal = SolarGoal.FlyToStation;
    private int _solarTargetIndex;
    private int _systemsVisited;
    private readonly List<SolarVisit> _solarVisitPlan = [];
    private int _solarVisitPlanIndex;
    private bool _solarPlanBuilt;

    private float _solarSystemTimer;
    private float _actionCooldown;
    private AIState _combatState;
    private Entity _combatTargetEntity;
    private Vector2 _combatLastKnownTargetPos;
    private Vector2 _combatLastKnownTargetVelocity;
    private float _combatTargetMemoryTimer;
    private float _combatStateTimer;

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
        _combatState = AIState.Idle;
        _combatTargetEntity = Entity.Null;
        _combatLastKnownTargetPos = Vector2.Zero;
        _combatLastKnownTargetVelocity = Vector2.Zero;
        _combatTargetMemoryTimer = 0;
        _combatStateTimer = 0;
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
        _combatStateTimer += dt;

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

        // ── Player dead — wait for respawn ──
        if (sim.LocalPlayerDead || !sim.EcsWorld.IsAlive(simPlayer.Entity))
            return true;

        var world = sim.EcsWorld;
        ref var shipTransform = ref world.Get<Transform>(simPlayer.Entity);
        ref var shipVelocity = ref world.Get<Velocity>(simPlayer.Entity);
        ref var shipInput = ref world.Get<ShipInputComponent>(simPlayer.Entity);
        ref var shipComp = ref world.Get<ShipComponent>(simPlayer.Entity);
        ref var shipHealth = ref world.Get<Health>(simPlayer.Entity);

        // Reset input; will be set by combat or navigation logic below if not overridden by an overlay
        shipInput.AccelerationDirection = Vector2.Zero;
        shipInput.RotationSpeed = 0;
        shipInput.Shoot = false;


        Vector2 shipPos = shipTransform.Position;
        EnemyContact? liveEnemy = FindNearestEnemy(sim, shipPos, EnemyDetectRange);
        EnemyContact? closeEnemy = ResolveCombatTarget(liveEnemy, shipPos, dt);
        bool hullLow = shipHealth.HullPercent <= RetreatHullThreshold;
        bool needsRepair = ShipRepairService.NeedsRepair(game.Player);
        bool canRepair = ShipRepairService.CanAffordFullRepair(game.Player);

        int retreatStationIndex = -1;
        bool retreatForRepair = hullLow && needsRepair && canRepair
            && TryGetNearestSpaceStationIndex(sim, shipPos, out retreatStationIndex);

        SolarGoal activeGoal = retreatForRepair ? SolarGoal.FlyToStation : _solarGoal;
        int activeTargetIndex = retreatForRepair ? retreatStationIndex : _solarTargetIndex;
        bool plannedStationVisit = _solarGoal == SolarGoal.FlyToStation;
        bool shouldRepairAtStation = needsRepair && canRepair && (plannedStationVisit || retreatForRepair);
        bool shouldDisembarkAtStation = plannedStationVisit && !retreatForRepair;

        if (stationOverlay.IsOpen)
        {
            return HandleStationOverlay(game, sim, simPlayer, stationOverlay, beginDocking,
                activeTargetIndex, shouldRepairAtStation, shouldDisembarkAtStation);
        }

        if (landingOverlay.IsOpen)
        {
            landingOverlay.Close();
            return true;
        }

        if (galaxyMapOverlay.IsOpen)
        {
            galaxyMapOverlay.Close(game);
            return true;
        }

        // ── Update status ──
        _statusGoal = activeGoal switch
        {
            SolarGoal.FlyToStation when retreatForRepair => "RETREATING TO STATION FOR REPAIRS",
            SolarGoal.FlyToStation => $"FLY TO STATION [{_solarVisitPlanIndex + 1}/{_solarVisitPlan.Count}]",
            SolarGoal.FlyToPlanet when hullLow => "FLEEING TO PLANET APPROACH",
            SolarGoal.FlyToPlanet => $"FLY TO PLANET  [{_solarVisitPlanIndex + 1}/{_solarVisitPlan.Count}]",
            SolarGoal.Explore => $"EXPLORING SYSTEM ({Math.Max(0f, SolarSystemFtlDelay - _solarSystemTimer):F0}s to FTL)",
            SolarGoal.FTLJump => "PREPARING FTL JUMP",
            _ => "SOLAR SYSTEM"
        };

        bool enemyNearby = closeEnemy.HasValue;
        shipInput.Shoot = false;

        // ── Check proximity interactions ──
        float shipSpeed = shipVelocity.Linear.Length();
        bool shipStopped = shipSpeed < StoppedSpeed;

        if (_actionCooldown <= 0)
        {
            if (sim.LocalNearbySpaceStationIndex >= 0 &&
                activeGoal == SolarGoal.FlyToStation &&
                activeTargetIndex == sim.LocalNearbySpaceStationIndex)
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
                activeGoal == SolarGoal.FlyToPlanet &&
                sim.Planets[sim.LocalNearbyPlanetIndex].HasSolidSurface &&
                activeTargetIndex == sim.LocalNearbyPlanetIndex)
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

        bool engageEnemy = closeEnemy.HasValue && !hullLow && !retreatForRepair;

        if (engageEnemy && closeEnemy is { } enemy)
        {
            _statusGoal = "ENGAGING HOSTILE";
            ApplyCombatBehavior(ref shipInput, ref shipTransform, ref shipVelocity, ref shipComp,
                shipPos, enemy, dt);
        }
        else
        {
            Vector2 targetPos = GetTargetPosition(sim, activeGoal, activeTargetIndex, game);
            bool suppressStatus = false;
            if (enemyNearby && hullLow && closeEnemy is { } retreatEnemy)
            {
                suppressStatus = true;
                _statusAction = retreatForRepair
                    ? "Breaking contact and running for repairs"
                    : $"Fleeing past hostile (dist:{retreatEnemy.Distance:F0})";

                SetCombatState(AIState.Flee);
                NavigateShipToTarget(ref shipInput, ref shipVelocity, ref shipTransform, ref shipComp,
                    targetPos, shipPos, dt, suppressStatus, holdRadius: ShipHoldRadius);
            }
            else if (enemyNearby)
            {
                suppressStatus = true;
                _statusAction = $"Holding course under fire (dist:{closeEnemy!.Value.Distance:F0})";
            }

            if (!(enemyNearby && hullLow))
            {
                NavigateShipToTarget(ref shipInput, ref shipVelocity, ref shipTransform, ref shipComp,
                    targetPos, shipPos, dt, suppressStatus, holdRadius: ShipHoldRadius);
            }
        }

        // ── FTL jump ──
        if (activeGoal == SolarGoal.FTLJump && _actionCooldown <= 0 && !enemyNearby)
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

    private Vector2 GetTargetPosition(SolarSystemSimulation sim, SolarGoal goal, int targetIndex, Game game)
    {
        var world = sim.EcsWorld;

        switch (goal)
        {
            case SolarGoal.FlyToStation:
                if (targetIndex >= 0 && targetIndex < sim.SpaceStationEntities.Count
                    && world.IsAlive(sim.SpaceStationEntities[targetIndex]))
                    return world.Get<Transform>(sim.SpaceStationEntities[targetIndex]).Position;
                break;

            case SolarGoal.FlyToPlanet:
                if (targetIndex >= 0 && targetIndex < sim.PlanetEntities.Count
                    && world.IsAlive(sim.PlanetEntities[targetIndex]))
                    return world.Get<Transform>(sim.PlanetEntities[targetIndex]).Position;
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

    private bool HandleStationOverlay(Game game, SolarSystemSimulation sim, SimulationPlayer simPlayer,
        SpaceStationOverlay overlay, Action<Game, SpaceStationData> beginDocking,
        int targetStationIndex, bool shouldRepairAtStation, bool shouldDisembarkAtStation)
    {
        if (targetStationIndex < 0)
        {
            overlay.Close();
            return true;
        }

        if (shouldRepairAtStation)
        {
            int repairIdx = overlay.FindMenuOptionIndex(StationMenuOption.Repair);
            if (repairIdx >= 0) overlay.MenuSelectedIndex = repairIdx;

            if (_actionCooldown > 0) return true;

            if (ShipRepairService.TryRepairFull(game.Player))
            {
                ApplyRepairToLiveShip(sim, simPlayer, game.Player.ShipHealth);
                _statusAction = "Repairing ship hull";
                _actionCooldown = ActionDelay;

                if (!shouldDisembarkAtStation)
                {
                    overlay.Close();
                    return true;
                }
            }
        }

        if (!shouldDisembarkAtStation)
        {
            overlay.Close();
            _actionCooldown = ActionDelay;
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

    private static EnemyContact? FindNearestEnemy(SolarSystemSimulation sim, Vector2 pos, float range)
    {
        EnemyContact? closest = null;
        foreach (var enemy in sim.EnemyEntities)
        {
            if (!sim.EcsWorld.IsAlive(enemy)) continue;
            if (sim.EcsWorld.Get<EnemyAI>(enemy).Config.Faction != Faction.Pirate) continue; // Only consider pirates as enemies to engage
            var enemyPos = sim.EcsWorld.Get<Transform>(enemy).Position;
            var enemyVel = sim.EcsWorld.Has<Velocity>(enemy)
                ? sim.EcsWorld.Get<Velocity>(enemy).Linear
                : Vector2.Zero;
            float distance = Vector2.Distance(pos, enemyPos);
            if (distance > range) continue;

            if (!closest.HasValue || distance < closest.Value.Distance)
                closest = new EnemyContact(enemy, enemyPos, enemyVel, distance);
        }
        return closest;
    }

    private EnemyContact? ResolveCombatTarget(EnemyContact? liveEnemy, Vector2 shipPos, float dt)
    {
        if (liveEnemy is { } enemy)
        {
            _combatTargetEntity = enemy.Entity;
            _combatLastKnownTargetPos = enemy.Position;
            _combatLastKnownTargetVelocity = enemy.Velocity;
            _combatTargetMemoryTimer = CombatTargetMemoryDuration;
            return enemy;
        }

        if (_combatTargetMemoryTimer <= 0f)
        {
            _combatTargetEntity = Entity.Null;
            SetCombatState(AIState.Idle);
            return null;
        }

        _combatTargetMemoryTimer -= dt;
        if (_combatTargetMemoryTimer <= 0f)
        {
            _combatTargetEntity = Entity.Null;
            SetCombatState(AIState.Idle);
            return null;
        }

        _combatLastKnownTargetPos += _combatLastKnownTargetVelocity * dt;
        return new EnemyContact(
            _combatTargetEntity,
            _combatLastKnownTargetPos,
            _combatLastKnownTargetVelocity,
            Vector2.Distance(shipPos, _combatLastKnownTargetPos));
    }

    private static bool TryGetNearestSpaceStationIndex(SolarSystemSimulation sim, Vector2 shipPos, out int stationIndex)
    {
        stationIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < sim.SpaceStationEntities.Count; i++)
        {
            var entity = sim.SpaceStationEntities[i];
            if (!sim.EcsWorld.IsAlive(entity)) continue;

            float distance = Vector2.Distance(shipPos, sim.EcsWorld.Get<Transform>(entity).Position);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            stationIndex = i;
        }

        return stationIndex >= 0;
    }

    private static void ApplyRepairToLiveShip(SolarSystemSimulation sim, SimulationPlayer simPlayer, float repairedHull)
    {
        if (!sim.EcsWorld.IsAlive(simPlayer.Entity) || !sim.EcsWorld.Has<Health>(simPlayer.Entity))
            return;

        ref var health = ref sim.EcsWorld.Get<Health>(simPlayer.Entity);
        health.Hull = Math.Clamp(repairedHull, 0f, health.MaxHull);
    }

    private void ApplyCombatBehavior(
        ref ShipInputComponent shipInput,
        ref Transform shipTransform,
        ref Velocity shipVelocity,
        ref ShipComponent ship,
        Vector2 shipPos,
        EnemyContact enemy,
        float dt)
    {
        Vector2 toTarget = enemy.Position - shipPos;
        Vector2 dirToTarget = NormalizeOrFacingFallback(toTarget, shipTransform.Rotation);
        float weaponRange = GetWeaponRange(ship.Weapons);
        float preferredDistance = ResolvePreferredCombatDistance(weaponRange);
        float attackDistance = ResolveAttackDistance(weaponRange, preferredDistance);
        float projectileSpeed = GetFastestProjectileSpeed(ship.Weapons);

        bool inAttackRange = _combatState switch
        {
            AIState.Attack => enemy.Distance <= attackDistance * AttackExitRangeFactor,
            _ => enemy.Distance <= attackDistance * AttackEnterRangeFactor
        };

        if (attackDistance <= 0f)
            inAttackRange = false;

        if (inAttackRange)
        {
            SetCombatState(AIState.Attack);

            Vector2 aimDir = ComputeAimDirection(shipPos, enemy.Position, enemy.Velocity,
                shipVelocity.Linear, projectileSpeed, dirToTarget);

            shipInput.RotationSpeed = ComputeWantedRotationSpeed(shipTransform.Rotation, aimDir, dt, ship.MaxRotationSpeed);

            if (enemy.Distance < preferredDistance * CombatCloseThresholdMultiplier)
            {
                shipInput.AccelerationDirection = dirToTarget * CombatBackoffThrustMultiplier;
            }
            else if (enemy.Distance > preferredDistance * CombatFarThresholdMultiplier)
            {
                shipInput.AccelerationDirection = dirToTarget * CombatCloseInThrustMultiplier;
            }
            else
            {
                ApplyStrafe(ref shipInput, dirToTarget);
            }

            float aimError = MathF.Abs(MathHelper.DiffRotation(
                shipTransform.Rotation,
                MathF.Atan2(aimDir.Y, aimDir.X) * 180f / MathF.PI));
            shipInput.Shoot = aimError <= CombatAimToleranceDegrees;
            _statusAction = shipInput.Shoot
                ? $"Attacking pirate (dist:{enemy.Distance:F0})"
                : $"Tracking firing solution (dist:{enemy.Distance:F0})";
            return;
        }

        SetCombatState(AIState.Chase);
        shipInput.RotationSpeed = ComputeWantedRotationSpeed(shipTransform.Rotation, dirToTarget, dt, ship.MaxRotationSpeed);
        shipInput.AccelerationDirection = dirToTarget * CombatChaseThrustMultiplier;
        _statusAction = $"Chasing pirate (dist:{enemy.Distance:F0})";
    }

    private void ApplyStrafe(ref ShipInputComponent shipInput, Vector2 dirToTarget)
    {
        Vector2 strafeDir = new(-dirToTarget.Y, dirToTarget.X);
        float direction = MathF.Sign(MathF.Sin(_combatStateTimer * CombatStrafeFrequency));
        if (direction == 0f)
            direction = 1f;

        shipInput.AccelerationDirection += strafeDir * CombatStrafeThrustMultiplier * direction;
    }

    private void SetCombatState(AIState nextState)
    {
        if (_combatState == nextState)
            return;

        _combatState = nextState;
        _combatStateTimer = 0f;
    }

    private static Vector2 NormalizeOrFacingFallback(Vector2 direction, float rotation)
    {
        if (direction.LengthSquared() <= 0.0001f)
            return FacingDirection(rotation);

        var normalized = Vector2.Normalize(direction);
        return float.IsNaN(normalized.X) ? FacingDirection(rotation) : normalized;
    }

    private static Vector2 FacingDirection(float rotationDeg)
    {
        float rad = rotationDeg * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }

    private static float ResolvePreferredCombatDistance(float weaponRange)
    {
        if (weaponRange <= 0f)
            return CombatHoldRadius;

        return Math.Clamp(weaponRange * 0.65f, NpcConfig.EnemyEngageDistance, CombatHoldRadius);
    }

    private static float ResolveAttackDistance(float weaponRange, float preferredDistance)
    {
        if (weaponRange <= 0f)
            return 0f;

        return Math.Min(weaponRange, preferredDistance * AttackDistanceCapFactor);
    }

    private static float GetWeaponRange(IReadOnlyList<ShipWeaponSpec> weapons)
    {
        float maxRange = 0f;
        for (int i = 0; i < weapons.Count; i++)
            maxRange = MathF.Max(maxRange, weapons[i].Range);

        return maxRange;
    }

    private static float GetFastestProjectileSpeed(IReadOnlyList<ShipWeaponSpec> weapons)
    {
        float maxSpeed = 0f;
        for (int i = 0; i < weapons.Count; i++)
            maxSpeed = MathF.Max(maxSpeed, weapons[i].ProjectileSpeed);

        return maxSpeed;
    }

    private static Vector2 ComputeAimDirection(Vector2 shooterPos, Vector2 targetPos,
        Vector2 targetVelocity, Vector2 shooterVelocity, float projectileSpeed, Vector2 fallbackDirection)
    {
        if (projectileSpeed <= 0f)
            return fallbackDirection;

        Vector2 toTarget = targetPos - shooterPos;
        float distance = toTarget.Length();
        if (distance <= 0.001f)
            return fallbackDirection;

        float leadTime = Math.Clamp(distance / projectileSpeed, 0f, 1.5f);
        Vector2 relativeTargetVelocity = targetVelocity - shooterVelocity;
        Vector2 predictedPos = targetPos + relativeTargetVelocity * leadTime;
        Vector2 aimDir = NormalizeOrZero(predictedPos - shooterPos);
        return aimDir == Vector2.Zero ? fallbackDirection : aimDir;
    }

    private static Vector2 NormalizeOrZero(Vector2 vector)
    {
        if (vector.LengthSquared() <= 0.0001f)
            return Vector2.Zero;

        Vector2 normalized = Vector2.Normalize(vector);
        return float.IsNaN(normalized.X) ? Vector2.Zero : normalized;
    }

    private static float ComputeWantedRotationSpeed(float currentRotation, Vector2 targetDirection,
        float dt, float maxRotationSpeed)
    {
        if (targetDirection == Vector2.Zero || dt <= 0f)
            return 0f;

        float targetRotation = MathF.Atan2(targetDirection.Y, targetDirection.X) * 180f / MathF.PI;
        float delta = MathHelper.DiffRotation(currentRotation, targetRotation);
        float requiredRotationSpeed = delta / dt;
        return Math.Clamp(requiredRotationSpeed, -maxRotationSpeed, maxRotationSpeed);
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
        bool suppressStatus,
        float holdRadius)
    {
        const float SafetyMargin = ShipBrakingMargin;

        Vector2 toTarget = targetPos - shipPos;
        float dist = toTarget.Length();
        float speed = shipVelocity.Linear.Length();
        float maxAccel = ship.MaxAcceleration > 0f ? ship.MaxAcceleration : 100f;
        float maxRot = ship.MaxRotationSpeed > 0f ? ship.MaxRotationSpeed : 200f;

        float brakingDist = speed * speed / (2f * maxAccel) * SafetyMargin;

        bool isAtTarget = dist < holdRadius && speed < StoppedSpeed;
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
