using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for the main menu start-option selection.
/// Renders inside a styled panel with the game title above and seed/controls below.
/// </summary>
public class MainMenuOverlay : MenuPanelOverlayBase<StartOption>
{
    private static readonly MenuOption<StartOption>[] Options =
    [
        new(StartOption.StarSystem, "STAR SYSTEM", "Start inside a random star system, ready to explore"),
        new(StartOption.GalaxyMap, "GALAXY MAP", "Begin at the galaxy overview and choose your destination"),
        new(StartOption.SpaceStation, "SPACE STATION", "Dock at a random space station"),
        new(StartOption.SpaceStationInside, "INSIDE SPACE STATION", "Walk around inside a random space station"),
        new(StartOption.PlanetSurface, "PLANET SURFACE", "Land directly on a random planet's surface"),
        new(StartOption.Settlement, "SETTLEMENT", "Start at a settlement on an inhabited planet"),
        new(StartOption.SettlementInside, "INSIDE SETTLEMENT", "Walk around inside a random settlement")
    ];

    private readonly TextInputOverlay _seedInputOverlay = new();

    /// <summary>Fired when the player confirms a start option.</summary>
    public StartOption? SelectedOption { get; set; }

    /// <summary>Fired when the player wants to change the seed.</summary>
    public ulong? NewSeed { get; set; }

    /// <summary>Fired when the player wants to randomize the seed.</summary>
    public bool RandomizeSeed { get; set; }

    /// <summary>Fired when the player wants to re-roll the starting location.</summary>
    public bool RerollLocation { get; set; }

    /// <summary>Top Y position of the panel, for external layout (e.g. title positioning).</summary>
    public float PanelTop => PanelY;

    /// <summary>Current seed to display.</summary>
    public ulong CurrentSeed { get; set; }

    /// <summary>Location preview text.</summary>
    public string? LocationPreview { get; set; }

    /// <summary>Currently selected start option value.</summary>
    public StartOption SelectedValue => Menu.SelectedValue;

    // ── Panel configuration ──

    protected override string Title => "CHOOSE YOUR STARTING POINT";
    protected override Color3 TitleColor => new(180, 200, 255);
    protected override float PanelWidth => 640;
    //protected override float PanelHeight => base.PanelHeight + 240; // Increased to fit seed controls and location preview
    protected override float BottomPadding => base.BottomPadding + 190;
    protected override bool CloseOnClickOutside => false;
    protected override byte DimAlpha => 0; // MainMenuState draws its own background
    protected override string? ControlsHint => "UP/DOWN: SELECT  ENTER: CONFIRM  R: RE-ROLL  S: EDIT SEED  N: RANDOM SEED";

    // ── Constructor ──

    public MainMenuOverlay()
    {
        Menu = new MenuWidget<StartOption>(Options)
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.5f,
            NormalScale = 2f,
            SelectedColor = new Color3(220, 240, 255),
            NormalColor = new Color3(140, 140, 160),
            HighlightBg = new Color3(40, 60, 120),
            HighlightAlpha = 180,
            DescriptionScale = 1.5f,
            DescriptionColor = new Color3(160, 160, 180)
        };

