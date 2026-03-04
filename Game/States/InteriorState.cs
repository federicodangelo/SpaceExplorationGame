using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Input;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.UI.Overlays.Customization;
using SpaceExplorationGame.UI.Overlays.Menu;
using Engine.Platform;

namespace SpaceExplorationGame.States;

/// <summary>
/// Walkable interior state for space stations and settlements.
/// Rendering and input only — simulation logic lives in <see cref="InteriorSimulation"/>.
/// </summary>
public class InteriorState : GameState
{
    public override GameStateType Type => GameStateType.Interior;

    // ── Simulation ──────────────────────────────────────────────────
    private InteriorSimulation _sim = null!;
    private SimulationPlayer _simPlayer = null!;

    // ── Origin data ─────────────────────────────────────────────────
    private readonly InteriorOrigin _origin;
    private readonly StarSystemData _starSystem;
    private readonly SpaceStationData? _spaceStation;
    private readonly PlanetData? _planet;
    private readonly SettlementData? _settlement;

    // ── Ship boarding ───────────────────────────────────────────────
    /// <summary>Whether the player is currently inside their ship (not yet disembarked).</summary>
    private bool _playerInsideShip;
    private readonly bool _startInShip;
    /// <summary>Countdown (sec) before the docking menu auto-opens on fresh arrival.</summary>
    private float _dockingMenuOpenDelay;
    private const float DockingMenuDelay = 0.6f;

    // ── Input system ────────────────────────────────────────────────
    private PlayerAvatarInputSystem _inputSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;

    // ── Camera ──────────────────────────────────────────────────────
    private readonly Camera _camera = new(GameConfig.DefaultWindowWidth, GameConfig.DefaultWindowHeight,
        GameConfig.InteriorZoomMin, GameConfig.InteriorZoomMax);

    // ── Dialogue state ──────────────────────────────────────────────
    private bool _showingDialogue;
    private InteriorNpc? _dialogueNpc;
    private int _dialogueLine;

    // ── Overlays ────────────────────────────────────────────────────
    private readonly InGameMenuOverlay _inGameMenuOverlay = new() { StateType = GameStateType.Interior };
    private readonly RepairOverlay _repairOverlay = new();
    private readonly MissionOverlay _missionOverlay = new();
    private readonly ShipCustomizationOverlay _shipCustomization = new();
    private readonly AvatarCustomizationOverlay _avatarCustomization = new();
    private readonly VehicleCustomizationOverlay _vehicleCustomization = new();
    private readonly ShipDealerOverlay _shipDealer = new();
    private readonly SellCargoOverlay _sellCargo = new();
    private readonly HealthStationOverlay _healthStationOverlay = new();
    private readonly StarshipMenuOverlay _starshipMenuOverlay = new();

    private bool AnyOverlayOpen => _inGameMenuOverlay.IsOpen || _showingDialogue || _repairOverlay.IsOpen || _missionOverlay.IsOpen
            || _shipCustomization.IsOpen || _avatarCustomization.IsOpen
            || _vehicleCustomization.IsOpen || _shipDealer.IsOpen || _sellCargo.IsOpen || _healthStationOverlay.IsOpen
            || _starshipMenuOverlay.IsOpen;

    public InteriorState(InteriorOrigin origin, StarSystemData starSystem,
        SpaceStationData? spaceStation = null, PlanetData? planet = null, SettlementData? settlement = null,
        bool startInShip = false)
    {
        _origin = origin;
        _starSystem = starSystem;
        _spaceStation = spaceStation;
        _planet = planet;
        _settlement = settlement;
        _startInShip = startInShip;
    }

