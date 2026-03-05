using System.Numerics;

namespace Engine.Platform.Null;

/// <summary>
/// No-op input manager for headless/server use. Returns neutral state for all queries.
/// </summary>
public sealed class NullInputManager : IInputManager
{
    public float MouseX => 0;
    public float MouseY => 0;
    public float MouseWheelY => 0;
    public bool QuitRequested => false;
    public string TextInput => string.Empty;
    public int TextInputBackspacesCount => 0;
    public int TextInputReturnsCount => 0;
    public InputMethod ActiveInputMethod => InputMethod.MouseKeyboard;
    public MovementInputMode MovementMode => MovementInputMode.HeadingRelative;

    public void BeginFrame() { }
    public void EndFrame() { }
    public void Reset() { }
    public void ProcessEvents() { }

    public bool IsActionDown(InputAction action) => false;
    public bool IsActionPressed(InputAction action) => false;
    public bool IsActionReleased(InputAction action) => false;
    public Vector2 GetActionAxisDirection(InputActionAxis axis) => Vector2.Zero;

    public string GetActionHelpText(InputAction action, bool includeSecondary = false) => "";
    public string GetActionHelpTextFull(InputAction action) => "";
    public string GetMouseButtonHelpText(MouseButton button) => "";

    public bool IsMouseDown(MouseButton button) => false;
    public bool IsMousePressed(MouseButton button) => false;
    public bool IsMouseReleased(MouseButton button) => false;
}
