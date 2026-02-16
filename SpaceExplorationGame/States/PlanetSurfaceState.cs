using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Movement;
using SpaceExplorationGame.ECS.Systems.AI;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.UI.Overlays;

namespace SpaceExplorationGame.States;

/// <summary>
/// Planet surface state: Top-down tilemap view where the player can walk/drive on a planet's surface.
/// </summary>
public class PlanetSurfaceState : GameState
{
    public override GameStateType Type => GameStateType.PlanetSurface;

    private readonly StarSystemData _starSystem;
    private readonly PlanetData _planet;
    private PlanetSurfaceData _surfaceData = null!;

    private Entity _playerAvatar;
    private Entity _shipEntity;
    private Entity _vehicleEntity;
    private const float BaseAvatarSpeed = 200f;
    private const float BoardShipRadius = 30f;

    // Vehicle state
    private bool _inVehicle;
    private bool _vehicleDeployed;

    // ECS Systems
    private PlayerMovementSystem _movementSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private VehicleMovementSystem? _vehicleMovementSystem;

    // Combat systems
    private VelocitySystem _velocitySystem = null!;
    private ProjectileSystem _projectileSystem = null!;
    private SurfaceEnemyAISystem _surfaceAISystem = null!;
    private readonly List<DamagePopup> _damagePopups = [];
    private readonly List<Explosion> _explosions = [];
    private float _playerFireCooldown;
    private Vector2 _lastMoveDir = new(0, -1); // default facing up

    // Combat HUD
    private string? _combatMessage;
    private float _combatMessageTimer;

    // Death handling
    private bool _playerDead;
    private float _respawnTimer;
    private const float RespawnDelay = 2.5f;

    // Settlement proximity tracking
    private SettlementData? _nearSettlement;

    // Ship proximity tracking (for boarding prompt)
    private bool _nearShip;

    // Vehicle proximity tracking (for mount prompt)
    private bool _nearVehicle;

    // In-game menu overlay
    private readonly InGameMenuOverlay _inGameMenuOverlay = new() { StateType = GameStateType.PlanetSurface };

    // Starship menu overlay (shown on landing and when boarding)
    private readonly StarshipMenuOverlay _starshipMenuOverlay = new();
    private bool _playerInsideShip = true; // player starts inside the ship

    // Takeoff animation
    private bool _isTakingOff;
    private float _takeoffTimer;
    private const float TakeoffDuration = 2f;

    // Landing animation
    private bool _isLanding;
    private float _landingTimer;
    private const float LandingDuration = 2f;

    // Landing site (tile coordinates, -1 = default center)
    private readonly int _landingTileX;
    private readonly int _landingTileY;

    public PlanetSurfaceState(StarSystemData starSystem, PlanetData planet, int landingTileX = -1, int landingTileY = -1)
    {
        _starSystem = starSystem;
        _planet = planet;
        _landingTileX = landingTileX;
        _landingTileY = landingTileY;
    }

    /// <summary>Helper: mount the player into their vehicle, creating movement system.</summary>
    private void MountVehicle(Game game)
    {
        ref var avatarTransform = ref game.EcsWorld.Get<Transform>(_playerAvatar);
        if (!_vehicleDeployed)
        {
            // Deploy vehicle at player (ship) position
            var shipTf = game.EcsWorld.Get<Transform>(_shipEntity);
            _vehicleEntity = EntityFactory.CreateVehicle(game.EcsWorld, shipTf.Position.X, shipTf.Position.Y);
            _vehicleDeployed = true;
        }
        ref var vTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
        avatarTransform.Position = vTf.Position;
        avatarTransform.Rotation = vTf.Rotation;
        var vStats = game.Player.GetCombinedVehicleStats();
        _vehicleMovementSystem = new VehicleMovementSystem(
            game.EcsWorld, game.Input, _playerAvatar,
            acceleration: vStats.Acceleration > 0 ? vStats.Acceleration : GameConfig.VehicleAcceleration,
            maxSpeed: vStats.MaxSpeed > 0 ? vStats.MaxSpeed : GameConfig.VehicleMaxSpeed,
            rotationSpeed: vStats.RotationSpeed > 0 ? vStats.RotationSpeed : GameConfig.VehicleRotationSpeed,
            friction: GameConfig.VehicleFriction + vStats.Friction);
        _vehicleMovementSystem.CanMoveTo = CanMoveToTerrain;
        _inVehicle = true;
        game.Player.InVehicle = true;
    }