    public override void Enter(Game game)
    {
        // Get or create the simulation
        ISimulation? parentSim = _origin == InteriorOrigin.Settlement && _planet != null
            ? game.Coordinator.Find<PlanetSurfaceSimulation>(
                s => s.StarSystem.Index == _starSystem.Index && s.Planet.Index == _planet.Index)
            : game.Coordinator.Find<SolarSystemSimulation>(s => s.StarSystem.Index == _starSystem.Index);
        _sim = game.Coordinator.FindOrCreate<InteriorSimulation>(
            s => s.Origin == _origin && s.StarSystem.Index == _starSystem.Index,
            () => new InteriorSimulation(game, _origin, _starSystem, _spaceStation, _planet, _settlement, parentSim));

        // Add player
        int playerTileX = _sim.Interior.SpawnPoint.X;
        int playerTileY = _sim.Interior.SpawnPoint.Y;
        _simPlayer = _sim.AddPlayer(game.Player, new AddContext(playerTileX, playerTileY));

        // Initialize input/camera systems on simulation's ECS world
        float avatarSpeed = game.Player.AvatarWalkSpeed;
        _inputSystem = new PlayerAvatarInputSystem(_sim.EcsWorld, game.Input, avatarSpeed);
        _inputSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(_sim.EcsWorld, _camera);
        _cameraFollowSystem.Initialize();

        // Camera
        _camera.Position = new Vector2(playerTileX * GameConfig.TileSize, playerTileY * GameConfig.TileSize);
        _camera.Zoom = GameConfig.InteriorZoomDefault;
        _camera.ClampZoom();

        // Ship boarding — arriving by ship triggers the docking menu after a brief delay
        _playerInsideShip = _startInShip && _sim.Interior.LandingPadTilePos.HasValue;
        if (_playerInsideShip)
            _dockingMenuOpenDelay = DockingMenuDelay;

        // Music
        game.Audio.SetMusicTheme(AudioThemes.Interior);
    }

    public override void Exit(Game game)
    {
        if (_sim != null && _simPlayer != null)
            _sim.RemovePlayer(_simPlayer);
    }

    public override void UpdateInput(Game game)
    {
        var input = game.Input;

        if (AnyOverlayOpen)
            ZeroPlayerMovementAcceleration();

        if (_repairOverlay.UpdateInput(game)) return;
        if (_healthStationOverlay.UpdateInput(game)) return;
        if (_missionOverlay.UpdateInput(game)) return;
        if (_shipCustomization.UpdateInput(game)) return;
        if (_avatarCustomization.UpdateInput(game)) return;
        if (_vehicleCustomization.UpdateInput(game)) return;
        if (_shipDealer.UpdateInput(game)) return;
        if (_sellCargo.UpdateInput(game)) return;

        // Starship menu (while player is inside their ship on the landing pad)
        if (_starshipMenuOverlay.UpdateInput(game))
        {
            if (_starshipMenuOverlay.LastChoice.HasValue)
                HandleDockingMenuChoice(game, _starshipMenuOverlay.LastChoice.Value);
            return;
        }

        // Dialogue
        if (_showingDialogue)
        {
            if (input.IsActionPressed(InputAction.MenuConfirm) || input.IsActionPressed(InputAction.Interact))
            {
                _dialogueLine++;
                if (_dialogueNpc == null || _dialogueLine >= _dialogueNpc.DialogueLines.Length)
                {
                    _showingDialogue = false;
                    _dialogueNpc = null;
                    _dialogueLine = 0;
                }
            }
            if (input.IsActionPressed(InputAction.MenuBack))
            {
                _showingDialogue = false;
                _dialogueNpc = null;
                _dialogueLine = 0;
            }
            return;
        }

        if (_inGameMenuOverlay.UpdateInput(game)) { ZeroPlayerMovementAcceleration(); return; }
        if (input.IsActionPressed(InputAction.MenuBack))
        {
            _inGameMenuOverlay.Open(game);
            return;
        }

        // Block normal interactions while inside ship
        if (_playerInsideShip)
        {
            // E while inside ship re-opens the docking menu
            if (input.IsActionPressed(InputAction.Interact))
                OpenDockingMenu();
            return;
        }

        // Interact
        if (input.IsActionPressed(InputAction.Interact))
        {
            if (_sim.NearShip)
                BoardShip();
            else if (_sim.NearestInteractable != null)
                HandleInteraction(game, _sim.NearestInteractable);
            else if (_sim.NearestNpc != null)
            {
                _showingDialogue = true;
                _dialogueNpc = _sim.NearestNpc;
                _dialogueLine = 0;
            }
        }

        // Camera zoom
        if (input.MouseWheelY != 0)
        {
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
        }

        // Movement input (write to entity each frame)
        float dt = game.DeltaTime;
        _inputSystem.Update(in dt);
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;
        var t = _debugTimer;
        t.Begin();

        // Auto-open docking menu after landing delay
        if (_dockingMenuOpenDelay > 0)
        {
            _dockingMenuOpenDelay -= dt;
            if (_dockingMenuOpenDelay <= 0)
                OpenDockingMenu();
        }

        t.Time("Overlays", () =>
        {
            _shipCustomization.Update(game);
            _avatarCustomization.Update(game);
            _vehicleCustomization.Update(game);
            _shipDealer.Update(game);
            _sellCargo.Update(game);
            _inGameMenuOverlay.Update(game);
        });

        // Camera
        t.Time("CameraFollow", () => _cameraFollowSystem.Update(in dt));
    }

