using System.Numerics;
using SDL3;

namespace SpaceExplorationGame.Core;

public enum InputAction
{
    MenuConfirm,
    MenuUp,
    MenuDown,
    MenuLeft,
    MenuRight,
    MenuBack,
    MenuSecondaryAction,

    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    FireWeapon,
    Interact,
    ToggleMap,
    ToggleNavTarget,
}

public enum InputActionAxis
{
    Movement,
}

/// <summary>
/// Input snapshot captured each frame. Provides current and previous state for edge detection.
/// </summary>
public class InputManager
{
    private readonly record struct InputBinding(SDL.Scancode? Scancode, int? MouseButton)
    {
        public static InputBinding Key(SDL.Scancode scancode) => new(scancode, null);
        public static InputBinding Mouse(int button) => new(null, button);
    }

    private readonly HashSet<SDL.Scancode> _keysDown = [];
    private readonly HashSet<SDL.Scancode> _keysPressed = [];  // just pressed this frame
    private readonly HashSet<SDL.Scancode> _keysReleased = []; // just released this frame

    private readonly HashSet<int> _mouseDown = [];
    private readonly HashSet<int> _mousePressed = [];
    private readonly HashSet<int> _mouseReleased = [];

    private readonly Dictionary<InputAction, List<InputBinding>> _bindings = new()
    {
        [InputAction.MenuConfirm] = [InputBinding.Key(SDL.Scancode.Return), InputBinding.Key(SDL.Scancode.Space)],
        [InputAction.MenuUp] = [InputBinding.Key(SDL.Scancode.Up), InputBinding.Key(SDL.Scancode.W)],
        [InputAction.MenuDown] = [InputBinding.Key(SDL.Scancode.Down), InputBinding.Key(SDL.Scancode.S)],
        [InputAction.MenuLeft] = [InputBinding.Key(SDL.Scancode.Left), InputBinding.Key(SDL.Scancode.A)],
        [InputAction.MenuRight] = [InputBinding.Key(SDL.Scancode.Right), InputBinding.Key(SDL.Scancode.D)],
        [InputAction.MenuBack] = [InputBinding.Key(SDL.Scancode.Escape), InputBinding.Key(SDL.Scancode.Backspace)],
        [InputAction.MenuSecondaryAction] = [InputBinding.Key(SDL.Scancode.X), InputBinding.Key(SDL.Scancode.Delete)],

        [InputAction.MoveUp] = [InputBinding.Key(SDL.Scancode.W), InputBinding.Key(SDL.Scancode.Up)],
        [InputAction.MoveDown] = [InputBinding.Key(SDL.Scancode.S), InputBinding.Key(SDL.Scancode.Down)],
        [InputAction.MoveLeft] = [InputBinding.Key(SDL.Scancode.A), InputBinding.Key(SDL.Scancode.Left)],
        [InputAction.MoveRight] = [InputBinding.Key(SDL.Scancode.D), InputBinding.Key(SDL.Scancode.Right)],
        [InputAction.FireWeapon] = [InputBinding.Key(SDL.Scancode.Space), InputBinding.Mouse(SDL.ButtonLeft)],
        [InputAction.Interact] = [InputBinding.Key(SDL.Scancode.E)],
        [InputAction.ToggleMap] = [InputBinding.Key(SDL.Scancode.M)],
        [InputAction.ToggleNavTarget] = [InputBinding.Key(SDL.Scancode.T), InputBinding.Key(SDL.Scancode.Return)],
    };

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public float MouseWheelY { get; private set; }
    public bool QuitRequested { get; private set; }

    /// <summary>Call at the start of each frame before processing events.</summary>
    public void BeginFrame()
    {
        // Only poll mouse position here.
        // Edge-detection sets (pressed/released) are NOT cleared here —
        // they persist until EndFrame() so that fixed-timestep updates always see them.
        SDL.GetMouseState(out float mx, out float my);
        MouseX = mx;
        MouseY = my;
    }

