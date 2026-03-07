using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Map;
using SpaceExplorationGame.UI.Overlays.Menu;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Autoplay bot that plays the game autonomously.
/// Toggled via the debug menu. Each game state calls the appropriate Update method
/// which writes directly to ECS input components and triggers state transitions.
/// </summary>
public class AutoplayBot
{
    /// <summary>Delay in seconds between bot actions.</summary>
    private const float ActionDelay = 3.0f;

    /// <summary>Speed threshold below which the ship is considered stopped.</summary>
    private const float StoppedSpeed = 15f;

    // ── Timing constants ────────────────────────────────────────────
    /// <summary>Seconds before auto-starting a new game from the main menu.</summary>
    private const float MainMenuStartDelay = 1.0f;
    /// <summary>Seconds spent in a solar system before attempting an FTL jump.</summary>
    private const float SolarSystemFtlDelay = 15.0f;
    /// <summary>Seconds on the planet surface (starship menu open) before taking off.</summary>
    private const float SurfaceTakeoffTime = SurfaceReturnTime + 2.0f;
    /// <summary>Seconds on the planet surface before heading back to the ship.</summary>
    private const float SurfaceReturnTime = 40.0f;
    /// <summary>Earliest time window (seconds) to head toward a settlement.</summary>
    private const float SurfaceSettleStartTime = 5.0f;
    /// <summary>Latest time window (seconds) to head toward a settlement.</summary>
    private const float SurfaceSettleEndTime = 15.0f;
    /// <summary>Seconds in an interior (starship menu open) before taking off.</summary>
    private const float InteriorTakeoffTime = InteriorExitTime + 2.0f;
    /// <summary>Seconds in an interior before switching goal to Exit.</summary>
    private const float InteriorExitTime = 40.0f;
    /// <summary>Seconds without waypoint progress before the interior path is recomputed.</summary>
    private const float StuckTimeout = 2.0f;
    /// <summary>Seconds the bot waits on each dialogue line before pressing continue.</summary>
    private const float DialogueLineDelay = 1.0f;

    // ── Navigation constants ─────────────────────────────────────────
    /// <summary>World-space radius within which the ship holds position.</summary>
    private const float ShipHoldRadius = 150f;
    /// <summary>Multiplier on braking distance to account for ship rotation time.</summary>
    private const float ShipBrakingMargin = 1.8f;
    /// <summary>World-space range at which the bot notices and fires at enemies.</summary>
    private const float EnemyDetectRange = 800f;
    /// <summary>Distance threshold at which on-foot movement is considered arrived.</summary>
    private const float SurfaceWalkThreshold = 20f;
    /// <summary>Wander radius used when exploring a planet surface.</summary>
    private const float SurfaceWanderDistance = 400f;
    /// <summary>Range within which the bot targets pirate NPCs while exploring.</summary>
    private const float SurfaceEnemyDetectRange = 700f;
    /// <summary>Range within which the bot targets mineable rocks while exploring.</summary>
    private const float SurfaceRockMineRange = 550f;
    /// <summary>Distance at which the bot stops approaching a pirate and fires from (avoids point-blank misses).</summary>
    private const float SurfaceEnemyStandoffDistance = 110f;
    /// <summary>Distance at which the bot stops approaching a rock and mines from.</summary>
    private const float SurfaceRockStandoffDistance = 80f;

    public bool Enabled { get; set; }

    // ── Timers ──────────────────────────────────────────────────────
    private float _mainMenuTimer;
    private float _solarSystemTimer;
    private float _surfaceTimer;
    private float _interiorTimer;
    private float _actionCooldown;

    // ── Solar system state ──────────────────────────────────────────
    private enum SolarGoal { FlyToStation, FlyToPlanet, Explore, FTLJump }
    private SolarGoal _solarGoal = SolarGoal.FlyToStation;
    private int _solarTargetIndex;
    private int _systemsVisited;
    private int _stationsDockedInSystem;
    private int _planetsLandedInSystem;

    // ── Surface state ───────────────────────────────────────────────
    private enum SurfaceGoal { Explore, GoToSettlement, GoToShip }
    private SurfaceGoal _surfaceGoal = SurfaceGoal.Explore;
    private Vector2 _surfaceWanderTarget;
    private bool _surfaceWanderTargetSet;
    private enum SurfaceExploreSubGoal { Wander, Enemy, Rock }
    private SurfaceExploreSubGoal _surfaceExploreSubGoal = SurfaceExploreSubGoal.Wander;
    private int _surfaceSettlementsVisited;
    // Pathfinding
    private readonly List<TilePos> _surfacePath = [];
    private TilePos _surfacePathTarget = new(-1, -1);
    private float _surfaceStuckTimer;

    // ── Interior state ──────────────────────────────────────────────
    private enum InteriorGoal { VisitInteractables, Exit }
    private InteriorGoal _interiorGoal = InteriorGoal.VisitInteractables;
    private int _interiorInteractableIndex;
    private bool _starshipMenuWasOpen;
    private readonly List<int> _interiorVisitOrder = []; // shuffled index into interactables+npcs
    private TilePos _lastInteractedTilePos = new(-1, -1); // prevents re-triggering same interactable
    // Pathfinding
    private readonly List<TilePos> _interiorPath = [];
    private TilePos _interiorPathTarget = new(-1, -1);
    private float _interiorStuckTimer;
    private float _dialogueCooldown;  // seconds remaining before dismissing the current dialogue line

    // ── Status display ───────────────────────────────────────────────
    private string _statusGoal = "";
    private string _statusAction = "";

