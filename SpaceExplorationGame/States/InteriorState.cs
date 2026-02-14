using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
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
    private const float AvatarSpeed = 200f;
    private const float InteractionRadius = 1.5f; // in tiles

    // Interaction state
    private InteriorNpc? _nearestNpc;
    private InteriorInteractable? _nearestInteractable;
    private bool _showingDialogue;
    private InteriorNpc? _dialogueNpc;
    private int _dialogueLine;

    // Overlay state
    private OverlayType _activeOverlay = OverlayType.None;

    // Trade
    private int _tradeSelection;
    private readonly TradeItem[] _tradeItems =
    [
        new("HULL PLATING", 50, "Repairs hull by 25 points"),
        new("FUEL CELLS", 30, "Restores 30 fuel"),
        new("SHIELD EMITTER", 120, "Increases max hull by 20"),
        new("NAV CHARTS", 80, "Reveals nearby systems"),
        new("RATION PACK", 15, "Standard crew supplies"),
    ];

    // Repair
    private const int RepairCostPerPoint = 2;

    // Mission
    private int _missionSelection;

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

        _playerAvatar = game.EcsWorld.Create(
            new Transform(spawnX, spawnY),
            ECS.Components.Sprite.ColoredRect(12, 12, 100, 255, 100),
            new Velocity(AvatarSpeed),
            new PlayerControlled()
        );

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
        if (_activeOverlay != OverlayType.None)
        {
            UpdateOverlay(game, input);
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

        // Player movement
        ref var avatarTf = ref game.EcsWorld.Get<Transform>(_playerAvatar);
        Vector2 moveDir = Vector2.Zero;

        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
            moveDir.Y -= 1;
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
            moveDir.Y += 1;
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            moveDir.X -= 1;
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            moveDir.X += 1;

        if (moveDir != Vector2.Zero)
        {
            moveDir = Vector2.Normalize(moveDir);
            var newPos = avatarTf.Position + moveDir * AvatarSpeed * dt;

            // Collision check
            int tileX = (int)(newPos.X / GameConfig.TileSize);
            int tileY = (int)(newPos.Y / GameConfig.TileSize);

            if (tileX >= 0 && tileX < _interior.Width &&
                tileY >= 0 && tileY < _interior.Height &&
                InteriorGenerator.IsWalkable(_interior.Tiles[tileX, tileY]))
            {
                avatarTf.Position = newPos;
            }
        }

        // Camera follows avatar
        camera.LerpTo(avatarTf.Position, 5f * dt);

        // Zoom
        if (input.MouseWheelY != 0)
        {
            camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            camera.ClampZoom();
        }

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
            case InteractableType.TradeTerminal:
                _activeOverlay = OverlayType.Trade;
                _tradeSelection = 0;
                break;
            case InteractableType.RepairStation:
                _activeOverlay = OverlayType.Repair;
                break;
            case InteractableType.MissionBoard:
                _activeOverlay = OverlayType.Mission;
                _missionSelection = 0;
                break;
        }
    }

    private void UpdateOverlay(Game game, InputManager input)
    {
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            _activeOverlay = OverlayType.None;
            return;
        }

        switch (_activeOverlay)
        {
            case OverlayType.Trade:
                UpdateTradeOverlay(game, input);
                break;
            case OverlayType.Repair:
                UpdateRepairOverlay(game, input);
                break;
            case OverlayType.Mission:
                UpdateMissionOverlay(game, input);
                break;
        }
    }

    private void UpdateTradeOverlay(Game game, InputManager input)
    {
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
        {
            _tradeSelection--;
            if (_tradeSelection < 0) _tradeSelection = _tradeItems.Length - 1;
        }
        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
        {
            _tradeSelection++;
            if (_tradeSelection >= _tradeItems.Length) _tradeSelection = 0;
        }

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            var item = _tradeItems[_tradeSelection];
            if (game.Player.Credits >= item.Cost)
            {
                game.Player.Credits -= item.Cost;

                switch (_tradeSelection)
                {
                    case 0: // Hull plating
                        game.Player.ShipHealth = Math.Min(game.Player.ShipHealth + 25, game.Player.ShipMaxHealth);
                        break;
                    case 1: // Fuel cells
                        game.Player.Refuel(30);
                        break;
                    case 2: // Shield emitter
                        game.Player.ShipMaxHealth += 20;
                        break;
                    case 3: // Nav charts
                        game.Player.Credits += 0; // placeholder
                        break;
                    case 4: // Rations
                        game.Player.Credits += 0; // placeholder
                        break;
                }
            }
        }
    }

    private void UpdateRepairOverlay(Game game, InputManager input)
    {
        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            // Repair all damage
            float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
            int cost = (int)(damage * RepairCostPerPoint);
            if (cost > 0 && game.Player.Credits >= cost)
            {
                game.Player.Credits -= cost;
                game.Player.ShipHealth = game.Player.ShipMaxHealth;
            }
        }
    }

    private void UpdateMissionOverlay(Game game, InputManager input)
    {
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
        {
            _missionSelection--;
            if (_missionSelection < 0) _missionSelection = 2;
        }
        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
        {
            _missionSelection++;
            if (_missionSelection > 2) _missionSelection = 0;
        }
        // Missions are placeholders - just show them, no acceptance yet
    }

    private void ExitInterior(Game game)
    {
        switch (_origin)
        {
            case InteriorOrigin.Station:
                game.ChangeState(new SpaceStationState(_starSystem, _station!));
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

        // Draw tiles
        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startTileX = Math.Max(0, (int)(topLeft.X / GameConfig.TileSize) - 1);
        int startTileY = Math.Max(0, (int)(topLeft.Y / GameConfig.TileSize) - 1);
        int endTileX = Math.Min(_interior.Width - 1, (int)(bottomRight.X / GameConfig.TileSize) + 1);
        int endTileY = Math.Min(_interior.Height - 1, (int)(bottomRight.Y / GameConfig.TileSize) + 1);

        for (int x = startTileX; x <= endTileX; x++)
        {
            for (int y = startTileY; y <= endTileY; y++)
            {
                var tile = _interior.Tiles[x, y];
                if (tile == InteriorTileType.Void) continue;

                var (r, g, b) = InteriorGenerator.GetTileColor(tile);

                // Subtle per-tile variation
                int hash = (x * 374761393 + y * 668265263) ^ (x * y);
                float variation = ((hash & 0xFF) - 128) / 1200f;
                byte vr = (byte)Math.Clamp(r + r * variation, 0, 255);
                byte vg = (byte)Math.Clamp(g + g * variation, 0, 255);
                byte vb = (byte)Math.Clamp(b + b * variation, 0, 255);

                var worldPos = new Vector2(x * GameConfig.TileSize + GameConfig.TileSize / 2f,
                                           y * GameConfig.TileSize + GameConfig.TileSize / 2f);
                renderer.DrawRect(camera, worldPos, GameConfig.TileSize, GameConfig.TileSize, vr, vg, vb);

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
                    // Corner markers
                    if ((x + y) % 2 == 0)
                    {
                        renderer.DrawRect(camera, worldPos, 4, 4, 80, 80, 40);
                    }
                }
            }
        }

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
        if (_nearestInteractable != null && !_showingDialogue && _activeOverlay == OverlayType.None)
        {
            string prompt = _nearestInteractable.Type switch
            {
                InteractableType.ExitDoor => "[E] EXIT",
                InteractableType.TradeTerminal => "[E] TRADE",
                InteractableType.RepairStation => "[E] REPAIR",
                InteractableType.MissionBoard => "[E] MISSIONS",
                _ => "[E] INTERACT"
            };
            float tw = renderer.MeasureText(prompt, 2f);
            renderer.DrawRectScreen(w / 2f - tw / 2f - 10, h - 60, tw + 20, 35, 0, 0, 0, 180);
            renderer.DrawTextScreen(w / 2f - tw / 2f, h - 55, prompt, 100, 255, 200, 2f);
        }
        else if (_nearestNpc != null && !_showingDialogue && _activeOverlay == OverlayType.None)
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
        if (_activeOverlay != OverlayType.None)
        {
            RenderOverlay(game, renderer, w, h);
        }

        // Controls help (when no overlay)
        if (_activeOverlay == OverlayType.None && !_showingDialogue)
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

    private void RenderOverlay(Game game, SpriteRenderer renderer, int w, int h)
    {
        // Semi-transparent background
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 150);

        float panelW = 500;
        float panelH = 400;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        // Panel border
        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        switch (_activeOverlay)
        {
            case OverlayType.Trade:
                RenderTradeOverlay(game, renderer, panelX, panelY, panelW, panelH);
                break;
            case OverlayType.Repair:
                RenderRepairOverlay(game, renderer, panelX, panelY, panelW, panelH);
                break;
            case OverlayType.Mission:
                RenderMissionOverlay(renderer, panelX, panelY, panelW, panelH);
                break;
        }

        // Close hint
        renderer.DrawTextScreen(panelX + 10, panelY + panelH - 25, "ESC: CLOSE", 100, 100, 130, 1.5f);
    }

    private void RenderTradeOverlay(Game game, SpriteRenderer renderer, float px, float py, float pw, float ph)
    {
        renderer.DrawTextScreen(px + 15, py + 10, "TRADE TERMINAL", 255, 220, 80, 2.5f);
        renderer.DrawTextScreen(px + pw - 200, py + 10, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);

        renderer.DrawLineScreen(px + 15, py + 45, px + pw - 15, py + 45, 60, 60, 100);

        for (int i = 0; i < _tradeItems.Length; i++)
        {
            float optY = py + 60 + i * 55;
            bool selected = i == _tradeSelection;
            var item = _tradeItems[i];
            bool canAfford = game.Player.Credits >= item.Cost;

            if (selected)
            {
                renderer.DrawRectScreen(px + 5, optY - 5, pw - 10, 50, 40, 40, 70);
            }

            byte nameR = selected ? (byte)255 : (byte)180;
            byte nameG = selected ? (byte)255 : (byte)180;
            byte nameB = selected ? (byte)200 : (byte)200;

            renderer.DrawTextScreen(px + 20, optY, selected ? $"> {item.Name}" : $"  {item.Name}", nameR, nameG, nameB, 2f);
            renderer.DrawTextScreen(px + 20, optY + 22, item.Description, 130, 130, 150, 1.5f);

            byte costR = canAfford ? (byte)100 : (byte)255;
            byte costG = canAfford ? (byte)255 : (byte)80;
            byte costB = canAfford ? (byte)100 : (byte)80;
            renderer.DrawTextScreen(px + pw - 120, optY + 5, $"{item.Cost} CR", costR, costG, costB, 2f);
        }

        renderer.DrawTextScreen(px + pw - 220, py + ph - 25, "ENTER: BUY", 100, 255, 100, 1.5f);
    }

    private void RenderRepairOverlay(Game game, SpriteRenderer renderer, float px, float py, float pw, float ph)
    {
        renderer.DrawTextScreen(px + 15, py + 10, "REPAIR STATION", 100, 255, 100, 2.5f);

        renderer.DrawLineScreen(px + 15, py + 45, px + pw - 15, py + 45, 60, 60, 100);

        float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
        int cost = (int)(damage * RepairCostPerPoint);

        renderer.DrawTextScreen(px + 20, py + 60, $"SHIP HULL: {game.Player.ShipHealth:F0} / {game.Player.ShipMaxHealth:F0}", 200, 200, 200, 2f);

        // Health bar
        float barX = px + 20;
        float barY = py + 90;
        float barW = pw - 40;
        renderer.DrawRectScreen(barX, barY, barW, 20, 40, 40, 40);
        renderer.DrawRectScreen(barX, barY, barW * (game.Player.ShipHealth / game.Player.ShipMaxHealth), 20, 100, 255, 100);

        if (damage > 0)
        {
            renderer.DrawTextScreen(px + 20, py + 130, $"DAMAGE: {damage:F0} POINTS", 255, 150, 100, 2f);
            renderer.DrawTextScreen(px + 20, py + 160, $"REPAIR COST: {cost} CREDITS", 255, 220, 80, 2f);

            bool canAfford = game.Player.Credits >= cost;
            if (canAfford)
            {
                renderer.DrawTextScreen(px + 20, py + 200, "[ENTER] REPAIR ALL", 100, 255, 100, 2f);
            }
            else
            {
                renderer.DrawTextScreen(px + 20, py + 200, "INSUFFICIENT CREDITS", 255, 80, 80, 2f);
            }
        }
        else
        {
            renderer.DrawTextScreen(px + 20, py + 130, "HULL INTEGRITY: 100%", 100, 255, 100, 2.5f);
            renderer.DrawTextScreen(px + 20, py + 165, "NO REPAIRS NEEDED", 150, 200, 150, 2f);
        }

        renderer.DrawTextScreen(px + 20, py + 250, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);
    }

    private void RenderMissionOverlay(SpriteRenderer renderer, float px, float py, float pw, float ph)
    {
        renderer.DrawTextScreen(px + 15, py + 10, "MISSION BOARD", 100, 180, 255, 2.5f);

        renderer.DrawLineScreen(px + 15, py + 45, px + pw - 15, py + 45, 60, 60, 100);

        string[] missions =
        [
            "CARGO DELIVERY - 200 CR",
            "SURVEY MISSION - 350 CR",
            "ESCORT DUTY - 500 CR"
        ];
        string[] descriptions =
        [
            "Transport supplies to a nearby settlement.",
            "Map an uncharted planetary surface.",
            "Protect a freighter convoy through the sector."
        ];

        for (int i = 0; i < missions.Length; i++)
        {
            float optY = py + 60 + i * 70;
            bool selected = i == _missionSelection;

            if (selected)
                renderer.DrawRectScreen(px + 5, optY - 5, pw - 10, 60, 40, 40, 70);

            renderer.DrawTextScreen(px + 20, optY,
                selected ? $"> {missions[i]}" : $"  {missions[i]}",
                selected ? (byte)255 : (byte)180,
                selected ? (byte)255 : (byte)180,
                selected ? (byte)200 : (byte)200, 2f);

            renderer.DrawTextScreen(px + 30, optY + 25, descriptions[i], 130, 130, 150, 1.5f);
            renderer.DrawTextScreen(px + 30, optY + 43, "[COMING SOON]", 100, 100, 120, 1.2f);
        }
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
                InteractableType.ExitDoor => ((byte)255, (byte)100, (byte)100),
                _ => ((byte)200, (byte)200, (byte)200)
            };
            renderer.DrawRectScreen(ix - 1, iy - 1, 3, 3, ir, ig, ib);
        }
    }

    private enum OverlayType
    {
        None,
        Trade,
        Repair,
        Mission
    }

    private record TradeItem(string Name, int Cost, string Description);
}

/// <summary>Where the interior was entered from.</summary>
public enum InteriorOrigin
{
    Station,
    Settlement
}
