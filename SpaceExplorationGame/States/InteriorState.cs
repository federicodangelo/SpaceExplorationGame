using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Overlays;
using SpaceExplorationGame.UI.Overlays.Customization;

namespace SpaceExplorationGame.States;

/// <summary>
/// Walkable interior state for space stations and settlements.
/// Tile-based top-down view with NPC interactions, trade, repair, and missions.
/// </summary>
public class InteriorState : GameState
{
    public override GameStateType Type => GameStateType.Interior;

    private InteriorData _interior = null!;
    private Entity _playerAvatar;

    // Where we came from
    private readonly InteriorOrigin _origin;
    private readonly StarSystemData _starSystem;
    private readonly SpaceStationData? _station;
    private readonly PlanetData? _planet;
    private readonly SettlementData? _settlement;

    // Movement
    private const float BaseAvatarSpeed = 200f;
    private const float InteractionRadius = 1.5f; // in tiles

    // ECS Systems
    private PlayerMovementSystem _movementSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;

    // Interaction state
    private InteriorNpc? _nearestNpc;
    private InteriorInteractable? _nearestInteractable;
    private bool _showingDialogue;
    private InteriorNpc? _dialogueNpc;
    private int _dialogueLine;

    // Service overlays (repair, missions)
    private readonly RepairOverlay _repairOverlay = new();
    private readonly MissionOverlay _missionOverlay = new();
    private readonly ShipCustomizationOverlay _shipCustomization = new();
    private readonly AvatarCustomizationOverlay _avatarCustomization = new();
    private readonly VehicleCustomizationOverlay _vehicleCustomization = new();
    private readonly ShipDealerOverlay _shipDealer = new();
    private readonly SellCargoOverlay _sellCargo = new();

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
        // Generate interior based on origin
        var rng = _origin switch
        {
            InteriorOrigin.Station => new SeededRandom(
                game.Seeds.GetStarSystemRandom(_starSystem.Index).DeriveChildSeed(2000 + (_station?.Index ?? 0))),
            InteriorOrigin.Settlement => new SeededRandom(
                game.Seeds.GetPlanetSurfaceRandom(_starSystem.Index, _planet?.Index ?? 0)
                    .DeriveChildSeed(3000 + (_settlement?.TileX ?? 0) * 100 + (_settlement?.TileY ?? 0))),
            _ => new SeededRandom(12345)
        };

        _interior = _origin switch
        {
            InteriorOrigin.Station => InteriorGenerator.GenerateStation(rng, _station?.Name ?? "STATION"),
            InteriorOrigin.Settlement => InteriorGenerator.GenerateSettlement(rng, _settlement?.Name ?? "SETTLEMENT"),
            _ => InteriorGenerator.GenerateStation(rng, "UNKNOWN")
        };

        // Spawn player avatar at spawn point
        float spawnX = _interior.SpawnPoint.X * GameConfig.TileSize;
        float spawnY = _interior.SpawnPoint.Y * GameConfig.TileSize;

        // Calculate avatar speed from equipped parts
        float avatarSpeed = BaseAvatarSpeed + game.Player.GetCombinedAvatarStats().WalkSpeed;

        _playerAvatar = game.EcsWorld.Create(
            new Transform(spawnX, spawnY),
            ECS.Components.Sprite.ColoredRect(12, 12, 100, 255, 100),
            new Velocity(avatarSpeed),
            new PlayerControlled()
        );

        // Initialize ECS systems
        _movementSystem = new PlayerMovementSystem(game.EcsWorld, game.Input, avatarSpeed);
        _movementSystem.CanMoveTo = (newPos) =>
        {
            int tileX = (int)(newPos.X / GameConfig.TileSize);
            int tileY = (int)(newPos.Y / GameConfig.TileSize);
            return tileX >= 0 && tileX < _interior.Width &&
                   tileY >= 0 && tileY < _interior.Height &&
                   InteriorGenerator.IsWalkable(_interior.Tiles[tileX, tileY]);
        };
        _movementSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(game.EcsWorld, game.Camera, game.Input);
        _cameraFollowSystem.Initialize();

