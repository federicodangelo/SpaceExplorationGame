using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay that displays context-appropriate keyboard controls.
/// Any key press or click outside dismisses it.
/// </summary>
public class ControlsOverlay : PanelOverlayBase
{
    private string[] _controls = [];

    /// <summary>The current game state type, determines which controls are shown.</summary>
    public GameStateType StateType { get; set; }

    protected override string Title => "CONTROLS";
    protected override Color3 TitleColor => new(200, 210, 255);
    protected override float PanelWidth => 380;
    protected override float PanelHeight => 55 + _controls.Length * 22f + 35;
    protected override string? ControlsHint => "PRESS ANY KEY TO CLOSE";

    public override void Open()
    {
        _controls = GetControlsForState(StateType);
        base.Open();
    }

    protected override void ProcessInput(Game game, InputManager input)
    {
        // Any key dismisses
        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E)
            || input.IsKeyPressed(SDL.Scancode.Space))
            Close();
    }

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        float cy = contentY;
        float centerX = panelX + panelW / 2f;
        foreach (var line in _controls)
        {
            float lw = renderer.MeasureText(line, 1.5f);
            renderer.DrawTextScreen(centerX - lw / 2f, cy, line,
                new Color3(180, 180, 200), 1.5f);
            cy += 22f;
        }
    }

    private static string[] GetControlsForState(GameStateType state) => state switch
    {
        GameStateType.SolarSystem =>
        [
            "W / UP ............. THRUST",
            "A / D .............. ROTATE",
            "S / DOWN ........... BRAKE",
            "SCROLL ............. ZOOM",
            "E .................. INTERACT",
            "M .................. GALAXY MAP",
            "SPACE .............. SHOOT",
            "ESC ................ MENU"
        ],
        GameStateType.PlanetSurface =>
        [
            "WASD / ARROWS ...... MOVE",
            "SCROLL ............. ZOOM",
            "E .................. INTERACT",
            "SPACE / LMB ........ SHOOT",
            "ESC ................ MENU"
        ],
        GameStateType.Interior =>
        [
            "WASD / ARROWS ...... MOVE",
            "SCROLL ............. ZOOM",
            "E .................. INTERACT",
            "ESC ................ MENU"
        ],
        _ => ["ESC ................ MENU"]
    };
}
