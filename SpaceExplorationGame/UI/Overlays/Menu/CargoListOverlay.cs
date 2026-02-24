using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay opened from the in-game menu that shows current cargo contents.
/// Allows discarding one unit from the selected resource or discarding all cargo at once.
/// </summary>
public class CargoListOverlay : ListPanelOverlay
{
    private ResourceType[] _cargoKeys = [];
    private bool _confirmDiscardAllArmed;

    protected override string Title => "CARGO HOLD";
    protected override Color3 TitleColor => new(170, 220, 255);
    protected override float PanelWidth => 560;
    protected override float PanelHeight => 200 + Math.Max(_cargoKeys.Length, 1) * ItemHeight;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            if (_cargoKeys.Length == 0)
                return $"{input.GetActionHelpText(InputAction.MenuBack)}: BACK";

            bool discardAllSelected = SelectedIndex == _cargoKeys.Length;
            if (discardAllSelected && _confirmDiscardAllArmed)
            {
                return $"{input.GetActionHelpText(InputAction.MenuConfirm)}: CONFIRM DISCARD ALL  " +
                       $"{input.GetActionHelpText(InputAction.MenuBack)}: BACK";
            }

            return $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}: SELECT  " +
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}: DISCARD  " +
                   $"{input.GetActionHelpText(InputAction.MenuBack)}: BACK";
        }
    }

    protected override int ItemCount => _cargoKeys.Length > 0 ? _cargoKeys.Length + 1 : 0;
    protected override float ItemHeight => 28f;
    protected override float ListOffsetY => 56f;

    public override void Open()
    {
        _confirmDiscardAllArmed = false;
        base.Open();
    }

    protected override void OnItemConfirmed(Game game, int index)
    {
        RefreshCargoKeys(game);

        if (_cargoKeys.Length == 0)
        {
            _confirmDiscardAllArmed = false;
            return;
        }

        if (index < _cargoKeys.Length)
        {
            _confirmDiscardAllArmed = false;
            var resource = _cargoKeys[index];
            if (game.Player.TryDiscardOneCargo(resource))
            {
                var name = ResourceCatalog.Get(resource).Name.ToUpper();
                SetStatus($"DISCARDED 1 {name}", 2f);
            }
        }
        else
        {
            if (!_confirmDiscardAllArmed)
            {
                _confirmDiscardAllArmed = true;
                SetStatus("PRESS DISCARD AGAIN TO CONFIRM ALL", 2.5f);
            }
            else
            {
                int units = game.Player.DiscardAllCargo();
                _confirmDiscardAllArmed = false;
                if (units > 0)
                    SetStatus($"DISCARDED ALL CARGO ({units} UNITS)", 2f);
            }
        }

        RefreshCargoKeys(game);
        ClampSelection();

        if (SelectedIndex != _cargoKeys.Length)
            _confirmDiscardAllArmed = false;
    }

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        RefreshCargoKeys(game);

        renderer.DrawTextScreen(panelX + 15, contentY + 6,
            $"CARGO: {game.Player.CargoUsed}/{game.Player.MaxCargo}", new Color3(180, 200, 240), 1.5f);
        renderer.DrawLineScreen(panelX + 15, contentY + 25, panelX + panelW - 15, contentY + 25,
            new Color3(60, 60, 100));

        float listY = contentY + ListOffsetY;

        if (_cargoKeys.Length == 0)
        {
            string empty = "CARGO HOLD EMPTY";
            float emptyW = renderer.MeasureText(empty, 2f);
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - emptyW / 2f, listY + 8,
                empty, new Color3(120, 130, 150), 2f);
            return;
        }

        for (int i = 0; i < _cargoKeys.Length; i++)
        {
            var resource = _cargoKeys[i];
            var info = ResourceCatalog.Get(resource);
            int amount = game.Player.Cargo[resource];
            bool selected = i == SelectedIndex;

            if (selected)
                renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, ItemHeight - 2,
                    new Color4(40, 50, 80, 200));

            byte r = selected ? (byte)255 : info.Color.R;
            byte g = selected ? (byte)255 : info.Color.G;
            byte b = selected ? (byte)255 : info.Color.B;

            renderer.DrawTextScreen(panelX + 20, listY + 3, info.Name.ToUpper(), new Color3(r, g, b), 1.8f);
            renderer.DrawTextScreen(panelX + 220, listY + 3, $"x{amount}", new Color3(220, 220, 220), 1.8f);

            if (selected)
                renderer.DrawTextScreen(panelX + panelW - 170, listY + 3, "[DISCARD 1]", new Color3(255, 160, 160), 1.5f);

            listY += ItemHeight;
        }

        bool discardAllSelected = SelectedIndex == _cargoKeys.Length;
        if (discardAllSelected)
            renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, ItemHeight - 2,
                new Color4(70, 30, 30, 210));

        byte ar = discardAllSelected ? (byte)255 : (byte)210;
        byte ag = discardAllSelected ? (byte)150 : (byte)110;
        byte ab = discardAllSelected ? (byte)150 : (byte)110;
        renderer.DrawTextScreen(panelX + 20, listY + 3, "DISCARD ALL CARGO", new Color3(ar, ag, ab), 1.9f);
    }

    private void RefreshCargoKeys(Game game)
    {
        var keys = new List<ResourceType>();
        foreach (var (resource, amount) in game.Player.Cargo)
        {
            if (amount > 0)
                keys.Add(resource);
        }

        _cargoKeys = keys.ToArray();
    }
}
