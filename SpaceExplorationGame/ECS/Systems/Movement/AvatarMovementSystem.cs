using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Handles top-down 4-way WASD/arrow movement intent for the player-controlled entity.
/// Physics integration is handled by VelocitySystem.
/// </summary>
public partial class AvatarMovementSystem : BaseSystem<World, float>
{
    private readonly InputManager _input;
    private readonly float _speed;

    public AvatarMovementSystem(World world, InputManager input, float speed) : base(world)
    {
        _input = input;
        _speed = speed;
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(Transform), typeof(Velocity))]
    public void MovePlayer(ref Transform transform, ref Velocity velocity)
    {
        Vector2 moveDir = _input.GetActionAxisDirection(InputActionAxis.Movement);

        // Critically damped response toward desired walk velocity.
        var targetVelocity = moveDir * _speed;
        velocity.Acceleration = (targetVelocity - velocity.Velocity) * 18f;
        velocity.RotationVelocity = 0f;
    }
}
