using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Input;

/// <summary>
/// Reads vehicle input axes from the player and writes intent into <see cref="AvatarInputComponent"/>.
/// The actual physics (rotation, thrust, braking, damping) is applied by <see cref="VehicleSystem"/>.
/// </summary>
public partial class PlayerVehicleInputSystem : BaseSystem<World, float>
{
    private readonly IInputManager _input;
    private readonly Entity _entity;

    public PlayerVehicleInputSystem(World world, IInputManager input, Entity entity) : base(world)
    {
        _input = input;
        _entity = entity;
    }

    public override void Update(in float dt)
    {
        ref var avatarInput = ref World.Get<AvatarInputComponent>(_entity);

        Vector2 movementInput = _input.GetActionAxisDirection(InputActionAxis.Movement);
        Vector2 headingDirection = _input.GetActionAxisDirection(InputActionAxis.Heading);

        if (_input.MovementMode == MovementInputMode.Absolute &&
            headingDirection == Vector2.Zero &&
            movementInput != Vector2.Zero)
        {
            headingDirection = Vector2.Normalize(movementInput);
        }

        avatarInput.HeadingDirection = headingDirection;

        switch (_input.MovementMode)
        {
            case MovementInputMode.Absolute:
                avatarInput.Throttle = Math.Clamp(movementInput.Length(), 0f, 1f);
                avatarInput.IsBraking = false;
                break;

            case MovementInputMode.HeadingRelative:
            default:
                avatarInput.IsBraking = movementInput.Y > 0f;
                avatarInput.Throttle = MathF.Max(0f, -movementInput.Y);
                break;
        }
    }
}

