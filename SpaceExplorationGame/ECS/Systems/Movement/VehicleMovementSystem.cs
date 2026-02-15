using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles vehicle-style movement: rotation with A/D, acceleration with W, braking with S.
/// The vehicle always moves forward in its facing direction (no drift).
/// Uses a pluggable collision delegate like PlayerMovementSystem.
/// </summary>
public partial class VehicleMovementSystem : BaseSystem<World, float>
{
    private readonly InputManager _input;
    private readonly float _acceleration;
    private readonly float _maxSpeed;
    private readonly float _rotationSpeed;
    private readonly float _friction;
    private readonly float _brakeMultiplier;

    /// <summary>Current scalar speed along the facing direction.</summary>
    public float Speed { get; set; }

    /// <summary>
    /// Optional collision check: receives the proposed new position and returns true if movement is allowed.
    /// If null, all movement is allowed.
    /// </summary>
    public Func<Vector2, bool>? CanMoveTo { get; set; }

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
        ref var transform = ref World.Get<ECS.Components.Transform>(_entity);

        // Rotation
        if (_input.IsKeyDown(SDL3.SDL.Scancode.A) || _input.IsKeyDown(SDL3.SDL.Scancode.Left))
            transform.Rotation -= _rotationSpeed * dt;
        if (_input.IsKeyDown(SDL3.SDL.Scancode.D) || _input.IsKeyDown(SDL3.SDL.Scancode.Right))
            transform.Rotation += _rotationSpeed * dt;

        // Accelerate / brake
        if (_input.IsKeyDown(SDL3.SDL.Scancode.W) || _input.IsKeyDown(SDL3.SDL.Scancode.Up))
        {
            Speed += _acceleration * dt;
        }
        else if (_input.IsKeyDown(SDL3.SDL.Scancode.S) || _input.IsKeyDown(SDL3.SDL.Scancode.Down))
        {
            Speed *= _brakeMultiplier;
        }

        // Friction
        Speed *= _friction;

        // Clamp
        Speed = Math.Clamp(Speed, 0f, _maxSpeed);

        // Kill tiny residual speed
        if (Speed < 1f) Speed = 0f;

        // Build velocity along facing direction
        float rad = transform.Rotation * MathF.PI / 180f;
        var forward = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        var velocity = forward * Speed;

        // Apply with collision check
        var newPos = transform.Position + velocity * dt;
        if (CanMoveTo == null || CanMoveTo(newPos))
        {
            transform.Position = newPos;
        }
        else
        {
            Speed = 0f;
        }
    }
}
