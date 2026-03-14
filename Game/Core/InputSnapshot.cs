using System.Numerics;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Platform-independent snapshot of player input intent for a single frame.
/// Produced by the client (from <see cref="Engine.Platform.IInputManager"/> + camera)
/// and consumed by ECS input systems. Can also be produced by the network layer
/// (for remote players) or by <see cref="AutoplayBot"/>.
/// </summary>
public struct InputSnapshot
{
    // ── Movement ──
    /// <summary>Normalized movement direction (WASD / stick). Zero when idle.</summary>
    public Vector2 MovementDirection;

    /// <summary>Normalized heading/facing direction (right stick / mouse). Zero when not aiming.</summary>
    public Vector2 HeadingDirection;

    /// <summary>Whether movement is absolute (twin-stick style) or heading-relative.</summary>
    public bool AbsoluteMovement;

    // ── Combat ──
    /// <summary>Whether the fire/shoot button is held.</summary>
    public bool Shoot;

    /// <summary>World-space aim direction for projectiles (computed client-side from mouse/gamepad).</summary>
    public Vector2 AimDirection;

    // ── Actions (edge-triggered, true on the frame the key was pressed) ──
    /// <summary>Interact key pressed (E — dock, land, board, talk).</summary>
    public bool Interact;

    /// <summary>Toggle map key pressed (M).</summary>
    public bool ToggleMap;

    /// <summary>Menu/back key pressed (Escape).</summary>
    public bool MenuBack;

    /// <summary>Dodge roll key pressed (Space — surface only).</summary>
    public bool DodgeRoll;

    // ── Camera (client-local, not sent over network) ──
    /// <summary>Mouse wheel delta for zoom. Zero when not scrolling.</summary>
    public float MouseWheelY;

    /// <summary>Returns a snapshot with all fields zeroed.</summary>
    public static InputSnapshot Zero => default;

    /// <summary>
    /// Captures the current frame's input from the platform input manager.
    /// The <paramref name="camera"/> is used to convert mouse position to world-space aim direction
    /// for avatar aiming. Pass the player entity position in <paramref name="playerWorldPos"/>
    /// when avatar aiming is needed (planet surface); pass <c>null</c> otherwise.
    /// </summary>
    public static InputSnapshot FromInput(Engine.Platform.IInputManager input, Camera? camera = null, Vector2? playerWorldPos = null)
    {
        var snapshot = new InputSnapshot
        {
            MovementDirection = input.GetActionAxisDirection(Engine.Platform.InputActionAxis.Movement),
            HeadingDirection = input.GetActionAxisDirection(Engine.Platform.InputActionAxis.Heading),
            AbsoluteMovement = input.MovementMode == Engine.Platform.MovementInputMode.Absolute,
            Shoot = input.IsActionDown(Engine.Platform.InputAction.FireWeapon),
            Interact = input.IsActionPressed(Engine.Platform.InputAction.Interact),
            ToggleMap = input.IsActionPressed(Engine.Platform.InputAction.ToggleMap),
            MenuBack = input.IsActionPressed(Engine.Platform.InputAction.MenuBack),
            DodgeRoll = input.IsActionPressed(Engine.Platform.InputAction.DodgeRoll),
            MouseWheelY = input.MouseWheelY,
        };

        // Compute world-space aim direction for avatar shooting
        if (snapshot.Shoot && camera != null && playerWorldPos.HasValue)
        {
            var gamepadHeading = input.ActiveInputMethod == Engine.Platform.InputMethod.Gamepad
                ? input.GetActionAxisDirection(Engine.Platform.InputActionAxis.Heading) : Vector2.Zero;

            if (gamepadHeading != Vector2.Zero)
            {
                snapshot.AimDirection = gamepadHeading;
            }
            else if (input.IsMouseDown(Engine.Platform.MouseButton.Left))
            {
                var mouseWorld = camera.ScreenToWorld(new Vector2(input.MouseX, input.MouseY));
                var dir = Vector2.Normalize(mouseWorld - playerWorldPos.Value);
                snapshot.AimDirection = float.IsNaN(dir.X) ? Vector2.Zero : dir;
            }
        }

        return snapshot;
    }
}
