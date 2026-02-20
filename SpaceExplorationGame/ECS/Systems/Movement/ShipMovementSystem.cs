using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles spaceship movement using heading-relative controls.
/// Heading comes from input (mouse direction), ship rotation always points to heading,
/// and movement input is interpreted in local heading space:
/// +Y forward thrust, -Y braking, -X left strafe, +X right strafe.
/// </summary>
public partial class ShipMovementSystem : BaseSystem<World, float>
{
    private readonly InputManager _input;
    private readonly Entity _entity;

    // Configurable physics params (updated each frame from ship stats)
    public float MaxSpeed { get; set; } = 0;
    public float RotationSpeed { get; set; } = 0;
    public float Acceleration { get; set; } = 0;
    public float BrakeMultiplier { get; set; } = 0.95f;

    public ShipMovementSystem(World world, InputManager input, Entity entity) : base(world)
    {
        _input = input;
        _entity = entity;
    }

    public override void Update(in float dt)
    {
        ref var transform = ref World.Get<Transform>(_entity);
        ref var velocity = ref World.Get<Velocity>(_entity);

        // Clear per-frame intent
        velocity.Acceleration = Vector2.Zero;
        velocity.RotationVelocity = 0f;

        Vector2 headingDirection = _input.GetActionAxisDirection(InputActionAxis.Heading);
        if (headingDirection != Vector2.Zero)
        {
            float targetRotation = MathF.Atan2(headingDirection.Y, headingDirection.X) * 180f / MathF.PI;
            float delta = targetRotation - transform.Rotation;
            delta = ((delta + 540f) % 360f) - 180f;

            if (dt > 0f)
            {
                float requiredRotationSpeed = delta / dt;
                velocity.RotationVelocity = Math.Clamp(requiredRotationSpeed, -RotationSpeed, RotationSpeed);
            }
        }

        Vector2 movementInput = _input.GetActionAxisDirection(InputActionAxis.Movement);
        bool isBraking = movementInput.Y > 0f;
        velocity.Damping = isBraking ? BrakeMultiplier : 1f;

        if (headingDirection != Vector2.Zero)
        {
            Vector2 forward = headingDirection;
            Vector2 right = new(-forward.Y, forward.X);

            float forwardThrust = MathF.Max(0f, -movementInput.Y);
            float strafeThrust = movementInput.X;

            Vector2 localAcceleration = (forward * forwardThrust) + (right * strafeThrust);
            velocity.Acceleration += localAcceleration * Acceleration;
        }
    }
}
