using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for selling cargo resources at stations and settlements.
/// Lists all cargo with sell prices, supports selling individual items or all at once.
/// </summary>
public class SellCargoOverlay : ListPanelOverlay
{
    private ResourceType[] _cargoKeys = [];

    protected override string Title => "CARGO TERMINAL";
    protected override Color3 TitleColor => new(255, 220, 80);
    protected override float PanelWidth => 500;
    protected override float PanelHeight => 160 + Math.Max(_cargoKeys.Length, 1) * 26
                                            + (_cargoKeys.Length > 0 ? 40 : 0);
    protected override bool ShowCredits => true;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}: SELECT  " +
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}: SELL  " +
                   $"{input.GetActionHelpText(InputAction.MenuBack)}: CLOSE";
        }
    }

    // Item count includes cargo items + 1 "SELL ALL" option (when cargo exists)
    protected override int ItemCount =>
        _cargoKeys.Length > 0 ? _cargoKeys.Length + 1 : 0;

    protected override float ItemHeight => 26f;
    protected override float ListOffsetY => 55f;   // after credits + cargo header

    public override void Open()
    {
        base.Open();
    }

    protected override void OnItemConfirmed(Game game, int index)
    {
        if (_cargoKeys.Length == 0) return;

        if (index < _cargoKeys.Length)
        {
            // Sell individual resource
            var resource = _cargoKeys[index];
            int earned = game.Player.SellCargo(resource);
            if (earned > 0)
                SetStatus($"SOLD FOR {earned} CREDITS");
            RefreshCargoKeys(game);
            ClampSelection();
        }
        else
        {
            // Sell all
            int earned = game.Player.SellAllCargo();
            if (earned > 0)
            {
                SetStatus($"SOLD ALL FOR {earned} CREDITS");
                RefreshCargoKeys(game);
                ClampSelection();
            }
        }
    }

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        // Refresh cargo keys each frame to stay in sync
        RefreshCargoKeys(game);

        // Cargo info
        renderer.DrawTextScreen(panelX + 15, contentY + 5,
            $"CARGO: {game.Player.CargoUsed}/{game.Player.MaxCargo}", new Color3(200, 180, 100), 1.5f, panelW - 30f);
        renderer.DrawLineScreen(panelX + 15, contentY + 25, panelX + panelW - 15, contentY + 25,
            new Color3(60, 60, 100));

        float listY = contentY + ListOffsetY;

        if (_cargoKeys.Length == 0)
        {
            renderer.DrawTextScreen(panelX + 20, listY, "CARGO HOLD EMPTY",
                new Color3(120, 120, 150), 2f, panelW - 35f);
            return;
        }

        for (int i = 0; i < _cargoKeys.Length; i++)
        {
            var resource = _cargoKeys[i];
            var resInfo = ResourceCatalog.Get(resource);
            int amount = game.Player.Cargo[resource];
            int value = amount * resInfo.ValuePerUnit;

            bool selected = i == SelectedIndex;
            if (selected)
                renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, 24,
                    new Color4(40, 40, 80, 200));

            byte tr = selected ? (byte)255 : resInfo.Color.R;
            byte tg = selected ? (byte)255 : resInfo.Color.G;
            byte tb = selected ? (byte)255 : resInfo.Color.B;

            renderer.DrawTextScreen(panelX + 20, listY + 2, resInfo.Name.ToUpper(),
                new Color3(tr, tg, tb), 1.8f, panelW - 35f);
            renderer.DrawTextScreen(panelX + 180, listY + 2, $"x{amount}",
                new Color3(200, 200, 200), 1.8f, 90f);
            renderer.DrawTextScreen(panelX + 280, listY + 2, $"= {value} CR",
                new Color3(255, 220, 80), 1.8f, panelW - 375f);

            if (selected)
                renderer.DrawTextScreen(panelX + panelW - 80, listY + 2, "[SELL]",
                    new Color3(100, 255, 100), 1.5f, 70f);

            listY += ItemHeight;
        }

        // SELL ALL option
        listY += 10;
        bool sellAllSelected = SelectedIndex == _cargoKeys.Length;
        if (sellAllSelected)
            renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, 28,
                new Color4(40, 60, 40, 200));

        int totalValue = 0;
        foreach (var (resource, amount) in game.Player.Cargo)
            totalValue += amount * ResourceCatalog.Get(resource).ValuePerUnit;

        byte sr = sellAllSelected ? (byte)100 : (byte)180;
        byte sg = sellAllSelected ? (byte)255 : (byte)180;
        byte sb = sellAllSelected ? (byte)100 : (byte)180;
        renderer.DrawTextScreen(panelX + 20, listY + 2, $"SELL ALL ({totalValue} CREDITS)",
            new Color3(sr, sg, sb), 2f, panelW - 35f);
    }

    private void RefreshCargoKeys(Game game)
    {
        var keys = new List<ResourceType>();
        foreach (var (resource, amount) in game.Player.Cargo)
        {
            if (amount > 0) keys.Add(resource);
        }
        _cargoKeys = keys.ToArray();
    }
}
