using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI;

/// <summary>
/// Represents a single menu option with an associated enum value, display label, and optional description.
/// </summary>
public record struct MenuOption<T>(T Value, string Label, string? Description = null, bool Enabled = true, string? DisabledHint = null) where T : struct, Enum;

/// <summary>
/// Reusable menu widget that handles keyboard / mouse navigation and rendering.
/// Generic over an enum type <typeparamref name="T"/> for type-safe option handling.
/// Used by main menu, in-game menu, station menu, service overlays, etc.
/// </summary>
public class MenuWidget<T> where T : struct, Enum
{
    private int _selected;
    private readonly MenuOption<T>[] _options;

    // ── Public state ──────────────────────────────────────────────
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = _options.Length > 0 ? Math.Clamp(value, 0, _options.Length - 1) : 0;
    }

    public T SelectedValue => _options[_selected].Value;
    public int ItemCount => _options.Length;
    public IReadOnlyList<MenuOption<T>> Options => _options;
    public bool IsSelected(T value) => EqualityComparer<T>.Default.Equals(_options[_selected].Value, value);

    // ── Styling (set via init properties) ─────────────────────────
    public float ItemHeight { get; init; } = 50f;
    public float SelectedScale { get; init; } = 2.5f;
    public float NormalScale { get; init; } = 2f;
    public (byte R, byte G, byte B) SelectedColor { get; init; } = (220, 240, 255);
    public (byte R, byte G, byte B) NormalColor { get; init; } = (140, 140, 160);
    public (byte R, byte G, byte B) DisabledColor { get; init; } = (80, 80, 90);
    public (byte R, byte G, byte B) DisabledHintColor { get; init; } = (200, 80, 80);
    public float DisabledHintScale { get; init; } = 1.5f;
    public (byte R, byte G, byte B) HighlightBg { get; init; } = (40, 60, 120);
    public byte HighlightAlpha { get; init; } = 180;
    public bool CenterAlign { get; init; } = false;
    public float DescriptionScale { get; init; } = 1.5f;
    public (byte R, byte G, byte B) DescriptionColor { get; init; } = (160, 160, 180);

    // ── Constructor ───────────────────────────────────────────────

    public MenuWidget(MenuOption<T>[] options)
    {
        _options = options;
    }

    // ── Update (keyboard only) ────────────────────────────────────

    /// <summary>
    /// Process keyboard navigation (Up/Down/W/S) and confirm (Return/E).
    /// Returns the confirmed enum value, or null if nothing was confirmed.
    /// Disabled options can be navigated to but cannot be confirmed.
    /// </summary>
    public T? Update(InputManager input)
    {
        if (_options.Length == 0) return null;

        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selected = (_selected - 1 + _options.Length) % _options.Length;

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selected = (_selected + 1) % _options.Length;

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_options[_selected].Enabled)
                return _options[_selected].Value;
        }

        return null;
    }

    // ── Update (keyboard + mouse) ─────────────────────────────────

    /// <summary>
    /// Process keyboard navigation, mouse hover, and mouse click.
    /// <paramref name="menuScreenX"/> and <paramref name="menuScreenY"/> define
    /// the top-left of the first item in screen coordinates.
    /// <paramref name="itemWidth"/> is the clickable width of each item.
    /// Returns the confirmed enum value, or null if nothing was confirmed.
    /// </summary>
    public T? Update(InputManager input, float menuScreenX, float menuScreenY, float itemWidth)
    {
        if (_options.Length == 0) return null;

        // Mouse hover / click
        float mx = input.MouseX;
        float my = input.MouseY;
        for (int i = 0; i < _options.Length; i++)
        {
            float optY = menuScreenY + i * ItemHeight;
            if (mx >= menuScreenX && mx <= menuScreenX + itemWidth &&
                my >= optY && my <= optY + ItemHeight)
            {
                _selected = i;
                if (input.IsMousePressed(1) && _options[i].Enabled)
                    return _options[i].Value;
                break;
            }
        }

        // Keyboard (after mouse so keyboard can still override)
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selected = (_selected - 1 + _options.Length) % _options.Length;

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selected = (_selected + 1) % _options.Length;

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_options[_selected].Enabled)
                return _options[_selected].Value;
        }

        return null;
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
        for (int i = 0; i < _options.Length; i++)
        {
            float optY = y + i * ItemHeight;
            bool sel = i == _selected;
            bool enabled = _options[i].Enabled;
            float scale = sel ? SelectedScale : NormalScale;
            var (cr, cg, cb) = !enabled ? DisabledColor : sel ? SelectedColor : NormalColor;
            string label = _options[i].Label;

            // Selection highlight (only for enabled items)
            if (sel && enabled)
                renderer.DrawRectScreen(x, optY - 5, width, ItemHeight - 10, HighlightBg.R, HighlightBg.G, HighlightBg.B, HighlightAlpha);

            if (CenterAlign)
            {
                // Arrow indicator to the left of centered text
                if (sel)
                {
                    float textW = renderer.MeasureText(label, scale);
                    float textX = x + width / 2f - textW / 2f;
                    renderer.DrawTextScreen(textX - renderer.MeasureText("> ", scale), optY, ">", cr, cg, cb, scale);
                    renderer.DrawTextScreen(textX, optY, label, cr, cg, cb, scale);
                }
                else
                {
                    float textW = renderer.MeasureText(label, scale);
                    renderer.DrawTextScreen(x + width / 2f - textW / 2f, optY, label, cr, cg, cb, scale);
                }
            }
            else
            {
                // Left-aligned with > prefix
                string displayLabel = sel ? $"> {label}" : label;
                float textX = sel ? x + 10 : x + 20;
                renderer.DrawTextScreen(textX, optY, displayLabel, cr, cg, cb, scale);
            }

            // Disabled hint text (shown below the label within the same item area)
            if (!enabled && _options[i].DisabledHint != null)
            {
                string hint = _options[i].DisabledHint!;
                if (CenterAlign)
                {
                    float hintW = renderer.MeasureText(hint, DisabledHintScale);
                    renderer.DrawTextScreen(x + width / 2f - hintW / 2f, optY + scale * 8 + 4, hint,
                        DisabledHintColor.R, DisabledHintColor.G, DisabledHintColor.B, DisabledHintScale);
                }
                else
                {
                    renderer.DrawTextScreen(x + 20, optY + scale * 8 + 4, hint,
                        DisabledHintColor.R, DisabledHintColor.G, DisabledHintColor.B, DisabledHintScale);
                }
            }
        }

        // Description for selected item (below the list)
        string? description = _options[_selected].Description;
        if (description != null)
        {
            float descY = y + _options.Length * ItemHeight + 10;
            if (CenterAlign)
            {
                float descW = renderer.MeasureText(description, DescriptionScale);
                renderer.DrawRectScreen(x + width / 2f - descW / 2f - 8, descY - 4, descW + 16, 22, 0, 0, 0, 160);
                renderer.DrawTextScreen(x + width / 2f - descW / 2f, descY, description, DescriptionColor.R, DescriptionColor.G, DescriptionColor.B, DescriptionScale);
            }
            else
            {
                renderer.DrawTextScreen(x + 20, descY, description, DescriptionColor.R, DescriptionColor.G, DescriptionColor.B, DescriptionScale);
            }
        }
    }

    /// <summary>Total pixel height of the menu items (excluding description).</summary>
    public float TotalHeight => _options.Length * ItemHeight;
}
