using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles spaceship movement: rotation with A/D, thrust with W along facing direction,
/// braking with S, and friction. Similar to VehicleMovementSystem but for space flight
/// (velocity-based drift, no collision delegate).
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
        bool isBraking = _input.IsActionDown(InputAction.MoveDown);
        velocity.Damping = isBraking ? BrakeMultiplier : 1f;

        // Rotation intent
        if (_input.IsActionDown(InputAction.MoveLeft))
            velocity.RotationVelocity -= RotationSpeed;
        if (_input.IsActionDown(InputAction.MoveRight))
            velocity.RotationVelocity += RotationSpeed;

        // Thrust intent along facing direction
        if (_input.IsActionDown(InputAction.MoveUp))
        {
            float rad = transform.Rotation * MathF.PI / 180f;
            velocity.Acceleration += new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * Acceleration;
        }

        // Braking is handled by VelocitySystem via Damping.
    }
}