    // ── Random ──────────────────────────────────────────────────────
    private readonly Random _rng = new();

    /// <summary>
    /// Reset bot state when starting fresh (e.g., returning to main menu).
    /// </summary>
    public void Reset()
    {
        _mainMenuTimer = 0;
        _solarSystemTimer = 0;
        _surfaceTimer = 0;
        _interiorTimer = 0;
        _actionCooldown = 0;
        _solarGoal = SolarGoal.FlyToStation;
        _solarTargetIndex = 0;
        _systemsVisited = 0;
        _stationsDockedInSystem = 0;
        _planetsLandedInSystem = 0;
        _surfaceGoal = SurfaceGoal.Explore;
        _surfaceWanderTargetSet = false;
        _surfacePath.Clear();
        _surfacePathTarget = new TilePos(-1, -1);
        _surfaceStuckTimer = 0;
        _interiorGoal = InteriorGoal.VisitInteractables;
        _interiorInteractableIndex = 0;
        _interiorPath.Clear();
        _interiorPathTarget = new TilePos(-1, -1);
        _interiorStuckTimer = 0;
        _interiorVisitOrder.Clear();
        _lastInteractedTilePos = new TilePos(-1, -1);
        _starshipMenuWasOpen = false;
        _dialogueCooldown = 0;
        _statusGoal = "";
        _statusAction = "";
    }

    /// <summary>
    /// Renders the bot's current goal and action as text in the bottom-left corner.
    /// Call from each state's RenderHud.
    /// </summary>
    public void RenderStatus(ISpriteRenderer renderer)
    {
        if (!Enabled) return;

        const float scale = 1.5f;
        const float pad = 8f;
        const float lineHeight = 14f;
        float y = renderer.WindowHeight - pad - lineHeight * 3;
        float x = pad;

        var labelColor = new Color4(120, 220, 255, 200);
        var goalColor = new Color4(255, 255, 255, 220);
        var actionColor = new Color4(200, 255, 180, 200);

        renderer.DrawTextScreen(x, y, "[AUTOPLAY]", labelColor, scale);
        y += lineHeight;
        if (!string.IsNullOrEmpty(_statusGoal))
        {
            renderer.DrawTextScreen(x, y, _statusGoal, goalColor, scale);
            y += lineHeight;
        }
        if (!string.IsNullOrEmpty(_statusAction))
            renderer.DrawTextScreen(x, y, _statusAction, actionColor, scale);
    }

    // ════════════════════════════════════════════════════════════════
    //  MAIN MENU
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-starts the game after a brief delay.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    public bool UpdateMainMenu(Game game, MainMenuOverlay menuOverlay, DebugMenuOverlay debugOverlay)
    {
        if (!Enabled) return false;

        // Close debug overlay if open
        if (debugOverlay.IsOpen)
        {
            debugOverlay.Close();
            return true;
        }

        _mainMenuTimer += game.DeltaTime;
        _statusGoal = "MAIN MENU";
        _statusAction = _mainMenuTimer < MainMenuStartDelay ? "Waiting..." : "Starting game";

        // Wait before starting the game
        if (_mainMenuTimer >= MainMenuStartDelay)
        {
            _mainMenuTimer = 0;
            _stationsDockedInSystem = 0;
            _planetsLandedInSystem = 0;
            _solarGoal = SolarGoal.FlyToStation;
            menuOverlay.StartRequested = true;
        }

        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  SOLAR SYSTEM
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Flies the ship around the solar system, docks at stations, lands on planets, and jumps to new systems.
    /// Called from SolarSystemState.UpdateInput when autoplay is on.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    public bool UpdateSolarSystem(
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
        Action<Game, LandingSelectionRequest> beginLanding)
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
        DecideSolarGoal(sim, starSystem, game);

        if (stationOverlay.IsOpen)
        {
            return HandleStationOverlay(game, sim, stationOverlay, starSystem, beginDocking);
        }

        if (landingOverlay.IsOpen)
        {
            return HandleLandingOverlay(game, sim, landingOverlay, starSystem, beginLanding);
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
            SolarGoal.FlyToStation => $"FLY TO STATION #{_solarTargetIndex + 1}",
            SolarGoal.FlyToPlanet => $"FLY TO PLANET #{_solarTargetIndex + 1}",
            SolarGoal.Explore => "EXPLORING SYSTEM",
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
            if (sim.LocalNearbySpaceStationIndex >= 0 && _solarGoal == SolarGoal.FlyToStation && _solarTargetIndex == sim.LocalNearbySpaceStationIndex)
            {
                if (!shipStopped)
                {
                    // Keep navigating to target center to shed speed before acting
                    _statusAction = $"Stopping before dock (spd:{shipSpeed:F0})";
                }
                else
                {
                    // Open station overlay
                    _actionCooldown = ActionDelay;
                    _statusAction = "Opening station overlay";
                    stationOverlay.Open(starSystem, sim.SpaceStations[sim.LocalNearbySpaceStationIndex], game);
                    return true;
                }
            }

            if (sim.LocalNearbyPlanetIndex >= 0 && _solarGoal == SolarGoal.FlyToPlanet
                && sim.Planets[sim.LocalNearbyPlanetIndex].HasSolidSurface && _solarTargetIndex == sim.LocalNearbyPlanetIndex)
            {
                if (!shipStopped)
                {
                    _statusAction = $"Stopping before landing (spd:{shipSpeed:F0})";
                }
                else
                {
                    // Trigger landing directly (skip the map overlay for bot simplicity)
                    _actionCooldown = ActionDelay;
                    var planet = sim.Planets[sim.LocalNearbyPlanetIndex];
                    int centerTile = WorldConfig.PlanetSurfaceWidth / 2;
                    var landing = new LandingSelectionRequest(
                        starSystem, planet,
                        centerTile, centerTile,
                        IsMoon: false, MoonPlanetIndex: -1, MoonIndex: -1);
                    _statusAction = "Landing on planet";
                    beginLanding(game, landing);
                    _planetsLandedInSystem++;
                    // Reset surface exploration state for the new planet
                    _surfaceSettlementsVisited = 0;
                    return true;
                }
            }
        }

