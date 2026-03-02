using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;
using Engine.Platform;

namespace SpaceExplorationGame.ECS.Systems.Input;

/// <summary>
/// Reads player controls and writes per-frame intent into <see cref="ShipInputComponent"/>.
/// </summary>
public partial class PlayerShipInputSystem : BaseSystem<World, float>
{
    private readonly IInputManager _input;

    public PlayerShipInputSystem(World world, IInputManager input) : base(world)
    {
        _input = input;
    }

    public override void Update(in float dt)
    {
        UpdatePlayerShipInputQuery(World, dt);
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(Transform), typeof(Velocity), typeof(ShipInputComponent), typeof(ShipComponent))]
    public void UpdatePlayerShipInput(ref Transform transform, ref Velocity velocity,
        ref ShipInputComponent shipInput, ref ShipComponent ship, [Data] in float dt)
    {
        shipInput.AccelerationDirection = Vector2.Zero;
        shipInput.RotationSpeed = 0f;
        shipInput.Shoot = _input.IsActionDown(InputAction.FireWeapon);

        Vector2 headingDirection = _input.GetActionAxisDirection(InputActionAxis.Heading);
        Vector2 movementInput = _input.GetActionAxisDirection(InputActionAxis.Movement);

        bool absolute = _input.MovementMode == MovementInputMode.Absolute;
        if (absolute && headingDirection == Vector2.Zero && movementInput != Vector2.Zero)
            headingDirection = Vector2.Normalize(movementInput);

        if (headingDirection != Vector2.Zero && dt > 0f)
        {
            float targetRotation = MathF.Atan2(headingDirection.Y, headingDirection.X) * 180f / MathF.PI;
            float delta = targetRotation - transform.Rotation;
            delta = ((delta % 360f) + 540f) % 360f - 180f;
            float requiredRotationSpeed = delta / dt;
            shipInput.RotationSpeed = Math.Clamp(requiredRotationSpeed, -ship.MaxRotationSpeed, ship.MaxRotationSpeed);
        }

        if (absolute)
        {
            shipInput.AccelerationDirection = movementInput;
        }
        else
        {
            if (headingDirection == Vector2.Zero)
            {
                float headingRad = transform.Rotation * MathF.PI / 180f;
                headingDirection = new Vector2(MathF.Cos(headingRad), MathF.Sin(headingRad));
            }

            Vector2 forward = headingDirection;
            Vector2 right = new(-forward.Y, forward.X);

            float forwardThrust = MathF.Max(0f, -movementInput.Y);
            float strafeThrust = movementInput.X;
            Vector2 desiredAcceleration = (forward * forwardThrust) + (right * strafeThrust);

            if (movementInput.Y > 0f && velocity.Linear != Vector2.Zero)
            {
                Vector2 brakeDirection = -Vector2.Normalize(velocity.Linear);
                desiredAcceleration += brakeDirection * movementInput.Y * ship.BrakeMultiplier;
            }

            shipInput.AccelerationDirection = desiredAcceleration;
        }
    }
}
