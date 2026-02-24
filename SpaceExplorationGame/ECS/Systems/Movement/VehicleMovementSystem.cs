using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles vehicle-style movement intent from the unified movement axis.
/// Physics integration is handled by VelocitySystem.
/// </summary>
public partial class VehicleMovementSystem : BaseSystem<World, float>
{
    private readonly InputManager _input;
    private readonly float _acceleration;
    private readonly float _maxSpeed;
    private readonly float _rotationSpeed;
    private readonly float _friction;
    private readonly float _brakeMultiplier;

    /// <summary>The entity being controlled.</summary>
    private readonly Entity _entity;

    public VehicleMovementSystem(World world, InputManager input, Entity entity,
        float acceleration = GameConfig.VehicleAcceleration,
        float maxSpeed = GameConfig.VehicleMaxSpeed,
        float rotationSpeed = GameConfig.VehicleRotationSpeed,
        float friction = GameConfig.VehicleFriction,
        float brakeMultiplier = GameConfig.VehicleBrakeMultiplier) : base(world)
    {
        _input = input;
        _entity = entity;
        _acceleration = acceleration;
        _maxSpeed = maxSpeed;
        _rotationSpeed = rotationSpeed;
        _friction = friction;
        _brakeMultiplier = brakeMultiplier;
    }

    public override void Update(in float dt)
    {
        ref var transform = ref World.Get<Transform>(_entity);
        ref var velocity = ref World.Get<Velocity>(_entity);
        Vector2 movementInput = _input.GetActionAxisDirection(InputActionAxis.Movement);
        Vector2 headingDirection = _input.GetActionAxisDirection(InputActionAxis.Heading);

        if (_input.MovementMode == MovementInputMode.Absolute &&
            headingDirection == Vector2.Zero &&
            movementInput != Vector2.Zero)
        {
            headingDirection = Vector2.Normalize(movementInput);
        }

        velocity.Acceleration = Vector2.Zero;
        velocity.RotationVelocity = 0f;

        if (headingDirection != Vector2.Zero && dt > 0f)
        {
            float targetRotation = MathF.Atan2(headingDirection.Y, headingDirection.X) * 180f / MathF.PI;
            float delta = targetRotation - transform.Rotation;
            delta = ((delta % 360f) + 540f) % 360f - 180f;

            float requiredRotationSpeed = delta / dt;
            velocity.RotationVelocity = Math.Clamp(requiredRotationSpeed, -_rotationSpeed, _rotationSpeed);
        }

        switch (_input.MovementMode)
        {
            case MovementInputMode.Absolute:
                velocity.Damping = _friction;
                velocity.Acceleration += movementInput * _acceleration;
                break;

            case MovementInputMode.HeadingRelative:
            default:
            {
                bool isBraking = movementInput.Y > 0f;
                velocity.Damping = _friction * (isBraking ? _brakeMultiplier : 1f);

                if (headingDirection != Vector2.Zero)
                {
                    Vector2 forward = headingDirection;
                    Vector2 right = new(-forward.Y, forward.X);

                    float forwardThrust = MathF.Max(0f, -movementInput.Y);
                    float strafeThrust = movementInput.X;

                    Vector2 localAcceleration = (forward * forwardThrust) + (right * strafeThrust);
                    velocity.Acceleration += localAcceleration * _acceleration;
                }
                break;
            }
        }

        // Damping/braking are handled centrally by VelocitySystem.
    }
}
