using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.ECS.Systems.Input;

/// <summary>
/// Handles top-down 4-way WASD/arrow movement intent for the player-controlled entity.
/// Physics integration is handled by VelocitySystem.
/// </summary>
public partial class PlayerAvatarInputSystem : BaseSystem<World, float>
{
    private readonly IInputManager _input;
    private readonly float _speed;

    public PlayerAvatarInputSystem(World world, IInputManager input, float speed) : base(world)
    {
        _input = input;
        _speed = speed;
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(Transform), typeof(Velocity))]
    public void MovePlayer(ref Velocity velocity)
    {
        Vector2 moveDir = _input.GetActionAxisDirection(InputActionAxis.Movement);

        // Critically damped response toward desired walk velocity.
        var targetVelocity = moveDir * _speed;
        velocity.Acceleration = (targetVelocity - velocity.Linear) * 18f;
        velocity.RotationVelocity = 0f;
    }
}