        // ── Navigate toward goal ──
        Vector2 targetPos = GetSolarTargetPosition(sim, starSystem, shipPos, game);
        NavigateShipToTarget(ref shipInput, ref shipVelocity, ref shipTransform, ref shipComp,
            targetPos, shipPos, dt, enemyNearby);

        // ── FTL jump when goal is FTLJump and we've been in system long enough ──
        if (_solarGoal == SolarGoal.FTLJump && _actionCooldown <= 0)
        {
            _statusAction = "Jumping to new system!";
            TryFTLJump(game, starSystem);
        }

        return true;
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
        const float HoldRadius = ShipHoldRadius;       // stop within this world-space distance
        const float SafetyMargin = ShipBrakingMargin;   // extra braking distance buffer for turn time

        Vector2 toTarget = targetPos - shipPos;
        float dist = toTarget.Length();
        float speed = shipVelocity.Linear.Length();
        float maxAccel = ship.MaxAcceleration > 0f ? ship.MaxAcceleration : 100f;
        float maxRot = ship.MaxRotationSpeed > 0f ? ship.MaxRotationSpeed : 200f;

        // Braking distance = v² / (2·a), scaled up by safety margin to account for
        // the time needed to rotate to face retrograde before the burn is effective.
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

        // Choose thrust direction: retrograde when braking, prograde when approaching.
        Vector2 thrustDir;
        if (shouldBrake)
        {
            // Retrograde: opposite of current velocity (safe-guard against zero velocity)
            thrustDir = speed > 0.1f
                ? -Vector2.Normalize(shipVelocity.Linear)
                : -(toTarget / dist);
        }
        else
        {
            thrustDir = toTarget / dist;
        }

        shipInput.AccelerationDirection = thrustDir;

