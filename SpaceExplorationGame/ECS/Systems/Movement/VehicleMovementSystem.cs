using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles vehicle-style movement intent: rotation with A/D, acceleration with W, braking with S.
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

        velocity.Acceleration = Vector2.Zero;
        velocity.RotationVelocity = 0f;
        bool isBraking = _input.IsActionDown(InputAction.MoveDown);
        velocity.Damping = _friction * (isBraking ? _brakeMultiplier : 1f);

        // Rotation intent
        if (_input.IsActionDown(InputAction.MoveLeft))
            velocity.RotationVelocity -= _rotationSpeed;
        if (_input.IsActionDown(InputAction.MoveRight))
            velocity.RotationVelocity += _rotationSpeed;

        // Forward acceleration intent
        float rad = transform.Rotation * MathF.PI / 180f;
        var forward = new Vector2(MathF.Cos(rad), MathF.Sin(rad));

        if (_input.IsActionDown(InputAction.MoveUp))
        {
            velocity.Acceleration += forward * _acceleration;
        }

        // Damping/braking are handled centrally by VelocitySystem.
    }
}
