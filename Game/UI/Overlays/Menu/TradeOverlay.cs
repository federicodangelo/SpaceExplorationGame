using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Unified trade terminal: browse all resource prices, buy from station, sell cargo.
/// Confirm = Sell one unit, Secondary = Buy one unit. Color-coded prices.
/// Bottom panel shows trade route tips for the selected resource.
/// </summary>
public class TradeOverlay : ListPanelOverlay
{
    private static readonly ResourceType[] AllResources = Enum.GetValues<ResourceType>();

    private int _systemIndex = -1;
    private string _locationKey = "";
    private int[] _sellPrices = [];
    private int[] _buyPrices = [];
    private int[] _stock = [];
    private List<(int SystemIndex, string SystemName, int PricePerUnit)>[] _bestRoutes = [];

    protected override string Title => "TRADE TERMINAL";
    protected override Color3 TitleColor => new(100, 220, 255);
    protected override float PanelWidth => 650;
    protected override float PanelHeight => 130 + AllResources.Length * ItemHeight + RoutesPanelHeight + 20;
    protected override bool ShowCredits => true;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}: SELECT  " +
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}: SELL  " +
                   $"{input.GetActionHelpText(InputAction.MenuSecondaryAction)}: BUY  " +
                   $"{input.GetActionHelpText(InputAction.MenuBack)}: CLOSE";
        }
    }

    protected override int ItemCount => AllResources.Length;
    protected override float ItemHeight => 28f;
    protected override float ListOffsetY => 70f; // after header lines

    private float RoutesPanelHeight => 100f;

    public void Open(Game game, int systemIndex, string locationKey)
    {
        _systemIndex = systemIndex;
        _locationKey = locationKey;
        CachePrices(game);
        base.Open();
    }

    private void CachePrices(Game game)
    {
        _sellPrices = new int[AllResources.Length];
        _buyPrices = new int[AllResources.Length];
        _stock = new int[AllResources.Length];
        _bestRoutes = new List<(int, string, int)>[AllResources.Length];
        double currentTime = game.Player.Stats.PlayTimeSeconds;

        for (int i = 0; i < AllResources.Length; i++)
        {
            var resource = AllResources[i];
            _sellPrices[i] = SystemEconomy.GetSellPrice(game.Seeds, _systemIndex, _locationKey, resource);
            _buyPrices[i] = SystemEconomy.GetBuyPrice(game.Seeds, _systemIndex, _locationKey, resource);
            _stock[i] = game.Player.GetAvailableStock(game.Seeds, _systemIndex, _locationKey, resource, currentTime);
            _bestRoutes[i] = SystemEconomy.GetBestSellSystems(
                game.Seeds, game.GalaxyData, resource, _systemIndex, 3);
        }
    }

    // Confirm = Sell one unit of the selected resource
    protected override void OnItemConfirmed(Game game, int index)
    {
        var resource = AllResources[index];
        game.Player.Cargo.TryGetValue(resource, out int amount);
        if (amount <= 0)
        {
            SetStatus("NO CARGO TO SELL");
            return;
        }

        int unitPrice = _sellPrices[index];
        int earned = game.Player.SellCargo(resource, 1, unitPrice);
        if (earned > 0)
        {
            game.Player.RecordStockChange(_locationKey, resource, -1, game.Player.Stats.PlayTimeSeconds);
            _stock[index]++;
            SetStatus($"SOLD 1 {ResourceCatalog.Get(resource).Name.ToUpper()} FOR {earned} CR");
        }
    }

    // Secondary = Buy one unit of the selected resource
    protected override void OnItemSecondary(Game game, int index)
    {
        var resource = AllResources[index];
        int buyPrice = _buyPrices[index];

        if (_stock[index] <= 0)
        {
            SetStatus("OUT OF STOCK");
            return;
        }
        if (game.Player.Credits < buyPrice)
        {
            SetStatus("NOT ENOUGH CREDITS");
            return;
        }
        if (game.Player.CargoFree <= 0)
        {
            SetStatus("CARGO HOLD FULL");
            return;
        }

        int bought = game.Player.BuyCargo(resource, buyPrice);
        if (bought > 0)
        {
            game.Player.RecordStockChange(_locationKey, resource, +1, game.Player.Stats.PlayTimeSeconds);
            _stock[index]--;
            SetStatus($"BOUGHT 1 {ResourceCatalog.Get(resource).Name.ToUpper()} FOR {buyPrice} CR");
        }
    }

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        // Cargo capacity bar
        renderer.DrawTextScreen(panelX + 20, contentY + 5,
            $"CARGO: {game.Player.CargoUsed}/{game.Player.MaxCargo}",
            new Color3(200, 180, 100), 1.5f, panelW - 40f);

        renderer.DrawLineScreen(panelX + 15, contentY + 22, panelX + panelW - 15, contentY + 22,
            new Color3(60, 60, 100));

        // Column headers
        float headY = contentY + 28;
        renderer.DrawTextScreen(panelX + 20, headY, "RESOURCE",
            new Color3(150, 150, 180), 1.3f, 110f);
        renderer.DrawTextScreen(panelX + 140, headY, "SELL",
            new Color3(100, 255, 100), 1.3f, 55f);
        renderer.DrawTextScreen(panelX + 210, headY, "BUY",
            new Color3(255, 180, 80), 1.3f, 55f);
        renderer.DrawTextScreen(panelX + 280, headY, "BASE",
            new Color3(150, 150, 180), 1.3f, 55f);
        renderer.DrawTextScreen(panelX + 340, headY, "STOCK",
            new Color3(150, 150, 180), 1.3f, 50f);
        renderer.DrawTextScreen(panelX + 400, headY, "HOLD",
            new Color3(150, 150, 180), 1.3f, 50f);
        renderer.DrawTextScreen(panelX + 460, headY, "VALUE",
            new Color3(150, 150, 180), 1.3f, panelW - 475f);

        renderer.DrawLineScreen(panelX + 15, contentY + 44, panelX + panelW - 15, contentY + 44,
            new Color3(50, 50, 80));

        float listY = contentY + ListOffsetY;

        // Resource rows
        for (int i = 0; i < AllResources.Length; i++)
        {
            var resource = AllResources[i];
            var resInfo = ResourceCatalog.Get(resource);
            int basePrice = resInfo.ValuePerUnit;
            int sellPrice = _sellPrices[i];
            int buyPrice = _buyPrices[i];
            game.Player.Cargo.TryGetValue(resource, out int amount);

            bool selected = i == SelectedIndex;
            if (selected)
                renderer.DrawRectScreen(panelX + 10, listY - 2, panelW - 20, 26,
                    new Color4(40, 40, 80, 200));

            var nameColor = selected ? new Color3(255, 255, 255) : resInfo.Color;
            var sellColor = GetPriceColor(sellPrice, basePrice);
            var buyColor = GetBuyPriceColor(buyPrice, basePrice);

            // Resource name
            renderer.DrawTextScreen(panelX + 20, listY + 3, resInfo.Name.ToUpper(),
                nameColor, 1.5f, 115f);

            // Sell price (what station pays you)
            renderer.DrawTextScreen(panelX + 140, listY + 3, $"{sellPrice}",
                sellColor, 1.5f, 55f);

            // Buy price (what you pay station)
            renderer.DrawTextScreen(panelX + 210, listY + 3, $"{buyPrice}",
                buyColor, 1.5f, 55f);

            // Base price
            renderer.DrawTextScreen(panelX + 280, listY + 3, $"{basePrice}",
                new Color3(130, 130, 150), 1.4f, 55f);

            // Stock available
            int stock = _stock[i];
            var stockColor = stock > 0 ? new Color3(180, 180, 200) : new Color3(255, 80, 80);
            renderer.DrawTextScreen(panelX + 340, listY + 3, stock > 0 ? $"x{stock}" : "NONE",
                stockColor, 1.4f, 50f);

            // Cargo held
            if (amount > 0)
            {
                renderer.DrawTextScreen(panelX + 400, listY + 3, $"x{amount}",
                    new Color3(200, 200, 200), 1.5f, 50f);
                renderer.DrawTextScreen(panelX + 460, listY + 3, $"{amount * sellPrice} CR",
                    new Color3(255, 220, 80), 1.3f, panelW - 475f);
            }
            else
            {
                renderer.DrawTextScreen(panelX + 400, listY + 3, "-",
                    new Color3(80, 80, 100), 1.5f, 50f);
            }

            // Action hints on selected row
            if (selected)
            {
                float hintX = panelX + panelW - 125;
                if (amount > 0)
                    renderer.DrawTextScreen(hintX, listY + 3, "[SELL]",
                        new Color3(100, 255, 100), 1.2f, 50f);
                renderer.DrawTextScreen(hintX + 52, listY + 3, "[BUY]",
                    new Color3(255, 180, 80), 1.2f, 50f);
            }

            listY += ItemHeight;
        }

        // Trade routes panel for selected resource
        float routeY = listY + 10;
        renderer.DrawLineScreen(panelX + 15, routeY, panelX + panelW - 15, routeY,
            new Color3(60, 60, 100));

        var selResource = AllResources[SelectedIndex];
        var selInfo = ResourceCatalog.Get(selResource);
        renderer.DrawTextScreen(panelX + 20, routeY + 8,
            $"BEST SELL MARKETS FOR {selInfo.Name.ToUpper()}:",
            new Color3(100, 200, 255), 1.5f, panelW - 40f);

        var routes = _bestRoutes[SelectedIndex];
        if (routes.Count == 0)
        {
            renderer.DrawTextScreen(panelX + 20, routeY + 28, "NO OTHER SYSTEMS",
                new Color3(120, 120, 150), 1.4f, panelW - 40f);
        }
        else
        {
            for (int r = 0; r < routes.Count; r++)
            {
                var (_, name, price) = routes[r];
                var routePriceColor = GetPriceColor(price, _sellPrices[SelectedIndex]);

                string label = r == 0 ? ">>> " : "    ";
                renderer.DrawTextScreen(panelX + 20, routeY + 28 + r * 20,
                    $"{label}{name}", new Color3(180, 180, 200), 1.3f, 310f);
                renderer.DrawTextScreen(panelX + 360, routeY + 28 + r * 20,
                    $"{price} CR/u", routePriceColor, 1.3f, 100f);

                int priceDiff = price - _sellPrices[SelectedIndex];
                string routeDiff = priceDiff >= 0 ? $"+{priceDiff}" : $"{priceDiff}";
                renderer.DrawTextScreen(panelX + 470, routeY + 28 + r * 20,
                    routeDiff, routePriceColor, 1.3f, 80f);
            }
        }
    }

    /// <summary>Color-code sell price: green = above base (good to sell here), red = below base.</summary>
    internal static Color3 GetPriceColor(int currentPrice, int basePrice)
    {
        if (currentPrice > basePrice) return new Color3(100, 255, 100);
        if (currentPrice < basePrice) return new Color3(255, 120, 80);
        return new Color3(255, 220, 80);
    }

    /// <summary>Color-code buy price: green = below base (cheap to buy), red = above base (expensive).</summary>
    private static Color3 GetBuyPriceColor(int buyPrice, int basePrice)
    {
        if (buyPrice < basePrice) return new Color3(100, 255, 100);  // cheap — good buy
        if (buyPrice > basePrice) return new Color3(255, 120, 80);   // expensive
        return new Color3(255, 220, 80);
    }
}
