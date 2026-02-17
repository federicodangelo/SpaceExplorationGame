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

    // When the user navigates with the keyboard we ignore mouse hover
    // until the physical mouse position actually changes.
    private bool _ignoreMouseHover;
    private float _lastMouseX = float.NaN;
    private float _lastMouseY = float.NaN;

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

    /// <summary>Replace a menu option at the given index (e.g. to toggle Enabled/DisabledHint).</summary>
    public void SetOption(int index, MenuOption<T> option) => _options[index] = option;

    // ── Styling (set via init properties) ─────────────────────────
    public float ItemHeight { get; init; } = 50f;
    public float SelectedScale { get; init; } = 2.5f;
    public float NormalScale { get; init; } = 2f;
    public Color3 SelectedColor { get; init; } = new(220, 240, 255);
    public Color3 NormalColor { get; init; } = new(140, 140, 160);
    public Color3 DisabledColor { get; init; } = new(80, 80, 90);
    public Color3 DisabledHintColor { get; init; } = new(200, 80, 80);
    public float DisabledHintScale { get; init; } = 1.5f;
    public Color3 HighlightBg { get; init; } = new(40, 60, 120);
    public byte HighlightAlpha { get; init; } = 180;
    public bool CenterAlign { get; init; } = false;
    public float DescriptionScale { get; init; } = 1.5f;
    public Color3 DescriptionColor { get; init; } = new(160, 160, 180);

    // ── Constructor ───────────────────────────────────────────────

    public MenuWidget(MenuOption<T>[] options)
    {
        _options = options;
    }

    // ── Item geometry helper ──────────────────────────────────────

    /// <summary>
    /// Returns the screen-space bounds of menu item <paramref name="index"/>
    /// given the menu origin (<paramref name="menuX"/>, <paramref name="menuY"/>)
    /// and <paramref name="menuWidth"/>. Used by both hit-testing and rendering
    /// so the two can never drift apart.
    /// </summary>
    private Rect GetItemRect(int index, float menuX, float menuY, float menuWidth)
    {
        float y = menuY + index * ItemHeight;
        return new Rect(menuX, y, menuWidth, ItemHeight);
    }

    /// <summary>
    /// Returns the inset highlight rectangle for the given item (smaller than the
    /// full item rect by <c>HighlightPadding</c> on top and bottom).
    /// </summary>
    private const float HighlightPadding = 2f;
    private Rect GetHighlightRect(int index, float menuX, float menuY, float menuWidth)
    {
        var r = GetItemRect(index, menuX, menuY, menuWidth);
        return new Rect(r.X, r.Y - HighlightPadding, r.W, r.H + HighlightPadding * 2);
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

        float mx = input.MouseX;
        float my = input.MouseY;

        // Re-enable mouse hover when the physical mouse position changes
        if (_ignoreMouseHover)
        {
            if (mx != _lastMouseX || my != _lastMouseY)
                _ignoreMouseHover = false;
        }
        _lastMouseX = mx;
        _lastMouseY = my;

        // Mouse hover / click — uses the same GetItemRect as rendering
        if (!_ignoreMouseHover)
        {
            for (int i = 0; i < _options.Length; i++)
            {
                var r = GetItemRect(i, menuScreenX, menuScreenY, itemWidth);
                if (mx >= r.X && mx <= r.X + r.W &&
                    my >= r.Y && my <= r.Y + r.H)
                {
                    _selected = i;
                    if (input.IsMousePressed(1) && _options[i].Enabled)
                        return _options[i].Value;
                    break;
                }
            }
        }
        else if (input.IsMousePressed(1))
        {
            // Allow click even while ignoring hover — re-enable mouse and process click
            _ignoreMouseHover = false;
            for (int i = 0; i < _options.Length; i++)
            {
                var r = GetItemRect(i, menuScreenX, menuScreenY, itemWidth);
                if (mx >= r.X && mx <= r.X + r.W &&
                    my >= r.Y && my <= r.Y + r.H)
                {
                    _selected = i;
                    if (_options[i].Enabled)
                        return _options[i].Value;
                    break;
                }
            }
        }

        // Keyboard navigation — suppress mouse hover when used
        bool keyNav = false;
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
        {
            _selected = (_selected - 1 + _options.Length) % _options.Length;
            keyNav = true;
        }

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
        {
            _selected = (_selected + 1) % _options.Length;
            keyNav = true;
        }

        if (keyNav)
            _ignoreMouseHover = true;

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
    public void Render(SpriteRenderer renderer, float x, float y, float width, float panelBottom)
    {
        for (int i = 0; i < _options.Length; i++)
        {
            var itemRect = GetItemRect(i, x, y, width);
            bool sel = i == _selected;
            bool enabled = _options[i].Enabled;
            float scale = sel ? SelectedScale : NormalScale;
            var c = !enabled ? DisabledColor : sel ? SelectedColor : NormalColor;
            string label = _options[i].Label;

            // Vertically center text within the item rect
            float textH = 8 * scale;
            float textY = itemRect.Y + (itemRect.H - textH) / 2f;

            // Debug: draw item rect with border
            //renderer.DrawRectScreen(itemRect.X, itemRect.Y, itemRect.W, itemRect.H, new Color4(255, 0, 0, 100));
            //renderer.DrawRectScreen(itemRect.X + 2, itemRect.Y + 2, itemRect.W - 4, itemRect.H - 4, new Color4(255, 0, 0, 150));

            // Selection highlight (only for enabled items)
            if (sel && enabled)
            {
                var hr = GetHighlightRect(i, x, y, width);
                renderer.DrawRectScreen(hr.X, hr.Y, hr.W, hr.H, HighlightBg.WithAlpha(HighlightAlpha));
            }

            if (CenterAlign)
            {
                // Arrow indicator to the left of centered text
                if (sel)
                {
                    float textW = renderer.MeasureText(label, scale);
                    float textX = x + width / 2f - textW / 2f;
                    renderer.DrawTextScreen(textX - renderer.MeasureText("> ", scale), textY, ">", c, scale);
                    renderer.DrawTextScreen(textX, textY, label, c, scale);
                }
                else
                {
                    float textW = renderer.MeasureText(label, scale);
                    renderer.DrawTextScreen(x + width / 2f - textW / 2f, textY, label, c, scale);
                }
            }
            else
            {
                // Left-aligned with > prefix
                string displayLabel = sel ? $"> {label}" : label;
                float textX = sel ? x + 10 : x + 20;
                renderer.DrawTextScreen(textX, textY, displayLabel, c, scale);
            }

            // Disabled hint text (shown below the label within the same item area)
            if (!enabled && _options[i].DisabledHint != null)
            {
                string hint = _options[i].DisabledHint!;
                if (CenterAlign)
                {
                    float hintW = renderer.MeasureText(hint, DisabledHintScale);
                    renderer.DrawTextScreen(x + width / 2f - hintW / 2f, textY + textH + 4, hint,
                        DisabledHintColor, DisabledHintScale);
                }
                else
                {
                    renderer.DrawTextScreen(x + 20, textY + textH + 4, hint,
                        DisabledHintColor, DisabledHintScale);
                }
            }
        }

        // Description for selected item (below the list)
        string? description = _options[_selected].Description;
        if (description != null)
        {
            float descY = panelBottom + 10;
            if (CenterAlign)
            {
                float descW = renderer.MeasureText(description, DescriptionScale);
                renderer.DrawRectScreen(x + width / 2f - descW / 2f - 8, descY - 4, descW + 16, 22, new Color4(0, 0, 0, 160));
                renderer.DrawTextScreen(x + width / 2f - descW / 2f, descY, description, DescriptionColor, DescriptionScale);
            }
            else
            {
                renderer.DrawTextScreen(x + 20, descY, description, DescriptionColor, DescriptionScale);
            }
        }
    }

    /// <summary>Total pixel height of the menu items (excluding description).</summary>
    public float TotalHeight => _options.Length * ItemHeight;
}