    /// <summary>
    /// Call after the fixed-timestep update loop has run at least once.
    /// Clears edge-detection state so the next frame starts fresh.
    /// </summary>
    public void EndFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        MouseWheelY = 0;
    }

    /// <summary>
    /// Full reset: clears ALL input state (pressed, released, down, wheel).
    /// Use on state transitions so the new state starts with a completely clean slate.
    /// </summary>
    public void Reset()
    {
        _keysDown.Clear();
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mouseDown.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        MouseWheelY = 0;
    }

    /// <summary>Feed an SDL event into the input manager.</summary>
    public void ProcessEvent(SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.Quit:
                QuitRequested = true;
                break;

            case SDL.EventType.KeyDown:
                if (!e.Key.Repeat)
                {
                    _keysDown.Add(e.Key.Scancode);
                    _keysPressed.Add(e.Key.Scancode);
                }
                break;

            case SDL.EventType.KeyUp:
                _keysDown.Remove(e.Key.Scancode);
                _keysReleased.Add(e.Key.Scancode);
                break;

            case SDL.EventType.MouseMotion:
                MouseX = e.Motion.X;
                MouseY = e.Motion.Y;
                break;

            case SDL.EventType.MouseButtonDown:
                _mouseDown.Add(e.Button.Button);
                _mousePressed.Add(e.Button.Button);
                MouseX = e.Button.X;
                MouseY = e.Button.Y;
                break;

            case SDL.EventType.MouseButtonUp:
                _mouseDown.Remove(e.Button.Button);
                _mouseReleased.Add(e.Button.Button);
                MouseX = e.Button.X;
                MouseY = e.Button.Y;
                break;

            case SDL.EventType.MouseWheel:
                MouseWheelY += e.Wheel.Y;  // accumulate across frames
                break;
        }
    }

    // Key queries
    public bool IsKeyDown(SDL.Scancode key) => _keysDown.Contains(key);
    public bool IsKeyPressed(SDL.Scancode key) => _keysPressed.Contains(key);
    public bool IsKeyReleased(SDL.Scancode key) => _keysReleased.Contains(key);

    public bool IsActionDown(InputAction action) => IsAnyBindingActive(action, _keysDown, _mouseDown);
    public bool IsActionPressed(InputAction action) => IsAnyBindingActive(action, _keysPressed, _mousePressed);
    public bool IsActionReleased(InputAction action) => IsAnyBindingActive(action, _keysReleased, _mouseReleased);

    public Vector2 GetActionAxisDirection(InputActionAxis axis)
    {
        return axis switch
        {
            InputActionAxis.Movement => GetDirectionFromActions(InputAction.MoveUp, InputAction.MoveDown, InputAction.MoveLeft, InputAction.MoveRight),
            _ => Vector2.Zero,
        };
    }

    public string GetActionHelpText(InputAction action)
    {
        if (!_bindings.TryGetValue(action, out List<InputBinding>? bindingList) || bindingList.Count == 0)
            return string.Empty;

        List<string> labels = [];
        foreach (InputBinding binding in bindingList)
        {
            string label = GetBindingLabel(binding);
            if (!string.IsNullOrWhiteSpace(label) && !labels.Contains(label))
                labels.Add(label);
        }

        return string.Join("/", labels);
    }

    public string GetKeyHelpText(SDL.Scancode scancode) => GetBindingLabel(InputBinding.Key(scancode));

    public string GetMouseButtonHelpText(int button) => GetBindingLabel(InputBinding.Mouse(button));

    // Mouse queries (SDL.ButtonLeft=1, SDL.ButtonMiddle=2, SDL.ButtonRight=3)
    public bool IsMouseDown(int button) => _mouseDown.Contains(button);
    public bool IsMousePressed(int button) => _mousePressed.Contains(button);
    public bool IsMouseReleased(int button) => _mouseReleased.Contains(button);

    private bool IsAnyBindingActive(InputAction action, HashSet<SDL.Scancode> keySet, HashSet<int> mouseSet)
    {
        if (!_bindings.TryGetValue(action, out List<InputBinding>? bindingList))
            return false;

        foreach (InputBinding binding in bindingList)
        {
            if (binding.Scancode.HasValue && keySet.Contains(binding.Scancode.Value))
                return true;

            if (binding.MouseButton.HasValue && mouseSet.Contains(binding.MouseButton.Value))
                return true;
        }

        return false;
    }

    private Vector2 GetDirectionFromActions(InputAction upAction, InputAction downAction, InputAction leftAction, InputAction rightAction)
    {
        Vector2 direction = Vector2.Zero;
        if (IsActionDown(upAction)) direction.Y -= 1f;
        if (IsActionDown(downAction)) direction.Y += 1f;
        if (IsActionDown(leftAction)) direction.X -= 1f;
        if (IsActionDown(rightAction)) direction.X += 1f;
        return direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(direction);
    }

    private static string GetBindingLabel(InputBinding binding)
    {
        if (binding.Scancode.HasValue)
        {
            return binding.Scancode.Value switch
            {
                SDL.Scancode.Return => "Enter",
                SDL.Scancode.Space => "Space",
                SDL.Scancode.Escape => "Esc",
                SDL.Scancode.Backspace => "Backspace",
                SDL.Scancode.Up => "Up",
                SDL.Scancode.Down => "Down",
                SDL.Scancode.Left => "Left",
                SDL.Scancode.Right => "Right",
                _ => binding.Scancode.Value.ToString(),
            };
        }

        if (binding.MouseButton.HasValue)
        {
            return binding.MouseButton.Value switch
            {
                SDL.ButtonLeft => "LMB",
                SDL.ButtonRight => "RMB",
                SDL.ButtonMiddle => "MMB",
                _ => $"Mouse{binding.MouseButton.Value}",
            };
        }

        return string.Empty;
    }
}
