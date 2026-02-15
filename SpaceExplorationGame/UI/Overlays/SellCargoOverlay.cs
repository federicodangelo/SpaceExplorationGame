using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for selling cargo resources at stations and settlements.
/// Lists all cargo with sell prices, supports selling individual items or all at once.
/// </summary>
public class SellCargoOverlay : OverlayBase
{
    private int _selectedIndex;
    private string? _statusMessage;
    private float _statusTimer;

    public void Open() => IsOpen = true;

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        var cargoKeys = GetCargoKeys(game.Player);
        int itemCount = cargoKeys.Length;

        // Navigate: Up/Down through individual resources, then "SELL ALL" at the bottom
        int totalOptions = itemCount + (itemCount > 0 ? 1 : 0); // +1 for SELL ALL

        if (totalOptions > 0)
        {
            if (input.IsKeyPressed(SDL.Scancode.Up))
                _selectedIndex = (_selectedIndex - 1 + totalOptions) % totalOptions;
            if (input.IsKeyPressed(SDL.Scancode.Down))
                _selectedIndex = (_selectedIndex + 1) % totalOptions;

            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
            {
                if (_selectedIndex < itemCount)
                {
                    // Sell individual resource
                    var resource = cargoKeys[_selectedIndex];
                    int earned = game.Player.SellCargo(resource);
                    if (earned > 0)
                    {
                        _statusMessage = $"SOLD FOR {earned} CREDITS";
                        _statusTimer = 2f;
                    }
                    // If we just sold the last index, clamp
                    if (_selectedIndex >= GetCargoKeys(game.Player).Length)
                        _selectedIndex = Math.Max(0, GetCargoKeys(game.Player).Length);
                }
                else
                {
                    // Sell all
                    int earned = game.Player.SellAllCargo();
                    if (earned > 0)
                    {
                        _statusMessage = $"SOLD ALL FOR {earned} CREDITS";
                        _statusTimer = 2f;
                        _selectedIndex = 0;
                    }
                }
            }
        }

        return true;
    }

    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;
        if (_statusTimer > 0)
        {
            _statusTimer -= dt;
            if (_statusTimer <= 0) _statusMessage = null;
        }
    }

    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent background
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 150);

        var cargoKeys = GetCargoKeys(game.Player);
        int itemCount = cargoKeys.Length;

        float panelW = 500;
        float panelH = 160 + Math.Max(itemCount, 1) * 26 + (itemCount > 0 ? 40 : 0);
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        // Panel border
        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        // Title
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "CARGO TERMINAL", 255, 220, 80, 2.5f);
        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, 60, 60, 100);

        // Credits
        renderer.DrawTextScreen(panelX + 15, panelY + 55, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);
        renderer.DrawTextScreen(panelX + 15, panelY + 80, $"CARGO: {game.Player.CargoUsed}/{game.Player.MaxCargo}", 200, 180, 100, 1.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 100, panelX + panelW - 15, panelY + 100, 60, 60, 100);

        float listY = panelY + 110;

        if (itemCount == 0)
        {
            renderer.DrawTextScreen(panelX + 20, listY, "CARGO HOLD EMPTY", 120, 120, 150, 2f);
        }
        else
        {
            for (int i = 0; i < itemCount; i++)
            {
                var resource = cargoKeys[i];
                var resInfo = ResourceCatalog.Get(resource);
                int amount = game.Player.Cargo[resource];
                int value = amount * resInfo.ValuePerUnit;

                bool selected = i == _selectedIndex;
                if (selected)
                    renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, 24, 40, 40, 80, 200);

                byte tr = selected ? (byte)255 : resInfo.R;
                byte tg = selected ? (byte)255 : resInfo.G;
                byte tb = selected ? (byte)255 : resInfo.B;

                renderer.DrawTextScreen(panelX + 20, listY + 2, $"{resInfo.Name.ToUpper()}", tr, tg, tb, 1.8f);
                renderer.DrawTextScreen(panelX + 180, listY + 2, $"x{amount}", 200, 200, 200, 1.8f);
                renderer.DrawTextScreen(panelX + 280, listY + 2, $"= {value} CR", 255, 220, 80, 1.8f);

                if (selected)
                    renderer.DrawTextScreen(panelX + panelW - 80, listY + 2, "[SELL]", 100, 255, 100, 1.5f);

                listY += 26;
            }

            // SELL ALL option
            listY += 10;
            bool sellAllSelected = _selectedIndex == itemCount;
            if (sellAllSelected)
                renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, 28, 40, 60, 40, 200);

            int totalValue = 0;
            foreach (var (resource, amount) in game.Player.Cargo)
                totalValue += amount * ResourceCatalog.Get(resource).ValuePerUnit;

            byte sr = sellAllSelected ? (byte)100 : (byte)180;
            byte sg = sellAllSelected ? (byte)255 : (byte)180;
            byte sb = sellAllSelected ? (byte)100 : (byte)180;
            renderer.DrawTextScreen(panelX + 20, listY + 2, $"SELL ALL ({totalValue} CREDITS)", sr, sg, sb, 2f);
        }

        // Status message
        if (_statusMessage != null)
        {
            float smY = panelY + panelH - 50;
            renderer.DrawTextScreen(panelX + 20, smY, _statusMessage, 100, 255, 100, 2f);
        }

        // Close hint
        renderer.DrawTextScreen(panelX + 10, panelY + panelH - 25, "UP/DOWN: SELECT  ENTER: SELL  ESC: CLOSE", 100, 100, 130, 1.5f);
    }

    private static ResourceType[] GetCargoKeys(PlayerData player)
    {
        var keys = new List<ResourceType>();
        foreach (var (resource, amount) in player.Cargo)
        {
            if (amount > 0) keys.Add(resource);
        }
        return keys.ToArray();
    }
}