        // Camera setup
        game.Camera.Position = new Vector2(spawnX, spawnY);
        game.Camera.Zoom = 1.5f;
    }

    public override void Exit(Game game)
    {
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void UpdateInput(Game game)
    {
        var input = game.Input;

        // Handle overlay interactions first
        if (_repairOverlay.UpdateInput(game))
            return;
        if (_missionOverlay.UpdateInput(game))
            return;

        // Customization/dealer overlays take priority over game input
        if (_shipCustomization.UpdateInput(game))
            return;
        if (_avatarCustomization.UpdateInput(game))
            return;
        if (_vehicleCustomization.UpdateInput(game))
            return;
        if (_shipDealer.UpdateInput(game))
            return;
        if (_sellCargo.UpdateInput(game))
            return;

        // Handle dialogue
        if (_showingDialogue)
        {
            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E) ||
                input.IsKeyPressed(SDL.Scancode.Space))
            {
                _dialogueLine++;
                if (_dialogueNpc == null || _dialogueLine >= _dialogueNpc.DialogueLines.Length)
                {
                    _showingDialogue = false;
                    _dialogueNpc = null;
                    _dialogueLine = 0;
                }
            }
            if (input.IsKeyPressed(SDL.Scancode.Escape))
            {
                _showingDialogue = false;
                _dialogueNpc = null;
                _dialogueLine = 0;
            }
            return;
        }

        // Interact
        if (input.IsKeyPressed(SDL.Scancode.E))
        {
            // Prefer interactable over NPC when both are nearby
            if (_nearestInteractable != null)
            {
                HandleInteraction(game, _nearestInteractable);
            }
            else if (_nearestNpc != null)
            {
                _showingDialogue = true;
                _dialogueNpc = _nearestNpc;
                _dialogueLine = 0;
            }
        }
    }

    public override void Update(Game game, float dt)
    {
        // Handle customization/dealer overlays that need dt
        _shipCustomization.Update(game, dt);
        _avatarCustomization.Update(game, dt);
        _vehicleCustomization.Update(game, dt);
        _shipDealer.Update(game, dt);
        _sellCargo.Update(game, dt);

        // Skip simulation when overlays or dialogue are active
        if (_repairOverlay.IsOpen || _missionOverlay.IsOpen || _showingDialogue
            || _shipCustomization.IsOpen || _avatarCustomization.IsOpen
            || _vehicleCustomization.IsOpen || _shipDealer.IsOpen || _sellCargo.IsOpen)
            return;

        // Player movement (via system with tile collision)
        _movementSystem.Update(in dt);

        // Camera follows player + handles zoom
        _cameraFollowSystem.Update(in dt);

        // Get player position for proximity checks
        ref var avatarTf = ref game.EcsWorld.Get<Transform>(_playerAvatar);

        // Find nearest NPC and interactable
        float playerTileX = avatarTf.Position.X / GameConfig.TileSize;
        float playerTileY = avatarTf.Position.Y / GameConfig.TileSize;

        _nearestNpc = null;
        float nearestNpcDist = float.MaxValue;
        foreach (var npc in _interior.Npcs)
        {
            float dx = npc.TileX - playerTileX;
            float dy = npc.TileY - playerTileY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < InteractionRadius && dist < nearestNpcDist)
            {
                nearestNpcDist = dist;
                _nearestNpc = npc;
            }
        }

        _nearestInteractable = null;
        float nearestIntDist = float.MaxValue;
        foreach (var interactable in _interior.Interactables)
        {
            float dx = interactable.TileX - playerTileX;
            float dy = interactable.TileY - playerTileY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < InteractionRadius && dist < nearestIntDist)
            {
                nearestIntDist = dist;
                _nearestInteractable = interactable;
            }
        }
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
                _missionOverlay.Open();
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
                // Return to planet surface at the settlement location
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = _planet!.Index;
                int landX = _settlement!.TileX + _settlement.Width / 2;
                int landY = _settlement!.TileY + _settlement.Height / 2;
                game.ChangeState(new PlanetSurfaceState(_starSystem, _planet, landX, landY));
                break;
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = game.Camera;
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);

        // Draw world (background, tiles, room labels, NPCs, interactables, player avatar)
        InteriorRenderer.RenderWorld(renderer, camera, _interior, avatarTf.Position, game.AvatarRenderer, game.GlobalTime, _planet);

        // --- HUD ---
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;
        bool anyOverlayOpen = _showingDialogue || _repairOverlay.IsOpen || _missionOverlay.IsOpen
            || _shipCustomization.IsOpen || _avatarCustomization.IsOpen
            || _vehicleCustomization.IsOpen || _shipDealer.IsOpen || _sellCargo.IsOpen;

        InteriorRenderer.RenderHud(renderer, _interior, game.Player.Credits,
            anyOverlayOpen ? null : _nearestInteractable,
            anyOverlayOpen ? null : _nearestNpc,
            anyOverlayOpen, w, h);

        // Dialogue box
        if (_showingDialogue && _dialogueNpc != null)
        {
            InteriorRenderer.RenderDialogue(renderer, w, h, _dialogueNpc, _dialogueLine);
        }

        // Overlays
        _repairOverlay.Render(game);
        _missionOverlay.Render(game);
        _shipCustomization.Render(game);
        _avatarCustomization.Render(game);
        _vehicleCustomization.Render(game);
        _shipDealer.Render(game);
        _sellCargo.Render(game);

        // Minimap
        InteriorRenderer.RenderMinimap(renderer, _interior, avatarTf.Position, w);
    }
}

/// <summary>Where the interior was entered from.</summary>
public enum InteriorOrigin
{
    Station,
    Settlement
}