    public override void Enter(Game game)
    {
        // Generate planet surface
        var rng = game.Seeds.GetPlanetSurfaceRandom(_starSystem.Index, _planet.Index);
        _surfaceData = PlanetSurfaceGenerator.Generate(rng, _planet);

        // Place player avatar at landing zone (use chosen site or default center)
        int lzTileX = _landingTileX >= 0 ? _landingTileX : _surfaceData.LandingZone.X;
        int lzTileY = _landingTileY >= 0 ? _landingTileY : _surfaceData.LandingZone.Y;
        float lzX = lzTileX * GameConfig.TileSize;
        float lzY = lzTileY * GameConfig.TileSize;

        // Calculate avatar speed from equipped parts
        float avatarSpeed = BaseAvatarSpeed + game.Player.GetCombinedAvatarStats().WalkSpeed;

        // Calculate avatar health from equipment
        game.Player.RecalculateAvatarStats();
        float maxHp = game.Player.AvatarMaxHealth;
        float curHp = game.Player.AvatarHealth;

        // Check if we have saved surface positions (returning from a settlement)
        float shipX, shipY;
        float playerStartX, playerStartY;
        if (game.Player.HasSavedSurfacePositions)
        {
            // Restore saved positions
            shipX = game.Player.SavedShipX;
            shipY = game.Player.SavedShipY;
            playerStartX = game.Player.SavedPlayerX;
            playerStartY = game.Player.SavedPlayerY;

            _vehicleDeployed = game.Player.SavedVehicleDeployed;
            _inVehicle = game.Player.SavedPlayerInVehicle;
            _playerInsideShip = false; // player was already outside when they entered the settlement
        }
        else
        {
            // Fresh landing: ship at landing zone, vehicle starts inside ship
            shipX = lzX + 30;
            shipY = lzY;
            playerStartX = lzX;
            playerStartY = lzY;
            _vehicleDeployed = false;
            _inVehicle = false;
            _playerInsideShip = true; // player starts inside the ship
        }

        _playerAvatar = EntityFactory.CreatePlayerAvatar(game.EcsWorld, playerStartX, playerStartY, avatarSpeed,
            maxHealth: maxHp, currentHealth: curHp);

        // Place ship
        _shipEntity = EntityFactory.CreateLandedShip(game.EcsWorld, shipX, shipY);

        // Notify mission system of planet landing
        game.Player.NotifyPlanetLanded(_starSystem.Index, _planet.Index);

        // Deploy vehicle if it was deployed before entering settlement
        if (_vehicleDeployed)
        {
            _vehicleEntity = EntityFactory.CreateVehicle(game.EcsWorld,
                game.Player.HasSavedSurfacePositions ? game.Player.SavedVehicleX : shipX - 30,
                game.Player.HasSavedSurfacePositions ? game.Player.SavedVehicleY : shipY);
        }

        // Initialize ECS systems
        _movementSystem = new PlayerMovementSystem(game.EcsWorld, game.Input, avatarSpeed);
        _movementSystem.CanMoveTo = MakeTerrainCollisionCheck();
        _movementSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(game.EcsWorld, game.Camera);
        _cameraFollowSystem.Initialize();

        // Camera
        game.Camera.Position = new Vector2(playerStartX, playerStartY);
        game.Camera.Zoom = 1.5f;

        // Open starship menu if this is a fresh landing (not returning from settlement)
        if (_playerInsideShip)
        {
            // Start landing animation for fresh landings
            _isLanding = true;
            _landingTimer = 0f;
        }
        else if (_inVehicle && _vehicleDeployed)
        {
            // Returning from settlement while in vehicle — mount vehicle
            MountVehicle(game);
        }

        // Clear saved positions now that we've used them
        game.Player.ClearSavedSurfacePositions();

        // Combat systems
        _velocitySystem = new VelocitySystem(game.EcsWorld);
        _velocitySystem.Initialize();
        _projectileSystem = new ProjectileSystem(game.EcsWorld);
        _projectileSystem.Initialize();
        _surfaceAISystem = new SurfaceEnemyAISystem(
            game.EcsWorld,
            () => game.EcsWorld.IsAlive(_playerAvatar) ? game.EcsWorld.Get<Transform>(_playerAvatar).Position : Vector2.Zero,
            () => game.EcsWorld.IsAlive(_playerAvatar) && !_playerDead,
            CanMoveToTerrain);
        _surfaceAISystem.Initialize();

        // Spawn fauna
        foreach (var (fx, fy, angle) in _surfaceData.FaunaSpawns)
        {
            EntityFactory.CreateFauna(game.EcsWorld, new Vector2(fx, fy), angle);
        }

        // Spawn bandits
        foreach (var (bx, by, angle) in _surfaceData.BanditSpawns)
        {
            EntityFactory.CreateBandit(game.EcsWorld, new Vector2(bx, by), angle);
        }

        // Spawn mineable rocks
        foreach (var (rx, ry, resource, amount, size, hp) in _surfaceData.RockSpawns)
        {
            EntityFactory.CreateSurfaceRock(game.EcsWorld, new Vector2(rx, ry), size, hp, resource, amount);
        }
    }

