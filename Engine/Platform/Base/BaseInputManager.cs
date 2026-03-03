using System.Numerics;

namespace Engine.Platform.Base;

/// <summary>
/// Shared base for SDL and Web input managers.
/// Handles mouse state, analog stick values, movement/heading helpers,
/// and common property implementations. Platform-specific key/event
/// processing and action bindings remain in each subclass.
/// </summary>
public abstract class BaseInputManager : IInputManager
{
    private readonly Func<(int Width, int Height)> _getWindowSize;

    protected const float GamepadDeadZone = 0.20f;

    // ── Mouse state ──────────────────────────────────────────────────
    protected readonly HashSet<int> _mouseDown = [];
    protected readonly HashSet<int> _mousePressed = [];
    protected readonly HashSet<int> _mouseReleased = [];

    // ── Analog stick values ──────────────────────────────────────────
    protected float _leftStickX, _leftStickY;
    protected float _rightStickX, _rightStickY;

    // ── Text input counters ──────────────────────────────────────────
    protected int _textInputBackspaceCount;
    protected int _textInputReturnCount;

    // ── IInputManager properties ─────────────────────────────────────
    public float MouseX { get; protected set; }
    public float MouseY { get; protected set; }
    public float MouseWheelY { get; protected set; }

    public abstract bool QuitRequested { get; }
    public abstract string TextInput { get; }
    public int TextInputBackspacesCount => _textInputBackspaceCount;
    public int TextInputReturnsCount => _textInputReturnCount;

    public InputMethod ActiveInputMethod { get; protected set; } = InputMethod.MouseKeyboard;
    public MovementInputMode MovementMode =>
        ActiveInputMethod == InputMethod.Gamepad ? MovementInputMode.Absolute : MovementInputMode.HeadingRelative;

    protected BaseInputManager(Func<(int Width, int Height)> getWindowSize)
    {
        _getWindowSize = getWindowSize;
    }

    // ── Frame lifecycle ──────────────────────────────────────────────

    public abstract void BeginFrame();
    public abstract void ProcessEvents();

    public virtual void EndFrame()
    {
        _mousePressed.Clear();
        _mouseReleased.Clear();
        MouseWheelY = 0;
    }

    public virtual void Reset()
    {
        _mouseDown.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        _leftStickX = _leftStickY = 0;
        _rightStickX = _rightStickY = 0;
        MouseWheelY = 0;
        ActiveInputMethod = InputMethod.MouseKeyboard;
    }

    // ── Action queries ───────────────────────────────────────────────

    public abstract bool IsActionDown(InputAction action);
    public abstract bool IsActionPressed(InputAction action);
    public abstract bool IsActionReleased(InputAction action);

    public Vector2 GetActionAxisDirection(InputActionAxis axis)
    {
        return axis switch
        {
            InputActionAxis.Movement => GetCombinedMovementDirection(),
            InputActionAxis.Heading => GetCombinedHeadingDirection(),
            _ => Vector2.Zero,
        };
    }

    public abstract string GetActionHelpText(InputAction action, bool includeSecondary = false);

    public string GetActionHelpTextFull(InputAction action) =>
        GetActionHelpText(action, includeSecondary: true);

    public abstract string GetMouseButtonHelpText(MouseButton button);

    // ── Mouse queries (virtual — Web uses 0-indexed buttons) ─────────

    public virtual bool IsMouseDown(MouseButton button) => _mouseDown.Contains((int)button);
    public virtual bool IsMousePressed(MouseButton button) => _mousePressed.Contains((int)button);
    public virtual bool IsMouseReleased(MouseButton button) => _mouseReleased.Contains((int)button);

    // ── Movement / heading helpers ───────────────────────────────────

    protected Vector2 GetCombinedMovementDirection() =>
        ActiveInputMethod == InputMethod.Gamepad
            ? GetLeftStickDirection()
            : GetKeyboardMovementDirection();

    protected Vector2 GetCombinedHeadingDirection() =>
        ActiveInputMethod == InputMethod.Gamepad
            ? GetRightStickDirection()
            : GetMouseHeadingDirection();

    protected Vector2 GetLeftStickDirection()
    {
        Vector2 dir = new(_leftStickX, _leftStickY);
        return dir.LengthSquared() < 0.001f ? Vector2.Zero : Vector2.Normalize(dir);
    }

    protected Vector2 GetRightStickDirection()
    {
        Vector2 dir = new(_rightStickX, _rightStickY);
        return dir.LengthSquared() < 0.001f ? Vector2.Zero : Vector2.Normalize(dir);
    }

    protected Vector2 GetKeyboardMovementDirection()
    {
        Vector2 dir = Vector2.Zero;
        if (IsActionDown(InputAction.MoveUp)) dir.Y -= 1f;
        if (IsActionDown(InputAction.MoveDown)) dir.Y += 1f;
        if (IsActionDown(InputAction.MoveLeft)) dir.X -= 1f;
        if (IsActionDown(InputAction.MoveRight)) dir.X += 1f;
        return dir == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(dir);
    }

    protected Vector2 GetMouseHeadingDirection()
    {
        var (winW, winH) = _getWindowSize();
        Vector2 screenCenter = new(winW / 2f, winH / 2f);
        Vector2 mousePosition = new(MouseX, MouseY);
        Vector2 direction = mousePosition - screenCenter;
        return direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(direction);
    }

    protected static float ApplyDeadZone(float value) =>
        MathF.Abs(value) < GamepadDeadZone ? 0f : value;
}
