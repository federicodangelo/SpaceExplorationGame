using System.Numerics;
using Arch.Core;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Movement;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.UI.Overlays.Customization;
using SpaceExplorationGame.UI.Overlays.Menu;

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
    private readonly SpaceStationData? _station;
    private readonly PlanetData? _planet;
    private readonly SettlementData? _settlement;

    // ── Input system ────────────────────────────────────────────────
    private AvatarMovementSystem _movementSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;

    // ── Camera ──────────────────────────────────────────────────────
    private readonly Camera _camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
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

    public InteriorState(InteriorOrigin origin, StarSystemData starSystem,
        SpaceStationData? station = null, PlanetData? planet = null, SettlementData? settlement = null)
    {
        _origin = origin;
        _starSystem = starSystem;
        _station = station;
        _planet = planet;
        _settlement = settlement;
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
            () => new InteriorSimulation(game, _origin, _starSystem, _station, _planet, _settlement, parentSim));

        // Add player
        _simPlayer = _sim.AddPlayer(game.Player);

        // Initialize input/camera systems on simulation's ECS world
        float avatarSpeed = game.Player.AvatarWalkSpeed;
        _movementSystem = new AvatarMovementSystem(_sim.EcsWorld, game.Input, avatarSpeed);
        _movementSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(_sim.EcsWorld, _camera);
        _cameraFollowSystem.Initialize();

        // Camera
        float spawnX = _sim.Interior.SpawnPoint.X * GameConfig.TileSize;
        float spawnY = _sim.Interior.SpawnPoint.Y * GameConfig.TileSize;
        _camera.Position = new Vector2(spawnX, spawnY);
        _camera.Zoom = GameConfig.InteriorZoomDefault;
        _camera.ClampZoom();

        // Music
        game.Audio.SetMusicTheme(MusicTheme.Interior);
    }

    public override void Exit(Game game)
    {
        if (_sim != null && _simPlayer != null)
            _sim.RemovePlayer(_simPlayer);
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void UpdateInput(Game game)
    {
        var input = game.Input;

        if (_repairOverlay.UpdateInput(game)) return;
        if (_healthStationOverlay.UpdateInput(game)) return;
        if (_missionOverlay.UpdateInput(game)) return;
        if (_shipCustomization.UpdateInput(game)) return;
        if (_avatarCustomization.UpdateInput(game)) return;
        if (_vehicleCustomization.UpdateInput(game)) return;
        if (_shipDealer.UpdateInput(game)) return;
        if (_sellCargo.UpdateInput(game)) return;

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

        if (_inGameMenuOverlay.UpdateInput(game)) return;
        if (input.IsActionPressed(InputAction.MenuBack))
        {
            _inGameMenuOverlay.Open(game);
            return;
        }

        // Interact
        if (input.IsActionPressed(InputAction.Interact))
        {
            if (_sim.NearestInteractable != null)
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
        _movementSystem.Update(in dt);
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;
        var t = _debugTimer;
        t.Begin();

        t.Time("Overlays", () =>
        {
            _shipCustomization.Update(game);
            _avatarCustomization.Update(game);
            _vehicleCustomization.Update(game);
            _shipDealer.Update(game);
            _sellCargo.Update(game);
            _inGameMenuOverlay.Update(game);
        });

        // Skip post-processing when overlays are active
        if (_inGameMenuOverlay.IsOpen || _repairOverlay.IsOpen || _missionOverlay.IsOpen || _showingDialogue
            || _shipCustomization.IsOpen || _avatarCustomization.IsOpen
            || _vehicleCustomization.IsOpen || _shipDealer.IsOpen || _sellCargo.IsOpen)
            return;

        // Camera
        t.Time("CameraFollow", () => _cameraFollowSystem.Update(in dt));
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
                if (_origin == InteriorOrigin.Station && _station != null)
                    boardSeed = MissionGenerator.GetStationBoardSeed(game.Seeds, _starSystem.Index, _station.Index);
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
        }
    }

    private void ExitInterior(Game game)
    {
        switch (_origin)
        {
            case InteriorOrigin.Station:
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = _station!.Index;
                game.ChangeState(new SolarSystemState(_starSystem, _station));
                break;
            case InteriorOrigin.Settlement:
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = _planet!.Index;
                int landX = _settlement!.TileRect.X + _settlement.TileRect.Width / 2;
                int landY = _settlement!.TileRect.Y + _settlement.TileRect.Height / 2;
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

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = _camera;
        var world = _sim.EcsWorld;
        var avatarTf = world.Get<Transform>(_simPlayer.Entity);

        // Draw world
        InteriorRenderer.RenderWorld(renderer, camera, _sim.Interior, avatarTf.Position,
            game.AvatarRenderer, game.GlobalTime, _planet);

        // HUD
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;
        bool anyOverlayOpen = _inGameMenuOverlay.IsOpen || _showingDialogue || _repairOverlay.IsOpen || _missionOverlay.IsOpen
            || _shipCustomization.IsOpen || _avatarCustomization.IsOpen
            || _vehicleCustomization.IsOpen || _shipDealer.IsOpen || _sellCargo.IsOpen || _healthStationOverlay.IsOpen;

        HudRenderer.RenderInteriorHud(renderer, game.Player, _sim.Interior, _starSystem);

        if (!anyOverlayOpen)
        {
            HudRenderer.RenderInteriorPrompt(renderer, _sim.NearestInteractable, _sim.NearestNpc,
                game.Input.GetActionHelpText(InputAction.Interact));
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

        // Minimap
        HudMinimapRenderer.RenderInteriorMinimap(renderer, _sim.Interior, avatarTf.Position);
    }

    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _debugInfo.Begin();
        _debugInfo.Add($"Origin: {_origin}  NPCs: {_sim.Interior.Npcs.Count}");
        _debugInfo.Add($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0}) Zoom: {_camera.Zoom:F2}");
        _debugInfo.Add($"Dialogue: {_showingDialogue}  NearNpc: {_sim.NearestNpc?.Name ?? "none"}");
        return _debugInfo.Entries;
    }
}

/// <summary>Where the interior was entered from.</summary>
public enum InteriorOrigin
{
    Station,
    Settlement
}