    public override void Exit(Game game)
    {
        // Persist avatar health back to PlayerData
        if (!_playerDead && game.EcsWorld.IsAlive(_playerAvatar) && game.EcsWorld.Has<Health>(_playerAvatar))
        {
            var health = game.EcsWorld.Get<Health>(_playerAvatar);
            game.Player.AvatarHealth = health.Hull;
        }
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void UpdateInput(Game game)
    {
        // Block all input during animations
        if (_isTakingOff || _isLanding) return;

        // Starship menu overlay (highest priority)
        if (_starshipMenuOverlay.UpdateInput(game))
        {
            // Check if the player made a choice
            if (_starshipMenuOverlay.LastChoice.HasValue)
            {
                HandleStarshipMenuChoice(game, _starshipMenuOverlay.LastChoice.Value);
            }
            return;
        }

        // In-game menu overlay (handles Escape toggle + menu navigation)
        if (_inGameMenuOverlay.UpdateInput(game))
            return;

        var input = game.Input;

        // Get player position for proximity checks
        ref var avatarTransform = ref game.EcsWorld.Get<Transform>(_playerAvatar);

        // Unified interaction with E key (priority: ship > vehicle > settlement)
        if (input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_inVehicle)
            {
                if (_nearShip)
                {
                    // In vehicle near ship → store vehicle back in ship, board ship
                    ref var vehicleTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
                    avatarTransform.Position = vehicleTf.Position;
                    avatarTransform.Rotation = 0f;
                    _vehicleMovementSystem!.Speed = 0f;
                    _inVehicle = false;
                    game.Player.InVehicle = false;

                    // Remove vehicle from the map
                    if (game.EcsWorld.IsAlive(_vehicleEntity))
                        game.EcsWorld.Destroy(_vehicleEntity);
                    _vehicleDeployed = false;

                    BoardShip(game);
                }
                else
                {
                    // Dismount vehicle: place avatar next to vehicle, reset rotation
                    ref var vehicleTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
                    avatarTransform.Position = vehicleTf.Position + new Vector2(20, 0);
                    avatarTransform.Rotation = 0f;
                    _vehicleMovementSystem!.Speed = 0f;
                    _inVehicle = false;
                    game.Player.InVehicle = false;
                }
            }
            else if (_nearShip)
            {
                // Board ship on foot → show starship menu
                BoardShip(game);
            }
            else if (_nearVehicle && _vehicleDeployed)
            {
                // Mount vehicle
                MountVehicle(game);
            }
            else if (_nearSettlement != null)
            {
                // Enter settlement interior — save positions first
                SaveSurfacePositions(game);
                game.ChangeState(new InteriorState(
                    InteriorOrigin.Settlement, _starSystem,
                    planet: _planet, settlement: _nearSettlement));
                return;
            }
        }

        // Camera zoom (handled per-frame so scroll events aren't missed)
        if (input.MouseWheelY != 0)
        {
            game.Camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            game.Camera.ClampZoom();
        }

        // Track player facing direction from movement input
        Vector2 moveDir = Vector2.Zero;
        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up)) moveDir.Y -= 1;
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down)) moveDir.Y += 1;
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left)) moveDir.X -= 1;
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right)) moveDir.X += 1;
        if (moveDir != Vector2.Zero) _lastMoveDir = Vector2.Normalize(moveDir);
    }

    /// <summary>Board the ship: open the starship menu overlay.</summary>
    private void BoardShip(Game game)
    {
        _playerInsideShip = true;
        _starshipMenuOverlay.HasVehicle = game.Player.HasVehicle;
        _starshipMenuOverlay.VehicleDeployed = _vehicleDeployed;
        _starshipMenuOverlay.Open();
    }

    /// <summary>Handle the player's choice from the starship menu.</summary>
    private void HandleStarshipMenuChoice(Game game, StarshipMenuOption choice)
    {
        switch (choice)
        {
            case StarshipMenuOption.FlyToSpace:
                // Start takeoff animation instead of immediately changing state
                _isTakingOff = true;
                _takeoffTimer = 0f;
                _playerInsideShip = true;
                break;

            case StarshipMenuOption.DisembarkOnFoot:
                _playerInsideShip = false;
                // Place avatar next to ship
                ref var shipTf = ref game.EcsWorld.Get<Transform>(_shipEntity);
                ref var avatarTf = ref game.EcsWorld.Get<Transform>(_playerAvatar);
                avatarTf.Position = shipTf.Position + new Vector2(30, 0);
                avatarTf.Rotation = 0f;
                break;

            case StarshipMenuOption.DisembarkOnVehicle:
                _playerInsideShip = false;
                MountVehicle(game);
                break;
        }
    }

    /// <summary>Save the positions of ship, vehicle, and player before entering a settlement.</summary>
    private void SaveSurfacePositions(Game game)
    {
        var shipTf = game.EcsWorld.Get<Transform>(_shipEntity);
        float vehicleX = 0, vehicleY = 0;
        if (_vehicleDeployed)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            vehicleX = vehicleTf.Position.X;
            vehicleY = vehicleTf.Position.Y;
        }
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        game.Player.SaveSurfacePositions(
            shipTf.Position.X, shipTf.Position.Y,
            vehicleX, vehicleY, _vehicleDeployed,
            avatarTf.Position.X, avatarTf.Position.Y,
            _inVehicle);
    }

    public override void Update(Game game, float dt)
    {
        // Landing animation
        if (_isLanding)
        {
            _landingTimer += dt;
            if (_landingTimer >= LandingDuration)
            {
                _isLanding = false;
                // Now open the starship menu
                _starshipMenuOverlay.HasVehicle = game.Player.HasVehicle;
                _starshipMenuOverlay.Open();
            }
            _cameraFollowSystem.Update(in dt);
            return;
        }

        // Takeoff animation
        if (_isTakingOff)
        {
            _takeoffTimer += dt;
            if (_takeoffTimer >= TakeoffDuration)
            {
                game.Player.InVehicle = false;
                game.Player.ClearSavedSurfacePositions();
                game.ChangeState(new SolarSystemState(_starSystem));
                return;
            }
            // Camera follows ship during takeoff, zoom out gradually
            _cameraFollowSystem.Update(in dt);
            return;
        }

        // Starship menu or in-game menu active — no simulation
        if (_starshipMenuOverlay.IsOpen || _inGameMenuOverlay.IsOpen)
            return;

        if (_inVehicle)
        {
            // Vehicle movement (thrust/rotation)
            _vehicleMovementSystem!.Update(in dt);
        }
        else
        {
            // Normal avatar movement (4-way WASD)
            _movementSystem.Update(in dt);
        }

        // Move all entities with velocity (projectiles, etc.)
        _velocitySystem.Update(in dt);

        // Camera follows player + handles zoom
        _cameraFollowSystem.Update(in dt);

        // Get player position for proximity checks
        ref var avatarTransform = ref game.EcsWorld.Get<Transform>(_playerAvatar);

        // Keep vehicle position and rotation synced when driving
        if (_inVehicle && _vehicleDeployed)
        {
            ref var vehicleTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
            vehicleTf.Position = avatarTransform.Position;
            vehicleTf.Rotation = avatarTransform.Rotation;
        }

        // Check settlement proximity
        _nearSettlement = null;
        foreach (var settlement in _surfaceData.Settlements)
        {
            float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * GameConfig.TileSize;
            float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * GameConfig.TileSize;
            float distToSettlement = Vector2.Distance(avatarTransform.Position, new Vector2(sx, sy));
            float settlementRadius = Math.Max(settlement.TileRect.Width, settlement.TileRect.Height) * GameConfig.TileSize / 2f + 20f;
            if (distToSettlement < settlementRadius)
            {
                _nearSettlement = settlement;
                break;
            }
        }

        // Check ship proximity
        var shipTransform = game.EcsWorld.Get<Transform>(_shipEntity);
        float distToShip = Vector2.Distance(avatarTransform.Position, shipTransform.Position);
        _nearShip = distToShip < BoardShipRadius;

        // Check vehicle proximity
        if (_vehicleDeployed)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            float distToVehicle = Vector2.Distance(avatarTransform.Position, vehicleTf.Position);
            _nearVehicle = distToVehicle < GameConfig.VehicleMountRadius;
        } 
        else        
        {
            _nearVehicle = false;
        }

        // ── Combat ─────────────────────────────────────────────────

        if (!_playerDead)
        {
            // Player shooting (Space key or left mouse button)
            _playerFireCooldown -= dt;
            var input = game.Input;
            if ((input.IsKeyDown(SDL.Scancode.Space) || input.IsMouseDown(1)) && _playerFireCooldown <= 0 && !_inVehicle)
            {
                var avatarStats = game.Player.GetCombinedAvatarStats();
                float weaponDamage = GameConfig.BaseAvatarWeaponDamage + avatarStats.WeaponDamage;

                _playerFireCooldown = GameConfig.AvatarFireRate;

                // Aim direction: use mouse if mouse button, else last movement direction
                Vector2 aimDir;
                if (input.IsMouseDown(1))
                {
                    var mouseWorld = game.Camera.ScreenToWorld(new Vector2(input.MouseX, input.MouseY));
                    aimDir = Vector2.Normalize(mouseWorld - avatarTransform.Position);
                    if (float.IsNaN(aimDir.X)) aimDir = _lastMoveDir;
                }
                else
                {
                    aimDir = _lastMoveDir;
                }

                var spawnPos = avatarTransform.Position + aimDir * 14f;
                EntityFactory.CreateProjectile(game.EcsWorld, spawnPos, aimDir,
                    weaponDamage, GameConfig.AvatarProjectileSpeed, Faction.Player,
                    new Color3(100, 255, 100), GameConfig.AvatarProjectileLifetime);
            }

            // Surface enemy AI
            _surfaceAISystem.Update(in dt);
        }

        // Projectile system (collisions)
        _projectileSystem.Update(in dt);

        // Process damage events
        CombatHelper.CreateDamagePopups(_damagePopups, _projectileSystem.DamageEventsLastUpdate);

        // Process destroyed entities
        var combatRng = new SeededRandom((ulong)(game.GlobalTime * 1000) ^ 0xBEEFCAFE);
        foreach (var destroyed in _projectileSystem.DestroyedLastUpdate)
        {
            if (destroyed.Asteroid.HasValue)
            {
                // Mineable rock destroyed — collect resources only if player mined it
                var rock = destroyed.Asteroid.Value;
                _explosions.Add(new Explosion(destroyed.Position, 12f, new Color3(140, 120, 100), 0.4f));

                if (destroyed.KillerFaction == Faction.Player)
                {
                    int added = game.Player.AddCargo(rock.Resource, rock.ResourceAmount);
                    var resInfo = ResourceCatalog.Get(rock.Resource);
                    if (added > 0)
                    {
                        _combatMessage = $"+{added} {resInfo.Name.ToUpper()}";
                        _combatMessageTimer = 2.5f;

                        // Track resource mining for missions
                        game.Player.NotifyResourceMined(rock.Resource, added);
                    }
                    else
                    {
                        _combatMessage = "CARGO FULL!";
                        _combatMessageTimer = 2.5f;
                    }
                }

                if (game.EcsWorld.IsAlive(destroyed.Entity))
                    game.EcsWorld.Destroy(destroyed.Entity);
            }
            else if (destroyed.Faction == Faction.Player)
            {
                // Player avatar died
                HandleAvatarDeath(game, destroyed.Position);
            }
            else
            {
                // Enemy died
                _explosions.Add(new Explosion(destroyed.Position, 15f,
                    new Color3(
                        destroyed.Faction == Faction.Fauna ? (byte)200 : (byte)255,
                        destroyed.Faction == Faction.Fauna ? (byte)80 : (byte)150,
                        destroyed.Faction == Faction.Fauna ? (byte)60 : (byte)50), 0.6f));

                if (destroyed.KillerFaction == Faction.Player && destroyed.Loot.HasValue)
                {
                    _combatMessage = CombatHelper.ProcessLootDrop(game, destroyed.Loot.Value, combatRng);
                    _combatMessageTimer = 3f;
                }

                if (game.EcsWorld.IsAlive(destroyed.Entity))
                    game.EcsWorld.Destroy(destroyed.Entity);
            }
        }

        // Death respawn timer
        if (_playerDead)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0)
            {
                // Return to solar system
                game.Player.AvatarHealth = game.Player.AvatarMaxHealth; // restore health
                game.ChangeState(new SolarSystemState(_starSystem));
                return;
            }
        }

        // Sync avatar health back to PlayerData
        if (!_playerDead && game.EcsWorld.IsAlive(_playerAvatar) && game.EcsWorld.Has<Health>(_playerAvatar))
        {
            var health = game.EcsWorld.Get<Health>(_playerAvatar);
            game.Player.AvatarHealth = health.Hull;
        }

        // Combat message timer
        CombatHelper.UpdateCombatMessageTimer(ref _combatMessage, ref _combatMessageTimer, dt);

        // Update visual effects
        CombatHelper.UpdateVisualEffects(_damagePopups, _explosions, dt);
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = game.Camera;

        // Draw terrain tiles
        PlanetSurfaceRenderer.RenderTerrain(renderer, camera, _surfaceData);

        // Draw settlements
        SettlementRenderer.Render(renderer, camera, _surfaceData);

        // Draw mission markers on settlements
        HudRenderer.RenderPlanetSurfaceMissionMarkers(renderer, camera,
            game.Player, (float)game.GlobalTime, _starSystem.Index, _planet.Index,
            _surfaceData.Settlements);

        // Draw ship
        var shipTf = game.EcsWorld.Get<Transform>(_shipEntity);
        float shipScale = 1f;
        if (_isTakingOff)
        {
            float progress = Math.Clamp(_takeoffTimer / TakeoffDuration, 0f, 1f);
            shipScale = 1f + progress * 2f; // scale from 1.0 up to 3.0
        }
        else if (_isLanding)
        {
            float progress = Math.Clamp(_landingTimer / LandingDuration, 0f, 1f);
            shipScale = 3f - progress * 2f; // scale from 3.0 down to 1.0
        }
        int baseSpriteSize = game.Player.CurrentShipType.SpriteSize;
        int scaledSpriteSize = (int)(baseSpriteSize * shipScale);
        game.SpaceshipRenderer.RenderLanded(renderer, camera, shipTf.Position,
            game.Player.CurrentShipType.Id, scaledSpriteSize);

        // Draw vehicle
        if (_vehicleDeployed)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            game.VehicleRenderer.Render(renderer, camera, vehicleTf.Position,
                vehicleTf.Rotation, _inVehicle);
        }

        // Draw player avatar (only when on foot, alive, and not inside ship)
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        if (!_inVehicle && !_playerDead && !_playerInsideShip)
        {
            game.AvatarRenderer.Render(renderer, camera, avatarTf.Position);
        }

        // Draw mineable rocks
        SurfaceRockRenderer.RenderRocks(renderer, camera, game.EcsWorld);

        // Draw surface enemies (fauna + bandits)
        SurfaceEnemyRenderer.RenderEnemies(renderer, camera, game.EcsWorld);

        // Draw projectiles
        ProjectileRenderer.RenderProjectiles(renderer, camera, game.EcsWorld);

        // Draw damage popups and explosions
        ProjectileRenderer.RenderDamageEffects(renderer, camera, _damagePopups);
        ProjectileRenderer.RenderExplosions(renderer, camera, _explosions);

        // Interaction prompts (only when alive and not inside ship)
        if (!_playerDead && !_playerInsideShip)
        {
            HudRenderer.RenderPlanetSurfacePrompt(renderer,
                _inVehicle, _nearShip, _nearVehicle, _vehicleDeployed, _nearSettlement);
        }

        // Unified HUD (top-left: location, player info, health)
        if (!_playerDead)
        {
            HudRenderer.RenderPlanetSurfaceHud(renderer, game.Player, _planet,
                _starSystem.DangerLevel, _inVehicle, game.EcsWorld, _playerAvatar);
        }

        // Combat message
        if (_combatMessage != null)
        {
            SolarSystemRenderer.RenderCenteredMessage(renderer, _combatMessage, -40, new Color3(255, 220, 80), 2f);
        }

        // Death message
        if (_playerDead)
        {
            SolarSystemRenderer.RenderCenteredMessage(renderer, "YOU DIED", -20, new Color3(255, 80, 80), 3f);
            SolarSystemRenderer.RenderCenteredMessage(renderer, "RETURNING TO ORBIT...", 20, new Color3(200, 200, 200), 1.5f);
        }

        // Minimap (top-right, unified style)
        Vector2? vehiclePos = _vehicleDeployed && !_inVehicle
            ? game.EcsWorld.Get<Transform>(_vehicleEntity).Position
            : null;
        HudMinimapRenderer.RenderPlanetSurfaceMinimap(renderer, _surfaceData,
            avatarTf.Position, shipTf.Position, vehiclePos, game.EcsWorld);

        // Off-screen settlement indicators
        if (!_playerDead && !_playerInsideShip)
        {
            HudRenderer.RenderSettlementOffscreenIndicators(renderer, camera,
                _surfaceData.Settlements);
            HudRenderer.RenderShipOffscreenIndicator(renderer, camera, shipTf.Position);
            HudRenderer.RenderPlanetSurfaceMissionOffscreenIndicators(renderer, camera,
                game.Player, _starSystem.Index, _planet.Index, _surfaceData.Settlements);
        }



        // In-game menu overlay drawn on top of everything
        _inGameMenuOverlay.Render(game);

        // Starship menu overlay drawn on top of everything
        _starshipMenuOverlay.Render(game);

        // Landing / takeoff animation overlay
        if (_isLanding || _isTakingOff)
        {
            bool landing = _isLanding;
            float duration = landing ? LandingDuration : TakeoffDuration;
            float timer = landing ? _landingTimer : _takeoffTimer;
            float progress = Math.Clamp(timer / duration, 0f, 1f);
            float remaining = duration - timer;

            HudRenderer.RenderLandingTakeoffOverlay(renderer, landing, progress, remaining);
        }
    }

    /// <summary>Creates a terrain collision delegate for the movement system.</summary>
    private Func<Vector2, bool> MakeTerrainCollisionCheck()
    {
        return CanMoveToTerrain;
    }

    /// <summary>Checks whether a position is on walkable/drivable terrain.</summary>
    private bool CanMoveToTerrain(Vector2 newPos)
    {
        int tileX = (int)(newPos.X / GameConfig.TileSize);
        int tileY = (int)(newPos.Y / GameConfig.TileSize);
        if (tileX < 0 || tileX >= _surfaceData.Width || tileY < 0 || tileY >= _surfaceData.Height)
            return false;
        var terrain = _surfaceData.Tiles[tileX, tileY];
        return terrain is not (TerrainType.Water or TerrainType.Lava);
    }

    /// <summary>Handle avatar death — show death screen, timer to return to orbit.</summary>
    private void HandleAvatarDeath(Game game, Vector2 deathPos)
    {
        _playerDead = true;
        _respawnTimer = RespawnDelay;
        _explosions.Add(new Explosion(deathPos, 25f, new Color3(255, 120, 80), 1.2f));

        // Apply death penalties — lose some credits
        int creditsLost = (int)(game.Player.Credits * 0.1f);
        game.Player.Credits -= creditsLost;

        _combatMessage = creditsLost > 0 ? $"LOST {creditsLost} CREDITS" : null;
        _combatMessageTimer = RespawnDelay;
    }

}
