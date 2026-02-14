using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI;

/// <summary>
/// Reusable menu widget that handles keyboard / mouse navigation and rendering.
/// Used by main menu, pause menu, station menu, service overlays, etc.
/// </summary>
public class MenuWidget
{
    private int _selected;
    private readonly string[] _labels;
    private readonly string[]? _descriptions;

    // ── Public state ──────────────────────────────────────────────
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = _labels.Length > 0 ? Math.Clamp(value, 0, _labels.Length - 1) : 0;
    }

    public int ItemCount => _labels.Length;
    public bool IsSelected(int index) => index == _selected;

    // ── Styling (set via init properties) ─────────────────────────
    public float ItemHeight { get; init; } = 50f;
    public float SelectedScale { get; init; } = 2.5f;
    public float NormalScale { get; init; } = 2f;
    public (byte R, byte G, byte B) SelectedColor { get; init; } = (220, 240, 255);
    public (byte R, byte G, byte B) NormalColor { get; init; } = (140, 140, 160);
    public (byte R, byte G, byte B) HighlightBg { get; init; } = (40, 60, 120);
    public byte HighlightAlpha { get; init; } = 180;
    public bool CenterAlign { get; init; } = false;
    public float DescriptionScale { get; init; } = 1.5f;
    public (byte R, byte G, byte B) DescriptionColor { get; init; } = (160, 160, 180);

    // ── Constructor ───────────────────────────────────────────────

    public MenuWidget(string[] labels, string[]? descriptions = null)
    {
        _labels = labels;
        _descriptions = descriptions;
    }

    // ── Update (keyboard only) ────────────────────────────────────

    /// <summary>
    /// Process keyboard navigation (Up/Down/W/S) and confirm (Return/E).
    /// Returns the confirmed index, or -1 if nothing was confirmed.
    /// </summary>
    public int Update(InputManager input)
    {
        if (_labels.Length == 0) return -1;

        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selected = (_selected - 1 + _labels.Length) % _labels.Length;

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selected = (_selected + 1) % _labels.Length;

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
            return _selected;

        return -1;
    }

    // ── Update (keyboard + mouse) ─────────────────────────────────

    /// <summary>
    /// Process keyboard navigation, mouse hover, and mouse click.
    /// <paramref name="menuScreenX"/> and <paramref name="menuScreenY"/> define
    /// the top-left of the first item in screen coordinates.
    /// <paramref name="itemWidth"/> is the clickable width of each item.
    /// Returns the confirmed index, or -1 if nothing was confirmed.
    /// </summary>
    public int Update(InputManager input, float menuScreenX, float menuScreenY, float itemWidth)
    {
        if (_labels.Length == 0) return -1;

        // Mouse hover / click
        float mx = input.MouseX;
        float my = input.MouseY;
        for (int i = 0; i < _labels.Length; i++)
        {
            float optY = menuScreenY + i * ItemHeight;
            if (mx >= menuScreenX && mx <= menuScreenX + itemWidth &&
                my >= optY && my <= optY + ItemHeight)
            {
                _selected = i;
                if (input.IsMousePressed(1))
                    return i;
                break;
            }
        }

        // Keyboard (after mouse so keyboard can still override)
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selected = (_selected - 1 + _labels.Length) % _labels.Length;

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selected = (_selected + 1) % _labels.Length;

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
            return _selected;

        return -1;
    }

    // ── Render ────────────────────────────────────────────────────

    /// <summary>
    /// Render the menu at the given position.
    /// <paramref name="x"/> is the left edge (left-aligned) or center area start (centered).
    /// <paramref name="y"/> is the top of the first item.
    /// <paramref name="width"/> is used for highlight rect width and centering.
    /// </summary>
    public void Render(SpriteRenderer renderer, float x, float y, float width)
    {
        for (int i = 0; i < _labels.Length; i++)
        {
            float optY = y + i * ItemHeight;
            bool sel = i == _selected;
            float scale = sel ? SelectedScale : NormalScale;
            var (cr, cg, cb) = sel ? SelectedColor : NormalColor;

            // Selection highlight
            if (sel)
                renderer.DrawRectScreen(x, optY - 5, width, ItemHeight - 10, HighlightBg.R, HighlightBg.G, HighlightBg.B, HighlightAlpha);

            if (CenterAlign)
            {
                // Arrow indicator to the left of centered text
                if (sel)
                {
                    float textW = renderer.MeasureText(_labels[i], scale);
                    float textX = x + width / 2f - textW / 2f;
                    renderer.DrawTextScreen(textX - renderer.MeasureText("> ", scale), optY, ">", cr, cg, cb, scale);
                    renderer.DrawTextScreen(textX, optY, _labels[i], cr, cg, cb, scale);
                }
                else
                {
                    float textW = renderer.MeasureText(_labels[i], scale);
                    renderer.DrawTextScreen(x + width / 2f - textW / 2f, optY, _labels[i], cr, cg, cb, scale);
                }
            }
            else
            {
                // Left-aligned with > prefix
                string label = sel ? $"> {_labels[i]}" : _labels[i];
                float textX = sel ? x + 10 : x + 20;
                renderer.DrawTextScreen(textX, optY, label, cr, cg, cb, scale);
            }
        }

        // Description for selected item (below the list)
        if (_descriptions != null && _selected >= 0 && _selected < _descriptions.Length)
        {
            float descY = y + _labels.Length * ItemHeight + 10;
            string desc = _descriptions[_selected];
            if (CenterAlign)
            {
                float descW = renderer.MeasureText(desc, DescriptionScale);
                renderer.DrawRectScreen(x + width / 2f - descW / 2f - 8, descY - 4, descW + 16, 22, 0, 0, 0, 160);
                renderer.DrawTextScreen(x + width / 2f - descW / 2f, descY, desc, DescriptionColor.R, DescriptionColor.G, DescriptionColor.B, DescriptionScale);
            }
            else
            {
                renderer.DrawTextScreen(x + 20, descY, desc, DescriptionColor.R, DescriptionColor.G, DescriptionColor.B, DescriptionScale);
            }
        }
    }

    /// <summary>Total pixel height of the menu items (excluding description).</summary>
    public float TotalHeight => _labels.Length * ItemHeight;
}