        // Rotate to face the thrust direction as fast as possible.
        float targetRotDeg = MathF.Atan2(thrustDir.Y, thrustDir.X) * 180f / MathF.PI;
        float rotDelta = MathHelper.DiffRotation(shipTransform.Rotation, targetRotDeg);
        shipInput.RotationSpeed = Math.Clamp(rotDelta / Math.Max(dt, 0.001f), -maxRot, maxRot);
    }

    private void DecideSolarGoal(SolarSystemSimulation sim, StarSystemData starSystem, Game game)
    {
        // Decide priorities based on starSystem.Index so its not always the same if we return to the same system multiple times during testing.
        var priorityStations = (starSystem.Index + _systemsVisited) % 2 == 0;

        // Priority: dock at stations first, then land on planets, then FTL
        if (_stationsDockedInSystem < sim.SpaceStations.Count &&
            (priorityStations || _planetsLandedInSystem >= sim.Planets.Count(p => p.HasSolidSurface)))
        {
            _solarGoal = SolarGoal.FlyToStation;
            _solarTargetIndex = Math.Min(_stationsDockedInSystem, sim.SpaceStations.Count - 1);
        }
        else if (_planetsLandedInSystem < sim.Planets.Count(p => p.HasSolidSurface) &&
            (!priorityStations || _stationsDockedInSystem >= sim.SpaceStations.Count))
        {
            _solarGoal = SolarGoal.FlyToPlanet;
            var availablePlanets = sim.Planets.Where(p => p.HasSolidSurface).ToList();
            _solarTargetIndex = availablePlanets[Math.Min(_planetsLandedInSystem, availablePlanets.Count - 1)].Index;
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

    private Vector2 GetSolarTargetPosition(SolarSystemSimulation sim, StarSystemData starSystem,
        Vector2 shipPos, Game game)
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
                // Fly toward center while preparing FTL
                float cx = WorldConfig.SolarSystemWidth * WindowConfig.TileSize / 2f;
                float cy = WorldConfig.SolarSystemHeight * WindowConfig.TileSize / 2f;
                return new Vector2(cx, cy);
        }

        // Explore: wander randomly
        float starCX = WorldConfig.SolarSystemWidth * WindowConfig.TileSize / 2f;
        float starCY = WorldConfig.SolarSystemHeight * WindowConfig.TileSize / 2f;
        float angle = (float)game.GlobalTime * 0.1f;
        float radius = 3000f;
        return new Vector2(starCX + MathF.Cos(angle) * radius, starCY + MathF.Sin(angle) * radius);
    }

    private bool HandleStationOverlay(Game game, SolarSystemSimulation sim,
        SpaceStationOverlay overlay, StarSystemData starSystem,
        Action<Game, SpaceStationData> beginDocking)
    {
        // If the goal is no longer to dock (e.g. we already visited this station and the
        // overlay was auto-reopened by SolarSystemState on return from interior), just close it.
        if (_solarGoal != SolarGoal.FlyToStation)
        {
            overlay.Close();
            return true;
        }

        // Pre-highlight "DOCK" so the player can see what the bot will pick
        int disembarkIdx = overlay.FindMenuOptionIndex(StationMenuOption.Disembark);
        if (disembarkIdx >= 0) overlay.MenuSelectedIndex = disembarkIdx;

        // At the station overlay, sell cargo, repair, then dock (disembark into interior)
        if (_actionCooldown > 0) return true;

        // Just dock (disembark) — the station already refuels on open
        int stationIdx = sim.LocalNearbySpaceStationIndex;
        if (stationIdx >= 0 && stationIdx < sim.SpaceStations.Count)
        {
            var station = sim.SpaceStations[stationIdx];
            game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
            game.Player.ReturnSpaceStationIndex = station.Index;
            overlay.Close();
            _stationsDockedInSystem++;
            beginDocking(game, station);
        }
        else
        {
            overlay.Close();
            _stationsDockedInSystem++;
        }
        _actionCooldown = ActionDelay;
        return true;
    }

    private bool HandleLandingOverlay(Game game, SolarSystemSimulation sim,
        PlanetLandingOverlay overlay, StarSystemData starSystem,
        Action<Game, LandingSelectionRequest> beginLanding)
    {
        // Close the overlay — we handle landing directly in the proximity check
        overlay.Close();
        return true;
    }

    private bool HasNearbyEnemy(SolarSystemSimulation sim, Vector2 pos, float range)
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

    private void TryFTLJump(Game game, StarSystemData currentSystem)
    {
        int currentIdx = game.Player.CurrentStarSystemIndex;
        var galaxyData = game.GalaxyData;

        // Find a reachable system
        for (int i = 0; i < galaxyData.Count; i++)
        {
            int targetIdx = (currentIdx + 1 + i) % galaxyData.Count;
            if (targetIdx == currentIdx) continue;

            float distance = (galaxyData[currentIdx].GalaxyPosition - galaxyData[targetIdx].GalaxyPosition).Length();
            float fuelCost = distance * ShipConfig.FuelPerDistanceUnit;

            if (distance <= ShipConfig.FtlMaxRange && game.Player.ShipFuel >= fuelCost)
            {
                game.Player.TrySpendFuel(fuelCost);
                game.Player.CurrentStarSystemIndex = targetIdx;

                var sourceSystem = galaxyData[currentIdx];
                var targetSystem = galaxyData[targetIdx];

                // Reset per-system tracking
                _solarSystemTimer = 0;
                _stationsDockedInSystem = 0;
                _planetsLandedInSystem = 0;
                _systemsVisited++;
                _actionCooldown = ActionDelay;

                game.ChangeState(new FTLTransitionState(sourceSystem, targetSystem));
                return;
            }
        }

        // Can't jump — go back to exploring
        _solarGoal = SolarGoal.Explore;
    }

    // ════════════════════════════════════════════════════════════════
    //  PLANET SURFACE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Explores the planet surface: wanders, mines rocks, enters settlements, returns to ship.
    /// Called from PlanetSurfaceState.UpdateInput when autoplay is on.
    /// Returns true if the bot consumed input. Populates out parameters for state-level actions.
    /// </summary>
    public bool UpdatePlanetSurface(
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
            // Block all input when any overlay is open, so the bot doesn't do anything unexpected in the background.
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
            // Ensure we wait at least ActionDelay after the menu opens before acting
            if (!_starshipMenuWasOpen)
            {
                _actionCooldown = Math.Max(_actionCooldown, ActionDelay);
                _starshipMenuWasOpen = true;
            }

            // Pre-highlight the option the bot intends to pick so the player can see it
            bool willTakeOff = _surfaceTimer > SurfaceTakeoffTime || _surfaceGoal == SurfaceGoal.GoToShip;
            StarshipMenuOption preSelect = willTakeOff ? StarshipMenuOption.TakeOff : StarshipMenuOption.DisembarkOnFoot;
            int preSelectIdx = starshipMenu.FindMenuOptionIndex(preSelect);
            if (preSelectIdx >= 0) starshipMenu.MenuSelectedIndex = preSelectIdx;

            if (_actionCooldown <= 0)
            {
                if (willTakeOff)
                {
                    // Take off after exploring
                    action = PlanetSurfaceAction.TakeOff;
                    _statusAction = "Taking off";
                    _surfaceTimer = 0;
                    _surfaceGoal = SurfaceGoal.Explore;
                    _surfaceWanderTargetSet = false;
                }
                else
                {
                    // Disembark on foot
                    action = PlanetSurfaceAction.DisembarkOnFoot;
                    _statusAction = "Disembarking on foot";
                    _starshipMenuWasOpen = false; // reset so next open also waits
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
        if (_surfaceTimer > SurfaceReturnTime)
            _surfaceGoal = SurfaceGoal.GoToShip;
        else if (_surfaceSettlementsVisited < sim.SurfaceData.Settlements.Count && _surfaceTimer > SurfaceSettleStartTime && _surfaceTimer < SurfaceSettleEndTime)
            _surfaceGoal = SurfaceGoal.GoToSettlement;
        else
            _surfaceGoal = SurfaceGoal.Explore;

        _statusGoal = _surfaceGoal switch
        {
            SurfaceGoal.Explore => _surfaceExploreSubGoal switch
            {
                SurfaceExploreSubGoal.Enemy => "HUNTING ENEMY",
                SurfaceExploreSubGoal.Rock => "MINING ROCK",
                _ => "EXPLORING SURFACE"
            },
            SurfaceGoal.GoToSettlement => "HEADING TO SETTLEMENT",
            SurfaceGoal.GoToShip => "RETURNING TO SHIP",
            _ => "PLANET SURFACE"
        };

        // ── Check proximity interactions ──
        if (_actionCooldown <= 0)
        {
            if (sim.LocalNearSettlement != null && _surfaceGoal == SurfaceGoal.GoToSettlement && sim.LocalNearSettlement.Index == _surfaceSettlementsVisited)
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
        Vector2 targetPos = GetSurfaceTargetPosition(sim, simPlayer, avatarPos);
        int goalTileX = (int)(targetPos.X / WindowConfig.TileSize);
        int goalTileY = (int)(targetPos.Y / WindowConfig.TileSize);
        TilePos surfaceGoalTile = new(goalTileX, goalTileY);

        int fromTileX = (int)(avatarPos.X / WindowConfig.TileSize);
        int fromTileY = (int)(avatarPos.Y / WindowConfig.TileSize);
        TilePos surfaceFromTile = new(fromTileX, fromTileY);

        if (surfaceGoalTile != _surfacePathTarget)
        {
            _surfacePath.Clear();
            _surfacePath.AddRange(BfsSurfacePath(sim.SurfaceData, surfaceFromTile, surfaceGoalTile));
            _surfacePathTarget = surfaceGoalTile;
            _surfaceStuckTimer = 0;
        }

        // Trim reached waypoints
        while (_surfacePath.Count > 0)
        {
            Vector2 wpWorld = TilePosToWorld(_surfacePath[0]);
            if (Vector2.Distance(avatarPos, wpWorld) < WindowConfig.TileSize * 0.6f)
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
                // If we're trying to go to a settlement but get stuck, skip to the next one rather than getting permanently stuck
                _surfaceSettlementsVisited++;
                _statusAction = "Got stuck, skipping to next settlement";
            }
            else
            {
                _statusAction = "Got stuck, recalculating path";
            }
        }

        // Determine standoff behaviour for combat / mining sub-goals
        bool inCombatOrMining = _surfaceGoal == SurfaceGoal.Explore &&
            (_surfaceExploreSubGoal == SurfaceExploreSubGoal.Enemy ||
             _surfaceExploreSubGoal == SurfaceExploreSubGoal.Rock);
        float standoffDist = _surfaceExploreSubGoal == SurfaceExploreSubGoal.Enemy
            ? SurfaceEnemyStandoffDistance : SurfaceRockStandoffDistance;
        float distToActualTarget = Vector2.Distance(avatarPos, targetPos);
        bool withinStandoff = inCombatOrMining && distToActualTarget <= standoffDist;

        if (withinStandoff)
        {
            // At standoff range — hold position, aim at the actual target entity and shoot
            Vector2 toActual = targetPos - avatarPos;
            Vector2 aimDir = toActual.Length() > 0.1f ? Vector2.Normalize(toActual) : Vector2.UnitX;
            avatarInput.DesiredVelocity = Vector2.Zero;
            avatarInput.Shoot = true;
            avatarInput.AimDirection = aimDir;
            _statusAction = _surfaceExploreSubGoal == SurfaceExploreSubGoal.Enemy
                ? $"Shooting enemy (dist:{distToActualTarget:F0})"
                : $"Mining rock (dist:{distToActualTarget:F0})";
            // Clear path so it is recalculated when the target moves
            _surfacePath.Clear();
            _surfacePathTarget = new TilePos(-1, -1);
        }
        else if (dist > SurfaceWalkThreshold)
        {
            Vector2 dir = toTarget / dist;
            float speed = game.Player.AvatarWalkSpeed;
            avatarInput.DesiredVelocity = dir * speed;
            _statusAction = _surfacePath.Count > 0
                ? $"Path [{_surfacePath.Count} steps] dist:{dist:F0}"
                : $"Walking (dist: {dist:F0})";

            if (inCombatOrMining)
            {
                // Aim toward the actual target entity even while approaching
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

            // Pick new wander target
            _surfaceWanderTargetSet = false;
            _surfacePath.Clear();
            _surfacePathTarget = new TilePos(-1, -1);
        }

        return true;
    }

    private Vector2 GetSurfaceTargetPosition(PlanetSurfaceSimulation sim, SimulationPlayer simPlayer, Vector2 avatarPos)
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
            float wanderDist = SurfaceWanderDistance;
            float angle = _rng.NextSingle() * MathF.PI * 2f;
            _surfaceWanderTarget = avatarPos + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * wanderDist;
            _surfaceWanderTargetSet = true;
        }
        return _surfaceWanderTarget;
    }

    /// <summary>
    /// Returns the position of the nearest live pirate NPC within <paramref name="maxRange"/>,
    /// or null if none is found.
    /// </summary>
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

    /// <summary>
    /// Returns the position of the nearest mineable rock within <paramref name="maxRange"/>,
    /// or null if none is found.
    /// </summary>
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

    /// <summary>
    /// BFS on the planet surface tile grid returning waypoints from start to goal.
    /// Uses <see cref="SurfaceTerrainRules.IsTraversable"/> for walkability.
    /// Caps expansion at <paramref name="maxNodes"/> to keep large maps fast.
    /// Returns an empty list if no path is found within the budget.
    /// </summary>
    private static List<TilePos> BfsSurfacePath(PlanetSurfaceData surface, TilePos from, TilePos to,
        int maxNodes = 4000)
    {
        int w = surface.Width;
        int h = surface.Height;

        bool IsSurfaceWalkable(int x, int y) =>
            x >= 0 && x < w && y >= 0 && y < h &&
            SurfaceTerrainRules.IsTraversable(surface.Tiles[x, y]);

        // If goal is on impassable terrain, find nearest traversable neighbour
        if (!IsSurfaceWalkable(to.X, to.Y))
        {
            TilePos? adj = null;
            for (int r = 1; r <= 4 && adj == null; r++)
                for (int dy = -r; dy <= r && adj == null; dy++)
                    for (int dx = -r; dx <= r && adj == null; dx++)
                        if (Math.Abs(dx) == r || Math.Abs(dy) == r)
                            if (IsSurfaceWalkable(to.X + dx, to.Y + dy))
                                adj = new TilePos(to.X + dx, to.Y + dy);
            if (adj == null) return [];
            to = adj.Value;
        }

        if (from == to) return [];

        var prev = new Dictionary<TilePos, TilePos>();
        var queue = new Queue<TilePos>();
        prev[from] = from;
        queue.Enqueue(from);

        ReadOnlySpan<(int dx, int dy)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        while (queue.Count > 0 && prev.Count < maxNodes)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;
            foreach (var (dx, dy) in dirs)
            {
                var nb = new TilePos(cur.X + dx, cur.Y + dy);
                if (!prev.ContainsKey(nb) && IsSurfaceWalkable(nb.X, nb.Y))
                {
                    prev[nb] = cur;
                    queue.Enqueue(nb);
                }
            }
        }

        if (!prev.ContainsKey(to)) return [];

        var path = new List<TilePos>();
        var step = to;
        while (step != from)
        {
            path.Add(step);
            step = prev[step];
        }
        path.Reverse();
        return path;
    }

    // ════════════════════════════════════════════════════════════════
    //  INTERIOR
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks around the interior, visits interactables, then exits.
    /// Called from InteriorState.UpdateInput when autoplay is on.
    /// Returns true if the bot consumed input. Populates out parameter for action.
    /// </summary>
    public bool UpdateInterior(
        Game game,
        InteriorSimulation sim,
        SimulationPlayer simPlayer,
        StarshipMenuOverlay starshipMenu,
        InGameMenuOverlay inGameMenu,
        bool playerInsideShip,
        bool showingDialogue,
        bool anyOverlayOpen,
        out InteriorAction action)
    {
        action = InteriorAction.None;
        if (!Enabled) return false;

        float dt = game.DeltaTime;
        _interiorTimer += dt;
        _actionCooldown = Math.Max(0, _actionCooldown - dt);

        if (sim.EcsWorld.IsAlive(simPlayer.Entity) && anyOverlayOpen)
        {
            // Block all input when any overlay is open, so the bot doesn't do anything unexpected in the background.
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

        // ── Dismiss dialogue ──
        if (showingDialogue)
        {
            // Stop moving while reading
            if (sim.EcsWorld.IsAlive(simPlayer.Entity))
            {
                ref var frozenInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
                frozenInput.DesiredVelocity = Vector2.Zero;
            }

            _dialogueCooldown -= dt;
            if (_dialogueCooldown <= 0)
            {
                action = InteriorAction.DismissDialogue;
                _statusAction = "Reading dialogue...";
                _dialogueCooldown = DialogueLineDelay; // wait before next line
            }
            else
            {
                _statusAction = $"Reading dialogue ({_dialogueCooldown:F1}s)...";
            }
            return true;
        }

        // ── Handle starship menu ──
        if (starshipMenu.IsOpen)
        {
            // Ensure we wait at least ActionDelay after the menu opens before acting
            if (!_starshipMenuWasOpen)
            {
                _actionCooldown = Math.Max(_actionCooldown, ActionDelay);
                _starshipMenuWasOpen = true;
            }

            if (_actionCooldown <= 0)
            {
                if (_interiorTimer > InteriorTakeoffTime || _interiorGoal == InteriorGoal.Exit)
                {
                    action = InteriorAction.TakeOff;
                    _statusAction = "Taking off from interior";
                    _interiorTimer = 0;
                    _interiorGoal = InteriorGoal.VisitInteractables;
                    _interiorInteractableIndex = 0;
                    _interiorVisitOrder.Clear(); // reshuffle on next interior entry
                }
                else
                {
                    action = InteriorAction.DisembarkOnFoot;
                    _statusAction = "Disembarking on foot";
                    _starshipMenuWasOpen = false; // reset so next open also waits
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

        // ── Handle open service overlays — wait ActionDelay then close ──
        // The bot waits 3s (the interact cooldown) before closing so the overlay
        // has time to process/display before being dismissed.
        if (anyOverlayOpen)
        {
            if (sim.EcsWorld.IsAlive(simPlayer.Entity))
            {
                ref var frozenInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
                frozenInput.DesiredVelocity = Vector2.Zero;
            }
            if (_actionCooldown <= 0)
            {
                action = InteriorAction.CloseAnyOverlay;
                _statusAction = "Closing overlay";
                _actionCooldown = ActionDelay; // wait before the proximity check can re-fire
            }
            else
            {
                _statusAction = $"Viewing overlay ({_actionCooldown:F1}s)...";
            }
            return true;
        }

        // ── Inside ship — open starship menu ──
        if (playerInsideShip)
        {
            action = InteriorAction.OpenStarshipMenu;
            return true;
        }

        var world = sim.EcsWorld;
        if (!world.IsAlive(simPlayer.Entity))
            return true;

        ref var avatarTf = ref world.Get<Transform>(simPlayer.Entity);

        // ── Decide what to do ──
        if (_interiorTimer > InteriorExitTime)
            _interiorGoal = InteriorGoal.Exit;

        _statusGoal = _interiorGoal switch
        {
            InteriorGoal.VisitInteractables => "VISITING INTERACTABLES",
            InteriorGoal.Exit => "HEADING TO EXIT",
            _ => "INTERIOR"
        };

        // ── Check proximity interactions ──
        if (_actionCooldown <= 0)
        {
            if (_interiorGoal == InteriorGoal.Exit)
            {
                // Try to find exit door
                if (sim.NearestInteractable?.Type == InteractableType.ExitDoor)
                {
                    action = InteriorAction.Interact;
                    _statusAction = "Using exit door";
                    _actionCooldown = ActionDelay;
                    return true;
                }

                // If near ship, board it
                if (sim.NearShip)
                {
                    action = InteriorAction.BoardShip;
                    _statusAction = "Boarding ship";
                    _actionCooldown = ActionDelay;
                    return true;
                }
            }
            else
            {
                // Only interact with the interactable / NPC that is the *current* goal —
                // ignore anything the avatar happens to walk past on the way there.
                TilePos targetTile = GetInteriorGoalTile(sim);

                // Visit interactables
                if (sim.NearestInteractable != null)
                {
                    var interactable = sim.NearestInteractable;
                    if (interactable.TilePos != _lastInteractedTilePos &&
                        interactable.TilePos == targetTile)
                    {
                        action = InteriorAction.Interact;
                        _statusAction = $"Using {interactable.Type}";
                        _actionCooldown = ActionDelay;
                        _lastInteractedTilePos = interactable.TilePos;
                        _interiorInteractableIndex++; // advance so next task is different
                        _interiorPathTarget = new TilePos(-1, -1); // force path recompute
                        return true;
                    }
                }

                // Talk to NPCs
                if (sim.NearestNpc != null &&
                    sim.NearestNpc.TilePos != _lastInteractedTilePos &&
                    sim.NearestNpc.TilePos == targetTile)
                {
                    action = InteriorAction.TalkToNpc;
                    _statusAction = $"Talking to {sim.NearestNpc.Name}";
                    _actionCooldown = ActionDelay;
                    _dialogueCooldown = DialogueLineDelay; // first line also waits the full delay
                    _lastInteractedTilePos = sim.NearestNpc.TilePos;
                    _interiorInteractableIndex++; // advance so bot doesn't keep talking to same NPC
                    _interiorPathTarget = new TilePos(-1, -1);
                    return true;
                }
            }
        }

        // ── Move toward goal via pathfinding ──
        ref var avatarInput = ref world.Get<AvatarInputComponent>(simPlayer.Entity);

        TilePos goalTile = GetInteriorGoalTile(sim);
        int fromTileX = (int)(avatarTf.Position.X / WindowConfig.TileSize);
        int fromTileY = (int)(avatarTf.Position.Y / WindowConfig.TileSize);
        TilePos fromTile = new(fromTileX, fromTileY);

        // Recompute path when target changed
        if (goalTile != _interiorPathTarget)
        {
            _interiorPath.Clear();
            _interiorPath.AddRange(BfsInteriorPath(sim.Interior, fromTile, goalTile));
            _interiorPathTarget = goalTile;
            _interiorStuckTimer = 0;
        }

        // Trim already-reached waypoints from front of path
        while (_interiorPath.Count > 0)
        {
            Vector2 wpWorld = TilePosToWorld(_interiorPath[0]);
            if (Vector2.Distance(avatarTf.Position, wpWorld) < WindowConfig.TileSize * 0.1f)
            {
                _interiorPath.RemoveAt(0);
                _interiorStuckTimer = 0;
            }
            else
                break;
        }

        if (_interiorPath.Count > 0)
        {
            Vector2 nextWp = TilePosToWorld(_interiorPath[0]);
            Vector2 toNext = nextWp - avatarTf.Position;
            float dist = toNext.Length();
            Vector2 dir = dist > 0.1f ? toNext / dist : Vector2.Zero;
            avatarInput.DesiredVelocity = dir * game.Player.AvatarWalkSpeed;
            _statusAction = $"Path [{_interiorPath.Count} steps] dist:{dist:F0}";

            // Stuck detection: if we haven't made progress, recompute path
            _interiorStuckTimer += game.DeltaTime;
            if (_interiorStuckTimer > StuckTimeout)
            {
                _interiorPath.Clear(); // force recompute next frame
                _interiorStuckTimer = 0;
                if (_interiorGoal != InteriorGoal.Exit)
                {
                    // If we're trying to visit interactables but get stuck, skip to the next one rather than getting permanently stuck
                    _interiorInteractableIndex++;
                    _statusAction = "Got stuck, skipping to next target";
                }
                else
                {
                    _statusAction = "Got stuck, recalculating path";
                }
            }
        }
        else
        {
            // At destination (or no path found — try direct walk)
            Vector2 goalWorld = TilePosToWorld(goalTile);
            Vector2 toDirect = goalWorld - avatarTf.Position;
            float directDist = toDirect.Length();
            if (directDist > WindowConfig.TileSize * 0.1f)
            {
                avatarInput.DesiredVelocity = Vector2.Normalize(toDirect) * game.Player.AvatarWalkSpeed;
                _statusAction = $"Direct walk (dist:{directDist:F0})";
            }
            else if (_actionCooldown <= 0)
            {
                // Arrived at goal tile — interact then wait before moving on
                avatarInput.DesiredVelocity = Vector2.Zero;
                if (_interiorGoal != InteriorGoal.Exit)
                {
                    action = InteriorAction.Interact;
                    _statusAction = "Interacting at target";
                }
                else
                {
                    _statusAction = "At exit, waiting...";
                }
                _actionCooldown = ActionDelay;
                _interiorInteractableIndex++;
                _interiorPathTarget = new TilePos(-1, -1); // force recompute for next target
            }
            else
            {
                // Waiting after interaction before moving on
                avatarInput.DesiredVelocity = Vector2.Zero;
                _statusAction = $"Waiting after interact ({_actionCooldown:F1}s)...";
            }
            _interiorStuckTimer = 0;
        }

        return true;
    }

    private Vector2 TilePosToWorld(TilePos tile) =>
        new Vector2((tile.X + 0.5f) * WindowConfig.TileSize, (tile.Y + 0.5f) * WindowConfig.TileSize);

    private TilePos GetInteriorGoalTile(InteriorSimulation sim)
    {
        var interior = sim.Interior;

        if (_interiorGoal == InteriorGoal.Exit)
        {
            foreach (var interactable in interior.Interactables)
                if (interactable.Type == InteractableType.ExitDoor)
                    return interactable.TilePos;
            if (interior.LandingPadTilePos.HasValue)
                return interior.LandingPadTilePos.Value;
        }

        int totalItems = interior.Interactables.Count + interior.Npcs.Count;

        // Build and shuffle the visit order the first time (or after a reset)
        if (_interiorVisitOrder.Count == 0 && totalItems > 0)
        {
            for (int i = 0; i < totalItems; i++)
                _interiorVisitOrder.Add(i);
            // Fisher-Yates shuffle
            for (int i = _interiorVisitOrder.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_interiorVisitOrder[i], _interiorVisitOrder[j]) = (_interiorVisitOrder[j], _interiorVisitOrder[i]);
            }
        }

        if (_interiorInteractableIndex < _interiorVisitOrder.Count)
        {
            int mappedIndex = _interiorVisitOrder[_interiorInteractableIndex];
            if (mappedIndex < interior.Interactables.Count)
                return interior.Interactables[mappedIndex].TilePos;
            int npcIndex = mappedIndex - interior.Interactables.Count;
            if (npcIndex < interior.Npcs.Count)
                return interior.Npcs[npcIndex].TilePos;
        }

        // Nothing left — head to exit
        _interiorGoal = InteriorGoal.Exit;
        return GetInteriorGoalTile(sim);
    }

    /// <summary>
    /// BFS on the interior tile grid returning a list of tile waypoints from start to goal.
    /// Returns an empty list if no path is found.
    /// </summary>
    private static List<TilePos> BfsInteriorPath(InteriorData interior, TilePos from, TilePos to)
    {
        int w = interior.Width;
        int h = interior.Height;

        if (!IsWalkable(interior, to.X, to.Y))
        {
            // Goal is impassable (e.g., NPC/interactable tile); find the nearest walkable neighbour
            TilePos? adj = FindNearestWalkableAdjacentTo(interior, to.X, to.Y);
            if (adj == null) return [];
            to = adj.Value;
        }

        if (from == to) return [];

        var prev = new Dictionary<TilePos, TilePos>();
        var queue = new Queue<TilePos>();
        prev[from] = from;
        queue.Enqueue(from);

        // 4-directional BFS
        Span<(int dx, int dy)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;

            foreach (var (dx, dy) in dirs)
            {
                var next = new TilePos(cur.X + dx, cur.Y + dy);
                if (next.X < 0 || next.X >= w || next.Y < 0 || next.Y >= h) continue;
                if (prev.ContainsKey(next)) continue;
                if (!IsWalkable(interior, next.X, next.Y)) continue;
                prev[next] = cur;
                queue.Enqueue(next);
            }
        }

        if (!prev.ContainsKey(to)) return []; // no path

        // Reconstruct path (skip the starting tile)
        var path = new List<TilePos>();
        var step = to;
        while (step != from)
        {
            path.Add(step);
            step = prev[step];
        }
        path.Reverse();
        return path;
    }

    private static bool IsWalkable(InteriorData interior, int x, int y)
    {
        if (x < 0 || x >= interior.Width || y < 0 || y >= interior.Height) return false;
        return interior.Tiles[x, y] switch
        {
            InteriorTileType.Void => false,
            InteriorTileType.Wall => false,
            InteriorTileType.Crate => false,
            InteriorTileType.Table => false,
            InteriorTileType.Plant => false,
            InteriorTileType.Window => false,
            InteriorTileType.Pipe => false,
            InteriorTileType.Shelf => false,
            InteriorTileType.Bed => false,
            InteriorTileType.BarCounter => false,
            InteriorTileType.Generator => false,
            InteriorTileType.Antenna => false,
            _ => true,
        };
    }

    private static TilePos? FindNearestWalkableAdjacentTo(InteriorData interior, int tx, int ty)
    {
        for (int r = 1; r <= 3; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Math.Abs(dx) == r || Math.Abs(dy) == r)
                        if (IsWalkable(interior, tx + dx, ty + dy))
                            return new TilePos(tx + dx, ty + dy);
        return null;
    }
}

// ── Action enums returned by bot to states ──────────────────────

public enum PlanetSurfaceAction
{
    None,
    TakeOff,
    DisembarkOnFoot,
    DisembarkOnVehicle,
    EnterSettlement,
    BoardShip,
}

public enum InteriorAction
{
    None,
    TakeOff,
    DisembarkOnFoot,
    Interact,
    TalkToNpc,
    DismissDialogue,
    BoardShip,
    OpenStarshipMenu,
    CloseAnyOverlay,
}
