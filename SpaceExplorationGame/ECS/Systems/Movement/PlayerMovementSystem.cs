using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles top-down 4-way WASD/arrow movement for the player-controlled entity.
/// Reads input, computes normalized direction, applies speed, and checks collision
/// via a pluggable delegate before committing the position change.
/// </summary>
public partial class PlayerMovementSystem : BaseSystem<World, float>
{
    private readonly InputManager _input;
    private readonly float _speed;

    /// <summary>
    /// Optional collision check: receives the proposed new position and returns true if movement is allowed.
    /// If null, all movement is allowed.
    /// </summary>
    public Func<Vector2, bool>? CanMoveTo { get; set; }

    public PlayerMovementSystem(World world, InputManager input, float speed) : base(world)
    {
        _input = input;
        _speed = speed;
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(Transform))]
    public void MovePlayer(ref Transform transform, [Data] float dt)
    {
        Vector2 moveDir = Vector2.Zero;

        if (_input.IsKeyDown(SDL3.SDL.Scancode.W) || _input.IsKeyDown(SDL3.SDL.Scancode.Up))
            moveDir.Y -= 1;
        if (_input.IsKeyDown(SDL3.SDL.Scancode.S) || _input.IsKeyDown(SDL3.SDL.Scancode.Down))
            moveDir.Y += 1;
        if (_input.IsKeyDown(SDL3.SDL.Scancode.A) || _input.IsKeyDown(SDL3.SDL.Scancode.Left))
            moveDir.X -= 1;
        if (_input.IsKeyDown(SDL3.SDL.Scancode.D) || _input.IsKeyDown(SDL3.SDL.Scancode.Right))
            moveDir.X += 1;

        if (moveDir == Vector2.Zero)
            return;

        moveDir = Vector2.Normalize(moveDir);
        var newPos = transform.Position + moveDir * _speed * dt;

        if (CanMoveTo == null || CanMoveTo(newPos))
        {
            transform.Position = newPos;
        }
    }
}
