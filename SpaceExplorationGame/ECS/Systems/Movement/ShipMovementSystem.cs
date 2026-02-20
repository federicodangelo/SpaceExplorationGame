using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles spaceship movement using either heading-relative or absolute controls.
/// Mouse/keyboard uses heading-relative movement and mouse heading.
/// Gamepad uses absolute movement (left stick = world acceleration) and stick heading.
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
        Vector2 movementInput = _input.GetActionAxisDirection(InputActionAxis.Movement);

        if (_input.MovementMode == MovementInputMode.Absolute &&
            headingDirection == Vector2.Zero &&
            movementInput != Vector2.Zero)
        {
            headingDirection = Vector2.Normalize(movementInput);
        }

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
        Vector2 desiredMovementDirection = Vector2.Zero;

        switch (_input.MovementMode)
        {
            case MovementInputMode.Absolute:
                velocity.Damping = 1f;
                desiredMovementDirection = movementInput;

                if (desiredMovementDirection != Vector2.Zero)
                {
                    ApplyDirectionalBrake(
                        ref velocity,
                        desiredMovementDirection,
                        Acceleration,
                        minSpeedForBrake: 50f,
                        misalignmentThreshold: 0.25f,
                        maxBrakeMultiplier: 1.2f);
                }

                velocity.Acceleration += movementInput * Acceleration;
                break;

            case MovementInputMode.HeadingRelative:
            default:
            {
                bool isBraking = movementInput.Y > 0f;
                velocity.Damping = isBraking ? BrakeMultiplier : 1f;

                if (headingDirection != Vector2.Zero)
                {
                    Vector2 forward = headingDirection;
                    Vector2 right = new(-forward.Y, forward.X);

                    float forwardThrust = MathF.Max(0f, -movementInput.Y);
                    float strafeThrust = movementInput.X;

                    Vector2 localAcceleration = (forward * forwardThrust) + (right * strafeThrust);
                    desiredMovementDirection = localAcceleration;

                    if (desiredMovementDirection != Vector2.Zero)
                    {
                        ApplyDirectionalBrake(
                            ref velocity,
                            desiredMovementDirection,
                            Acceleration,
                            minSpeedForBrake: 50f,
                            misalignmentThreshold: 0.25f,
                            maxBrakeMultiplier: 1.2f);
                    }

                    velocity.Acceleration += localAcceleration * Acceleration;
                }
                break;
            }
        }
    }

    private static void ApplyDirectionalBrake(ref Velocity velocity, Vector2 desiredDirection,
        float baseAcceleration, float minSpeedForBrake, float misalignmentThreshold, float maxBrakeMultiplier)
    {
        if (desiredDirection == Vector2.Zero)
            return;

        desiredDirection = Vector2.Normalize(desiredDirection);

        float speed = velocity.Velocity.Length();
        if (speed < minSpeedForBrake)
            return;

        var moveDir = velocity.Velocity / speed;
        float alignment = Vector2.Dot(moveDir, desiredDirection);
        if (alignment >= misalignmentThreshold)
            return;

        float t = (misalignmentThreshold - alignment) / (misalignmentThreshold + 1f);
        float brakeMultiplier = 0.5f + t * (maxBrakeMultiplier - 0.5f);
        velocity.Acceleration -= moveDir * baseAcceleration * brakeMultiplier;
    }
}
