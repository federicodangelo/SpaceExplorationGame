using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

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

    // Shared service overlays (repair, missions)
    private readonly ServiceOverlays _overlays = new();
    private readonly ShipCustomizationOverlay _shipCustomization = new();
    private readonly AvatarCustomizationOverlay _avatarCustomization = new();
    private readonly VehicleCustomizationOverlay _vehicleCustomization = new();
    private readonly ShipDealerOverlay _shipDealer = new();

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

    public override void Update(Game game, float dt)
    {
        var input = game.Input;
        var camera = game.Camera;

        // Handle overlay interactions first
        if (_overlays.Update(game, input))
            return;
        if (_shipCustomization.IsOpen)
        {
            _shipCustomization.Update(game, input, dt);
            return;
        }
        if (_avatarCustomization.IsOpen)
        {
            _avatarCustomization.Update(game, input, dt);
            return;
        }
        if (_vehicleCustomization.IsOpen)
        {
            _vehicleCustomization.Update(game, input, dt);
            return;
        }
        if (_shipDealer.IsOpen)
        {
            _shipDealer.Update(game, input, dt);
            return;
        }

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

        // Exit
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            ExitInterior(game);
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
                _overlays.Open(ServiceOverlays.OverlayType.Repair);
                break;
            case InteractableType.MissionBoard:
                _overlays.Open(ServiceOverlays.OverlayType.Mission);
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

        // Draw exterior background visible through Void tiles
        RenderExteriorBackground(game, renderer, camera);

        // Draw tiles via shared renderer
        TileMapRenderer.RenderTiles(renderer, camera, _interior.Width, _interior.Height,
            (x, y) =>
            {
                var tile = _interior.Tiles[x, y];
                if (tile == InteriorTileType.Void) return null;
                return InteriorGenerator.GetTileColor(tile);
            },
            1200f,
            (x, y, worldPos, hash) =>
            {
                var tile = _interior.Tiles[x, y];

                // Wall detail: highlight top edge
                if (tile == InteriorTileType.Wall)
                {
                    var topEdge = new Vector2(x * GameConfig.TileSize + GameConfig.TileSize / 2f,
                                              y * GameConfig.TileSize + 2);
                    renderer.DrawRect(camera, topEdge, GameConfig.TileSize, 2, 55, 55, 65);
                }

                // Console glow
                if (tile == InteriorTileType.Console)
                {
                    float pulse = MathF.Sin((float)game.GlobalTime * 3f + x + y) * 0.3f + 0.7f;
                    byte gr = (byte)(40 * pulse);
                    byte gg = (byte)(120 * pulse);
                    byte gb = (byte)(180 * pulse);
                    renderer.DrawRect(camera, worldPos, GameConfig.TileSize - 8, GameConfig.TileSize - 8, gr, gg, gb);
                }

                // Crate detail: cross pattern
                if (tile == InteriorTileType.Crate)
                {
                    renderer.DrawRect(camera, worldPos, GameConfig.TileSize - 6, 2, 120, 100, 60);
                    renderer.DrawRect(camera, worldPos, 2, GameConfig.TileSize - 6, 120, 100, 60);
                }

                // Window transparency effect
                if (tile == InteriorTileType.Window)
                {
                    float shimmer = MathF.Sin((float)game.GlobalTime * 2f + x * 0.5f) * 0.2f + 0.5f;
                    byte wr = (byte)(80 * shimmer);
                    byte wg = (byte)(120 * shimmer);
                    byte wb = (byte)(180 * shimmer);
                    renderer.DrawRect(camera, worldPos, GameConfig.TileSize - 4, GameConfig.TileSize - 4, wr, wg, wb, 150);
                }

                // Landing pad markings
                if (tile == InteriorTileType.LandingPad)
                {
                    if ((x + y) % 2 == 0)
                    {
                        renderer.DrawRect(camera, worldPos, 4, 4, 80, 80, 40);
                    }
                }
            });

        // Draw room labels
        foreach (var room in _interior.Rooms)
        {
            float roomLabelW = renderer.MeasureText(room.Name, 3f) / 2f / camera.Zoom;
            var labelPos = new Vector2(
                (room.X + room.Width / 2f) * GameConfig.TileSize - roomLabelW,
                room.Y * GameConfig.TileSize - 8
            );
            renderer.DrawText(camera, labelPos, room.Name, 120, 120, 160, 3f);
        }

        // Draw NPCs
        foreach (var npc in _interior.Npcs)
        {
            var npcPos = new Vector2(
                npc.TileX * GameConfig.TileSize + GameConfig.TileSize / 2f,
                npc.TileY * GameConfig.TileSize + GameConfig.TileSize / 2f
            );

            // Body
            renderer.DrawRect(camera, npcPos, 10, 14, npc.R, npc.G, npc.B);

            // Head circle approximation
            var headPos = npcPos - new Vector2(0, 8);
            renderer.DrawRect(camera, headPos, 8, 8, (byte)Math.Min(npc.R + 30, 255),
                (byte)Math.Min(npc.G + 30, 255), (byte)Math.Min(npc.B + 30, 255));

            // Nametag (centered)
            float nameW = renderer.MeasureText(npc.Name, 1.5f) / 2f / camera.Zoom;
            var namePos = npcPos - new Vector2(nameW, 18);
            renderer.DrawText(camera, namePos, npc.Name, 200, 200, 200, 1.5f);

            // Role tag (centered)
            float roleW = renderer.MeasureText(npc.Role, 1.5f) / 2f / camera.Zoom;
            var rolePos = npcPos + new Vector2(-roleW, 12);
            renderer.DrawText(camera, rolePos, npc.Role, npc.R, npc.G, npc.B, 1.5f);
        }

        // Draw interactable markers
        foreach (var interactable in _interior.Interactables)
        {
            var intPos = new Vector2(
                interactable.TileX * GameConfig.TileSize + GameConfig.TileSize / 2f,
                interactable.TileY * GameConfig.TileSize + GameConfig.TileSize / 2f
            );

            // Floating indicator
            float bob = MathF.Sin((float)game.GlobalTime * 2f) * 3f;
            var indicatorPos = intPos - new Vector2(0, 20 + bob);

            var (ir, ig, ib) = interactable.Type switch
            {
                InteractableType.TradeTerminal => ((byte)255, (byte)220, (byte)80),
                InteractableType.RepairStation => ((byte)100, (byte)255, (byte)100),
                InteractableType.MissionBoard => ((byte)100, (byte)180, (byte)255),
                InteractableType.ShipCustomization => ((byte)100, (byte)220, (byte)255),
                InteractableType.ShipDealer => ((byte)255, (byte)200, (byte)80),
                InteractableType.ExitDoor => ((byte)255, (byte)100, (byte)100),
                _ => ((byte)200, (byte)200, (byte)200)
            };

            renderer.DrawRect(camera, indicatorPos, 6, 6, ir, ig, ib);
            float intLabelW = renderer.MeasureText(interactable.Name, 1.5f) / 2f / camera.Zoom;
            renderer.DrawText(camera, indicatorPos - new Vector2(intLabelW, 10), interactable.Name, ir, ig, ib, 1.5f);
        }

        // Draw player avatar
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        var avatarTex = game.Textures.GetTexture(Rendering.TextureManager.AvatarDown);
        renderer.DrawTexture(camera, avatarTex, avatarTf.Position, 28, 28);

        // --- HUD ---
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Location name
        renderer.DrawRectScreen(0, 0, w, 35, 0, 0, 0, 180);
        string locationLabel = _interior.Type == InteriorType.Station
            ? $"STATION: {_interior.Name.ToUpper()}"
            : $"SETTLEMENT: {_interior.Name.ToUpper()}";
        renderer.DrawTextScreen(10, 8, locationLabel, 200, 200, 255, 2f);

        // Credits
        renderer.DrawTextScreen(w - 200, 8, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);

        // Interaction prompts
        if (_nearestInteractable != null && !_showingDialogue && _overlays.Active == ServiceOverlays.OverlayType.None
            && !_shipCustomization.IsOpen && !_avatarCustomization.IsOpen && !_vehicleCustomization.IsOpen
            && !_shipDealer.IsOpen)
        {
            string prompt = _nearestInteractable.Type switch
            {
                InteractableType.ExitDoor => "[E] EXIT",
                InteractableType.RepairStation => "[E] REPAIR",
                InteractableType.MissionBoard => "[E] MISSIONS",
                InteractableType.ShipCustomization => "[E] SHIP CUSTOMIZATION",
                InteractableType.AvatarCustomization => "[E] AVATAR CUSTOMIZATION",
                InteractableType.VehicleCustomization => "[E] VEHICLE CUSTOMIZATION",
                InteractableType.ShipDealer => "[E] SHIP DEALER",
                _ => "[E] INTERACT"
            };
            float tw = renderer.MeasureText(prompt, 2f);
            renderer.DrawRectScreen(w / 2f - tw / 2f - 10, h - 60, tw + 20, 35, 0, 0, 0, 180);
            renderer.DrawTextScreen(w / 2f - tw / 2f, h - 55, prompt, 100, 255, 200, 2f);
        }
        else if (_nearestNpc != null && !_showingDialogue && _overlays.Active == ServiceOverlays.OverlayType.None
            && !_shipCustomization.IsOpen && !_avatarCustomization.IsOpen && !_vehicleCustomization.IsOpen
            && !_shipDealer.IsOpen)
        {
            string prompt = $"[E] TALK TO {_nearestNpc.Name.ToUpper()}";
            float tw = renderer.MeasureText(prompt, 2f);
            renderer.DrawRectScreen(w / 2f - tw / 2f - 10, h - 60, tw + 20, 35, 0, 0, 0, 180);
            renderer.DrawTextScreen(w / 2f - tw / 2f, h - 55, prompt, 200, 200, 255, 2f);
        }

        // Dialogue box
        if (_showingDialogue && _dialogueNpc != null)
        {
            RenderDialogue(renderer, w, h);
        }

        // Overlays
        _overlays.Render(game, renderer);
        _shipCustomization.Render(game, renderer);
        _avatarCustomization.Render(game, renderer);
        _vehicleCustomization.Render(game, renderer);
        _shipDealer.Render(game, renderer);

        // Controls help (when no overlay)
        if (_overlays.Active == ServiceOverlays.OverlayType.None && !_showingDialogue
            && !_shipCustomization.IsOpen && !_avatarCustomization.IsOpen && !_vehicleCustomization.IsOpen
            && !_shipDealer.IsOpen)
        {
            renderer.DrawRectScreen(w - 200, h - 110, 195, 100, 0, 0, 0, 160);
            renderer.DrawTextScreen(w - 190, h - 105, "WASD: MOVE", 160, 160, 160, 1.5f);
            renderer.DrawTextScreen(w - 190, h - 85, "SCROLL: ZOOM", 160, 160, 160, 1.5f);
            renderer.DrawTextScreen(w - 190, h - 65, "E: INTERACT", 160, 160, 160, 1.5f);
            renderer.DrawTextScreen(w - 190, h - 45, "ESC: EXIT", 160, 160, 160, 1.5f);
        }

        // Minimap
        RenderMinimap(renderer, game, w);
    }

    /// <summary>
    /// Draws the exterior background visible through Void tiles.
    /// Stations show space with stars; settlements show planet terrain.
    /// </summary>
    private void RenderExteriorBackground(Game game, SpriteRenderer renderer, Camera camera)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();

        // Extend slightly beyond visible area
        int margin = GameConfig.TileSize * 2;
        float bgLeft = topLeft.X - margin;
        float bgTop = topLeft.Y - margin;
        float bgRight = bottomRight.X + margin;
        float bgBottom = bottomRight.Y + margin;
        float bgW = bgRight - bgLeft;
        float bgH = bgBottom - bgTop;
        var bgCenter = new Vector2(bgLeft + bgW / 2f, bgTop + bgH / 2f);

        if (_interior.Type == InteriorType.Station)
        {
            // Space background: dark blue-black
            renderer.DrawRect(camera, bgCenter, (int)bgW, (int)bgH, 4, 4, 12);

            // Deterministic stars based on visible area
            int starGridSize = 60; // a star roughly every 60px
            int sx0 = (int)MathF.Floor(bgLeft / starGridSize) - 1;
            int sy0 = (int)MathF.Floor(bgTop / starGridSize) - 1;
            int sx1 = (int)MathF.Ceiling(bgRight / starGridSize) + 1;
            int sy1 = (int)MathF.Ceiling(bgBottom / starGridSize) + 1;

            for (int sx = sx0; sx <= sx1; sx++)
            {
                for (int sy = sy0; sy <= sy1; sy++)
                {
                    // Hash to get deterministic position and brightness
                    int h = (sx * 374761393 + sy * 668265263) ^ (sx * 17 + sy * 31);
                    if ((h & 3) != 0) continue; // ~25% density

                    float px = sx * starGridSize + ((h >> 4) & 0x3F) - 32;
                    float py = sy * starGridSize + ((h >> 10) & 0x3F) - 32;

                    // Star brightness and color variation
                    int bright = 80 + ((h >> 16) & 0x7F); // 80-207
                    byte sr = (byte)Math.Min(255, bright + ((h >> 2) & 0x1F));
                    byte sg = (byte)Math.Min(255, bright + ((h >> 5) & 0x0F));
                    byte sb = (byte)Math.Min(255, bright + ((h >> 8) & 0x2F));

                    int starSize = ((h >> 20) & 1) == 0 ? 2 : 1; // most stars are 1px, some 2px
                    renderer.DrawRect(camera, new Vector2(px, py), starSize, starSize, sr, sg, sb);
                }
            }
        }
        else
        {
            // Settlement exterior: planet terrain color
            byte tr = _planet?.R ?? 80;
            byte tg = _planet?.G ?? 120;
            byte tb = _planet?.B ?? 60;

            // Darken slightly (it's ground seen from inside)
            tr = (byte)(tr * 0.4f);
            tg = (byte)(tg * 0.4f);
            tb = (byte)(tb * 0.4f);

            // Base terrain fill
            renderer.DrawRect(camera, bgCenter, (int)bgW, (int)bgH, tr, tg, tb);

            // Terrain detail: scattered dots for ground texture
            int detailGridSize = 40;
            int dx0 = (int)MathF.Floor(bgLeft / detailGridSize) - 1;
            int dy0 = (int)MathF.Floor(bgTop / detailGridSize) - 1;
            int dx1 = (int)MathF.Ceiling(bgRight / detailGridSize) + 1;
            int dy1 = (int)MathF.Ceiling(bgBottom / detailGridSize) + 1;

            for (int dx = dx0; dx <= dx1; dx++)
            {
                for (int dy = dy0; dy <= dy1; dy++)
                {
                    int h = (dx * 374761393 + dy * 668265263) ^ (dx * 13 + dy * 29);
                    if ((h & 7) > 2) continue; // ~37% density

                    float px = dx * detailGridSize + ((h >> 4) & 0x1F) - 16;
                    float py = dy * detailGridSize + ((h >> 10) & 0x1F) - 16;

                    // Slight color variation
                    int var_ = ((h >> 16) & 0x1F) - 16;
                    byte dr = (byte)Math.Clamp(tr + var_, 0, 255);
                    byte dg = (byte)Math.Clamp(tg + var_, 0, 255);
                    byte db = (byte)Math.Clamp(tb + var_, 0, 255);

                    renderer.DrawRect(camera, new Vector2(px, py), 3, 3, dr, dg, db);
                }
            }
        }

        // Draw interior boundary outline to make the structure edges visible
        float interiorPixelW = _interior.Width * GameConfig.TileSize;
        float interiorPixelH = _interior.Height * GameConfig.TileSize;
        var tl = new Vector2(0, 0);
        var tr2 = new Vector2(interiorPixelW, 0);
        var bl = new Vector2(0, interiorPixelH);
        var br = new Vector2(interiorPixelW, interiorPixelH);

        byte lr = _interior.Type == InteriorType.Station ? (byte)40 : (byte)50;
        byte lg = _interior.Type == InteriorType.Station ? (byte)50 : (byte)45;
        byte lb = _interior.Type == InteriorType.Station ? (byte)80 : (byte)35;

        renderer.DrawLine(camera, tl, tr2, lr, lg, lb);
        renderer.DrawLine(camera, tr2, br, lr, lg, lb);
        renderer.DrawLine(camera, br, bl, lr, lg, lb);
        renderer.DrawLine(camera, bl, tl, lr, lg, lb);
    }

    private void RenderDialogue(SpriteRenderer renderer, int w, int h)
    {
        float boxW = 600;
        float boxH = 120;
        float boxX = w / 2f - boxW / 2f;
        float boxY = h - boxH - 20;

        // Background
        renderer.DrawRectScreen(boxX - 2, boxY - 2, boxW + 4, boxH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(boxX, boxY, boxW, boxH, 15, 15, 35, 240);

        // NPC name and role
        renderer.DrawTextScreen(boxX + 15, boxY + 10, _dialogueNpc!.Name.ToUpper(),
            _dialogueNpc.R, _dialogueNpc.G, _dialogueNpc.B, 2f);
        renderer.DrawTextScreen(boxX + 15 + renderer.MeasureText(_dialogueNpc.Name + "  ", 2f), boxY + 10,
            _dialogueNpc.Role, 120, 120, 150, 1.5f);

        // Dialogue line
        if (_dialogueLine < _dialogueNpc.DialogueLines.Length)
        {
            string line = _dialogueNpc.DialogueLines[_dialogueLine];

            // Word wrap at ~50 chars
            int lineY = 0;
            int charsPerLine = 55;
            for (int i = 0; i < line.Length; i += charsPerLine)
            {
                int end = Math.Min(i + charsPerLine, line.Length);
                // Try to break at space
                if (end < line.Length && end > i)
                {
                    int lastSpace = line.LastIndexOf(' ', end - 1, end - i);
                    if (lastSpace > i) end = lastSpace + 1;
                }
                string segment = line[i..end].TrimEnd();
                renderer.DrawTextScreen(boxX + 15, boxY + 40 + lineY * 18, segment, 200, 200, 200, 1.5f);
                lineY++;
            }
        }

        // Continue prompt
        string continueText = _dialogueLine < _dialogueNpc.DialogueLines.Length - 1
            ? "[ENTER] CONTINUE" : "[ENTER] CLOSE";
        renderer.DrawTextScreen(boxX + boxW - 200, boxY + boxH - 25, continueText, 100, 200, 100, 1.5f);
    }

    private void RenderMinimap(SpriteRenderer renderer, Game game, int screenW)
    {
        float mmSize = 120;
        float mmX = screenW - mmSize - 10;
        float mmY = 42;
        renderer.DrawRectScreen(mmX - 1, mmY - 1, mmSize + 2, mmSize + 2, 60, 60, 100);
        renderer.DrawRectScreen(mmX, mmY, mmSize, mmSize, 10, 10, 15, 220);

        float scaleX = mmSize / (_interior.Width * GameConfig.TileSize);
        float scaleY = mmSize / (_interior.Height * GameConfig.TileSize);

        // Draw rooms on minimap
        foreach (var room in _interior.Rooms)
        {
            float rx = mmX + room.X * GameConfig.TileSize * scaleX;
            float ry = mmY + room.Y * GameConfig.TileSize * scaleY;
            float rw = room.Width * GameConfig.TileSize * scaleX;
            float rh = room.Height * GameConfig.TileSize * scaleY;
            renderer.DrawRectScreen(rx, ry, rw, rh, 50, 50, 60);
        }

        // Player dot
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        float px = mmX + avatarTf.Position.X * scaleX;
        float py = mmY + avatarTf.Position.Y * scaleY;
        renderer.DrawRectScreen(px - 2, py - 2, 4, 4, 100, 255, 100);

        // NPC dots
        foreach (var npc in _interior.Npcs)
        {
            float nx = mmX + npc.TileX * GameConfig.TileSize * scaleX;
            float ny = mmY + npc.TileY * GameConfig.TileSize * scaleY;
            renderer.DrawRectScreen(nx - 1, ny - 1, 3, 3, npc.R, npc.G, npc.B);
        }

        // Interactable dots
        foreach (var interactable in _interior.Interactables)
        {
            float ix = mmX + interactable.TileX * GameConfig.TileSize * scaleX;
            float iy = mmY + interactable.TileY * GameConfig.TileSize * scaleY;
            var (ir, ig, ib) = interactable.Type switch
            {
                InteractableType.TradeTerminal => ((byte)255, (byte)220, (byte)80),
                InteractableType.RepairStation => ((byte)100, (byte)255, (byte)100),
                InteractableType.MissionBoard => ((byte)100, (byte)180, (byte)255),
                InteractableType.ShipCustomization => ((byte)100, (byte)220, (byte)255),
                InteractableType.AvatarCustomization => ((byte)100, (byte)255, (byte)180),
                InteractableType.VehicleCustomization => ((byte)255, (byte)180, (byte)80),
                InteractableType.ExitDoor => ((byte)255, (byte)100, (byte)100),
                _ => ((byte)200, (byte)200, (byte)200)
            };
            renderer.DrawRectScreen(ix - 1, iy - 1, 3, 3, ir, ig, ib);
        }
    }
}

/// <summary>Where the interior was entered from.</summary>
public enum InteriorOrigin
{
    Station,
    Settlement
}
