using System.Numerics;

namespace Engine.Platform;

/// <summary>
/// Abstraction over input handling — keyboard, mouse, and gamepad.
/// </summary>
public interface IInputManager
{
    float MouseX { get; }
    float MouseY { get; }
    float MouseWheelY { get; }
    bool QuitRequested { get; }

    /// <summary>Text typed this frame. Reset each BeginFrame.</summary>
    string TextInput { get; }
    int TextInputBackspacesCount { get; }
    int TextInputReturnsCount { get; }
    InputMethod ActiveInputMethod { get; }
    MovementInputMode MovementMode { get; }

    /// <summary>Call at the start of each frame before processing events.</summary>
    void BeginFrame();

    /// <summary>Call after the fixed-timestep update loop has run at least once.</summary>
    void EndFrame();

    /// <summary>Full reset: clears ALL input state.</summary>
    void Reset();

    /// <summary>Poll and process platform events.</summary>
    void ProcessEvents();

    bool IsActionDown(InputAction action);
    bool IsActionPressed(InputAction action);
    bool IsActionReleased(InputAction action);

    Vector2 GetActionAxisDirection(InputActionAxis axis);

    string GetActionHelpText(InputAction action, bool includeSecondary = false);
    string GetActionHelpTextFull(InputAction action);
    string GetMouseButtonHelpText(MouseButton button);

    bool IsMouseDown(MouseButton button);
    bool IsMousePressed(MouseButton button);
    bool IsMouseReleased(MouseButton button);
}
