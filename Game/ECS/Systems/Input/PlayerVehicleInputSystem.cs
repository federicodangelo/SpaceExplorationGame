using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.ECS.Systems.Input;

/// <summary>
/// Reads an <see cref="InputSnapshot"/> and writes vehicle intent into <see cref="AvatarInputComponent"/>.
/// The actual physics (rotation, thrust, braking, damping) is applied by <see cref="VehicleSystem"/>.
/// </summary>
public partial class PlayerVehicleInputSystem : BaseSystem<World, float>
{
    private readonly Entity _entity;
    public InputSnapshot LocalSnapshot;

    public PlayerVehicleInputSystem(World world, Entity entity) : base(world)
    {
        _entity = entity;
    }

    public override void Update(in float dt)
    {
        ref var avatarInput = ref World.Get<AvatarInputComponent>(_entity);

        Vector2 movementInput = LocalSnapshot.MovementDirection;
        Vector2 headingDirection = LocalSnapshot.HeadingDirection;

        if (LocalSnapshot.AbsoluteMovement &&
            headingDirection == Vector2.Zero &&
            movementInput != Vector2.Zero)
        {
            headingDirection = Vector2.Normalize(movementInput);
        }

        avatarInput.HeadingDirection = headingDirection;

        if (LocalSnapshot.AbsoluteMovement)
        {
            avatarInput.Throttle = Math.Clamp(movementInput.Length(), 0f, 1f);
            avatarInput.IsBraking = false;
        }
        else
        {
            avatarInput.IsBraking = movementInput.Y > 0f;
            avatarInput.Throttle = MathF.Max(0f, -movementInput.Y);
        }
    }
}

