using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Input;

/// <summary>
/// Writes movement and shoot intent for the player-controlled avatar entity into <see cref="AvatarInputComponent"/>.
/// Reads <see cref="AvatarComponent.InVehicle"/> to suppress avatar input while driving.
/// <see cref="AvatarSystem"/> converts intents into physics and projectile spawning.
/// </summary>
public partial class PlayerAvatarInputSystem : BaseSystem<World, float>
{
    private readonly IInputManager _input;
    private readonly float _speed;
    private readonly Camera _camera;

    private Vector2 _lastMoveDir = new(0, -1);

    public PlayerAvatarInputSystem(World world, IInputManager input, float speed, Camera camera) : base(world)
    {
        _input = input;
        _speed = speed;
        _camera = camera;
    }

    public override void Update(in float dt)
    {
        Vector2 moveDir = _input.GetActionAxisDirection(InputActionAxis.Movement);
        if (moveDir != Vector2.Zero) _lastMoveDir = moveDir;
        SetAvatarInputQuery(World);
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(AvatarInputComponent), typeof(AvatarComponent), typeof(Transform))]
    public void SetAvatarInput(ref Transform transform, ref AvatarInputComponent input, ref AvatarComponent avatar)
    {
        if (avatar.InVehicle)
        {
            input.DesiredVelocity = Vector2.Zero;
            input.Shoot = false;
            return;
        }

        input.DesiredVelocity = _input.GetActionAxisDirection(InputActionAxis.Movement) * _speed;
        input.Shoot = _input.IsActionDown(InputAction.FireWeapon);

        if (input.Shoot)
        {
            var gamepadHeading = _input.ActiveInputMethod == InputMethod.Gamepad
                ? _input.GetActionAxisDirection(InputActionAxis.Heading) : Vector2.Zero;

            if (gamepadHeading != Vector2.Zero)
                input.AimDirection = gamepadHeading;
            else if (_input.IsMouseDown(MouseButton.Left))
            {
                var mouseWorld = _camera.ScreenToWorld(new Vector2(_input.MouseX, _input.MouseY));
                var dir = Vector2.Normalize(mouseWorld - transform.Position);
                input.AimDirection = float.IsNaN(dir.X) ? _lastMoveDir : dir;
            }
            else
                input.AimDirection = _lastMoveDir;
        }
    }
}
