using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.ECS.Systems.Input;

/// <summary>
/// Reads an <see cref="InputSnapshot"/> and writes movement/shoot intent into <see cref="AvatarInputComponent"/>.
/// Reads <see cref="AvatarComponent.InVehicle"/> to suppress avatar input while driving.
/// <see cref="AvatarSystem"/> converts intents into physics and projectile spawning.
/// </summary>
public partial class PlayerAvatarInputSystem : BaseSystem<World, float>
{
    private readonly float _speed;
    public InputSnapshot LocalSnapshot;

    private Vector2 _lastMoveDir = new(0, -1);

    public PlayerAvatarInputSystem(World world, float speed) : base(world)
    {
        _speed = speed;
    }

    public override void Update(in float dt)
    {
        if (LocalSnapshot.MovementDirection != Vector2.Zero)
            _lastMoveDir = LocalSnapshot.MovementDirection;
        SetAvatarInputQuery(World);
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(PlayerLocal), typeof(AvatarInputComponent), typeof(AvatarComponent))]
    public void SetAvatarInput(ref AvatarInputComponent input, ref AvatarComponent avatar)
    {
        if (avatar.InVehicle)
        {
            input.DesiredVelocity = Vector2.Zero;
            input.Shoot = false;
            return;
        }

        input.DesiredVelocity = LocalSnapshot.MovementDirection * _speed;
        input.Shoot = LocalSnapshot.Shoot;

        if (input.Shoot)
        {
            if (LocalSnapshot.AimDirection != Vector2.Zero)
                input.AimDirection = LocalSnapshot.AimDirection;
            else
                input.AimDirection = _lastMoveDir;
        }
    }
}
