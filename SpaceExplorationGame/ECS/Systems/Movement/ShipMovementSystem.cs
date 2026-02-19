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

        // Update max speed
        velocity.MaxSpeed = MaxSpeed;

        // Rotation
        if (_input.IsKeyDown(SDL3.SDL.Scancode.A) || _input.IsKeyDown(SDL3.SDL.Scancode.Left))
            transform.Rotation -= RotationSpeed * dt;
        if (_input.IsKeyDown(SDL3.SDL.Scancode.D) || _input.IsKeyDown(SDL3.SDL.Scancode.Right))
            transform.Rotation += RotationSpeed * dt;

        // Thrust along facing direction
        if (_input.IsKeyDown(SDL3.SDL.Scancode.W) || _input.IsKeyDown(SDL3.SDL.Scancode.Up))
        {
            float rad = transform.Rotation * MathF.PI / 180f;
            var thrust = new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * Acceleration * dt;
            velocity.Value += thrust;
        }

        // Brake
        if (_input.IsKeyDown(SDL3.SDL.Scancode.S) || _input.IsKeyDown(SDL3.SDL.Scancode.Down))
        {
            velocity.Value *= BrakeMultiplier;
        }
    }
}