        RegisterSubOverlay(_seedInputOverlay);
    }

    // ── Open ──

    public override void Open()
    {
        base.Open();
        SelectedOption = null;
        NewSeed = null;
        RandomizeSeed = false;
        RerollLocation = false;
        LocationPreview = null;
    }

    // ── Escape does nothing on main menu ──

    protected override void OnEscapePressed() { }

    // ── Selection ──

    protected override void OnOptionSelected(Game game, StartOption option)
    {
        SelectedOption = option;
    }

    // ── Custom input processing ──

    protected override void ProcessInput(Game game, InputManager input)
    {
        // Check if seed input was confirmed
        if (!_seedInputOverlay.IsOpen)
        {
            var confirmedSeed = _seedInputOverlay.TakeConfirmedValue();
            if (confirmedSeed != null && ulong.TryParse(confirmedSeed, out ulong newSeed))
            {
                NewSeed = newSeed;
            }
        }

        // Don't process other input while seed input is open
        if (_seedInputOverlay.IsOpen)
        {
            return;
        }

        // 'S' key - Change seed
        if (input.IsKeyPressed(SDL3.SDL.Scancode.S))
        {
            _seedInputOverlay.Open("ENTER GALAXY SEED", CurrentSeed.ToString(), numericOnly: true, maxLength: 20);
            return;
        }

        // 'N' key - Random seed
        if (input.IsKeyPressed(SDL3.SDL.Scancode.N))
        {
            RandomizeSeed = true;
            return;
        }

        // 'R' key - Re-roll location
        if (input.IsKeyPressed(SDL3.SDL.Scancode.R))
        {
            RerollLocation = true;
            return;
        }

        // Default menu input processing
        base.ProcessInput(game, input);
    }

    // ── Custom content rendering ──

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer, float panelX, float contentY, float panelW, float contentH)
    {
        // Render the menu
        Menu.Render(renderer, MenuX, MenuY, MenuWidth, PanelBottom);

        // Render separator line below menu
        renderer.DrawLineScreen(panelX + 15, MenuY + Menu.TotalHeight + 5, panelX + panelW - 15, MenuY + Menu.TotalHeight + 5, new Color3(60, 80, 140));        

        // Seed control section
        float seedSectionY = MenuY + Menu.TotalHeight + 20;
        RenderSeedSection(renderer, panelX, seedSectionY, panelW);

        // Location preview section
        float previewSectionY = seedSectionY + 80;
        RenderLocationPreview(renderer, panelX, previewSectionY, panelW);
    }

    private void RenderSeedSection(SpriteRenderer renderer, float panelX, float y, float panelW)
    {
        // Section header
        renderer.DrawTextScreen(panelX + 15, y, "GALAXY SEED", new Color3(180, 200, 220), 2f);

        // Seed value background box
        float boxX = panelX + 15;
        float boxY = y + 30;
        float boxW = panelW - 30;
        float boxH = 35;

        renderer.DrawRectScreen(boxX, boxY, boxW, boxH, new Color4(20, 30, 50, 200));
        renderer.DrawRectScreen(boxX - 1, boxY - 1, boxW + 2, boxH + 2, new Color4(60, 80, 140, 150));

        // Seed value text
        string seedText = CurrentSeed.ToString();
        renderer.DrawTextScreen(boxX + 10, boxY + 8, seedText, new Color3(120, 200, 255), 2f);
    }

    private void RenderLocationPreview(SpriteRenderer renderer, float panelX, float y, float panelW)
    {
        // Section header
        renderer.DrawTextScreen(panelX + 15, y, "STARTING LOCATION", new Color3(180, 200, 220), 2f);

        // Preview box
        float boxX = panelX + 15;
        float boxY = y + 30;
        float boxW = panelW - 30;
        float boxH = 42;

        renderer.DrawRectScreen(boxX, boxY, boxW, boxH, new Color4(20, 30, 50, 200));
        renderer.DrawRectScreen(boxX - 1, boxY - 1, boxW + 2, boxH + 2, new Color4(60, 80, 140, 150));

        // Preview text
        if (!string.IsNullOrEmpty(LocationPreview))
        {
            // Split into multiple lines if needed
            string[] lines = LocationPreview.Split('\n');
            float lineY = boxY + 8;
            foreach (var line in lines)
            {
                renderer.DrawTextScreen(boxX + 10, lineY, line, new Color3(200, 220, 240), 1.5f);
                lineY += 18;
            }
        }
        else
        {
            renderer.DrawTextScreen(boxX + 10, boxY + 20, "Select a starting point above", new Color3(140, 140, 160), 1.5f);
        }
    }
}