    private void ZeroPlayerMovementAcceleration()
    {
        if (_sim == null || _simPlayer == null || !_sim.EcsWorld.IsAlive(_simPlayer.Entity)) return;
        ref var vel = ref _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity);
        vel.Acceleration = Vector2.Zero;
        vel.Linear = Vector2.Zero;
    }

    private void OpenDockingMenu()
    {
        // Vehicles can't be deployed inside space stations
        _starshipMenuOverlay.VehicleCanBeDeployed = false;
        _starshipMenuOverlay.Open();
    }

    private void HandleDockingMenuChoice(Game game, StarshipMenuOption choice)
    {
        switch (choice)
        {
            case StarshipMenuOption.TakeOff:
                ExitInterior(game);
                break;

            case StarshipMenuOption.DisembarkOnFoot:
                _playerInsideShip = false;
                // Position avatar next to the ship on the landing pad
                if (_sim.Interior.LandingPadTilePos.HasValue)
                {
                    ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);
                    float shipX = _sim.Interior.LandingPadTilePos.Value.X * GameConfig.TileSize;
                    float shipY = _sim.Interior.LandingPadTilePos.Value.Y * GameConfig.TileSize;
                    avatarTf.Position = new Vector2(shipX, shipY);
                }
                break;
        }
    }

    private void BoardShip()
    {
        _playerInsideShip = true;
        OpenDockingMenu();
    }

    private void HandleInteraction(Game game, InteriorInteractable interactable)
    {
        switch (interactable.Type)
        {
            case InteractableType.ExitDoor:
                ExitInterior(game);
                break;
            case InteractableType.RepairStation:
                _repairOverlay.Open();
                break;
            case InteractableType.MissionBoard:
            {
                ulong boardSeed;
                if (_origin == InteriorOrigin.SpaceStation && _spaceStation != null)
                    boardSeed = MissionGenerator.GetSpaceStationBoardSeed(game.Seeds, _starSystem.Index, _spaceStation.Index);
                else if (_planet != null && _settlement != null)
                    boardSeed = MissionGenerator.GetSettlementBoardSeed(game.Seeds, _starSystem.Index, _planet.Index, _settlement.TileRect.X, _settlement.TileRect.Y);
                else
                    boardSeed = (ulong)_starSystem.Index * 9999;
                _missionOverlay.Open(game, _starSystem, boardSeed);
            }
            break;
            case InteractableType.ShipCustomization:
                _shipCustomization.Open(game.Player);
                break;
            case InteractableType.AvatarCustomization:
                _avatarCustomization.Open();
                break;
            case InteractableType.VehicleCustomization:
                _vehicleCustomization.Open();
                break;
            case InteractableType.ShipDealer:
                _shipDealer.Open();
                break;
            case InteractableType.CargoTerminal:
                _sellCargo.Open();
                break;
            case InteractableType.HealthStation:
                _healthStationOverlay.Open();
                break;
            case InteractableType.NoticeBoard:
                ShowNoticeBoardText();
                break;
        }
    }

    private static readonly string[] NoticeBoardMessages =
    [
        "WANTED: Experienced pilot for cargo runs. Inquire at docking bay.",
        "SECTOR ADVISORY: Pirate activity reported near outer rim. Travel with caution.",
        "FOR SALE: Slightly used shield generator. Only failed twice.",
        "MISSING: Grey cat. Answers to 'Commander'. Reward offered.",
        "LOCAL NEWS: Mining yields up 12% this cycle. Prospectors rejoice.",
        "HELP WANTED: Medic needed. No questions asked. Ask at cantina.",
        "ATTENTION: Gravity generators will be offline for maintenance 0300-0500.",
        "FOUND: Unidentified alloy sample. Claim at trading post.",
        "COMMUNITY NOTICE: Annual star-gazing event this cycle. All welcome.",
        "WARNING: Do not feed the station wildlife. Seriously.",
        "CREW BULLETIN: Karaoke night canceled due to hull breach. Again.",
        "TRADE ALERT: Fuel prices expected to rise. Stock up now."
    ];

    private void ShowNoticeBoardText()
    {
        // Generate deterministic notice based on location
        int seed = _starSystem.Index * 997 +
            (_spaceStation?.Index ?? 0) * 37 +
            (_settlement?.TileRect.X ?? 0) * 7;
        int idx1 = ((seed & 0xFFFF) % NoticeBoardMessages.Length);
        int idx2 = (((seed >> 8) + 3) % NoticeBoardMessages.Length);
        if (idx2 == idx1) idx2 = (idx2 + 1) % NoticeBoardMessages.Length;
        int idx3 = (((seed >> 16) + 7) % NoticeBoardMessages.Length);
        if (idx3 == idx1 || idx3 == idx2)
            idx3 = (idx3 + 2) % NoticeBoardMessages.Length;

        // Reuse dialogue system with a fake "Notice Board" NPC
        _dialogueNpc = new InteriorNpc
        {
            Name = "NOTICE BOARD",
            Role = "",
            DialogueLines =
            [
                NoticeBoardMessages[idx1],
                NoticeBoardMessages[idx2],
                NoticeBoardMessages[idx3]
            ],
            Color = new Color3(220, 200, 140)
        };
        _dialogueLine = 0;
        _showingDialogue = true;
    }

    private void ExitInterior(Game game)
    {
        switch (_origin)
        {
            case InteriorOrigin.SpaceStation:
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
                game.Player.ReturnSpaceStationIndex = _spaceStation!.Index;

                // Look up the station world position from the still-alive solar simulation
                // so the undocking cinematic can render the exterior at the right location.
                Vector2 stationWorldPos = Vector2.Zero;
                var solarSim = game.Coordinator.Find<SolarSystemSimulation>(
                    s => s.StarSystem.Index == _starSystem.Index);
                if (solarSim != null)
                {
                    int stIdx = solarSim.SpaceStations.FindIndex(s => s.Index == _spaceStation.Index);
                    if (stIdx >= 0 && stIdx < solarSim.SpaceStationEntities.Count
                        && solarSim.EcsWorld.IsAlive(solarSim.SpaceStationEntities[stIdx]))
                    {
                        stationWorldPos = solarSim.EcsWorld.Get<Transform>(
                            solarSim.SpaceStationEntities[stIdx]).Position;
                    }
                }

                game.ChangeState(new StationDockingTransitionState(
                    _starSystem, _spaceStation!, _sim.Interior, stationWorldPos));
                break;
            case InteriorOrigin.Settlement:
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = _planet!.Index;
                int landX = _settlement!.TileRect.CenterX;
                int landY = _settlement!.TileRect.Y + _settlement.TileRect.Height; // one tile below settlement
                if (!game.Player.HasSavedSurfacePositions)
                {
                    float px = landX * GameConfig.TileSize;
                    float py = landY * GameConfig.TileSize;
                    game.Player.SaveSurfacePositions(
                        px + 30, py, 0, 0, false, px, py, false);
                }
                game.ChangeState(new PlanetSurfaceState(_starSystem, _planet, landX, landY));
                break;
        }
    }

    public override void RenderGame(Game game)
    {
        _camera.Update(game.SpriteRenderer.WindowWidth, game.SpriteRenderer.WindowHeight);
        var renderer = game.SpriteRenderer;
        var camera = _camera;
        var world = _sim.EcsWorld;
        var avatarTf = world.Get<Transform>(_simPlayer.Entity);

        // Draw world
        InteriorRenderer.RenderWorld(renderer, camera, _sim.Interior, game.GlobalTime, _planet);

        // Draw landed ship on the docking bay landing pad (always visible)
        if (_sim.Interior.LandingPadTilePos.HasValue)
        {
            float shipX = _sim.Interior.LandingPadTilePos.Value.X * GameConfig.TileSize;
            float shipY = _sim.Interior.LandingPadTilePos.Value.Y * GameConfig.TileSize;
            game.SpaceshipRenderer.RenderShadow(renderer, camera, new Vector2(shipX, shipY), game.Player.CurrentShipType.SpriteSize);
            game.SpaceshipRenderer.RenderWithLabel(renderer, camera, new Vector2(shipX, shipY), 0f,
                game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);
        }

        // Draw player avatar only when not inside the ship
        if (!_playerInsideShip)
            InteriorRenderer.RenderPlayerAvatar(renderer, camera, avatarTf.Position, game.AvatarRenderer);

        int w = renderer.WindowWidth;
        int h = renderer.WindowHeight;

        // Atmospheric post-processing (vignette)
        InteriorRenderer.RenderAtmosphere(renderer, w, h);

        // Weather overlay for settlement biomes
        if (_sim.Interior.Type == InteriorType.Settlement)
            WeatherRenderer.Render(renderer, w, h, _planet, game.GlobalTime,
                _camera.Position.X, _camera.Position.Y);
    }

    public override void RenderHud(Game game)
    {
        var renderer = game.SpriteRenderer;
        var world = _sim.EcsWorld;
        var avatarTf = world.Get<Transform>(_simPlayer.Entity);

        int w = renderer.WindowWidth;
        int h = renderer.WindowHeight;

        // HUD
        HudRenderer.RenderInteriorHud(renderer, game.Player, _sim.Interior, _starSystem);

        if (!AnyOverlayOpen)
        {
            if (_playerInsideShip)
            {
                // Inside the ship: prompt to re-open the ship menu if it's been closed
                HudRenderer.RenderCenteredMessage(renderer,
                    $"[{game.Input.GetActionHelpText(InputAction.Interact)}] SHIP MENU",
                    -20, new Color3(100, 255, 100), 2f);
            }
            else
            {
                HudRenderer.RenderInteriorPrompt(renderer, _sim.NearestInteractable, _sim.NearestNpc,
                    game.Input.GetActionHelpText(InputAction.Interact),
                    nearShip: _sim.NearShip);
            }
        }

        // Dialogue
        if (_showingDialogue && _dialogueNpc != null)
            InteriorRenderer.RenderDialogue(renderer, w, h, _dialogueNpc, _dialogueLine);

        // Overlays
        _repairOverlay.Render(game);
        _healthStationOverlay.Render(game);
        _missionOverlay.Render(game);
        _shipCustomization.Render(game);
        _avatarCustomization.Render(game);
        _vehicleCustomization.Render(game);
        _shipDealer.Render(game);
        _sellCargo.Render(game);
        _inGameMenuOverlay.Render(game);
        _starshipMenuOverlay.Render(game);

        // Minimap
        HudMinimapRenderer.RenderInteriorMinimap(renderer, _sim.Interior, avatarTf.Position);
    }

    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _debugInfo.Begin();
        _debugInfo.Add($"Origin: {_origin}  NPCs: {_sim.Interior.Npcs.Count}");
        _debugInfo.Add($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0}) Zoom: {_camera.Zoom:F2}");
        _debugInfo.Add($"Dialogue: {_showingDialogue}  NearNpc: {_sim.NearestNpc?.Name ?? "none"}");
        _debugInfo.Add($"InShip: {_playerInsideShip}  NearShip: {_sim.NearShip}");
        return _debugInfo.Entries;
    }
}

/// <summary>Where the interior was entered from.</summary>
public enum InteriorOrigin
{
    SpaceStation,
    Settlement
}
