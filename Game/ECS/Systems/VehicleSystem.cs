using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Applies vehicle physics for all entities that carry a <see cref="VehicleComponent"/>
/// (i.e. player avatars that are currently mounted in a vehicle).
/// Reads intent from <see cref="AvatarInputComponent"/> and writes to <see cref="Velocity"/>.
/// Analogous to <see cref="AvatarSystem"/> for walking.
/// </summary>
public partial class VehicleSystem : BaseSystem<World, float>
{
    public VehicleSystem(World world) : base(world) { }

    public override void Update(in float dt)
    {
        ProcessVehicleQuery(World, dt);
    }

    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(AvatarInputComponent), typeof(VehicleComponent))]
    public void ProcessVehicle(ref Transform transform, ref Velocity velocity,
        ref AvatarInputComponent input, ref VehicleComponent vehicle, [Data] in float dt)
    {
        float rotationRadians = transform.Rotation * MathF.PI / 180f;
        Vector2 forward = new(MathF.Cos(rotationRadians), MathF.Sin(rotationRadians));

        // Keep linear movement fully aligned with facing to avoid floaty lateral drift
        if (velocity.Linear != Vector2.Zero)
        {
            float forwardSpeed = Vector2.Dot(velocity.Linear, forward);
            velocity.Linear = forward * forwardSpeed;
        }

        velocity.Acceleration = Vector2.Zero;
        velocity.RotationVelocity = 0f;

        // Rotate toward heading direction
        if (input.HeadingDirection != Vector2.Zero && dt > 0f)
        {
            float targetRotation = MathF.Atan2(input.HeadingDirection.Y, input.HeadingDirection.X) * 180f / MathF.PI;
            float delta = targetRotation - transform.Rotation;
            delta = ((delta % 360f) + 540f) % 360f - 180f;

            float requiredRotationSpeed = delta / dt;
            velocity.RotationVelocity = Math.Clamp(requiredRotationSpeed, -vehicle.RotationSpeed, vehicle.RotationSpeed);
        }

        // Apply throttle and braking
        velocity.Damping = vehicle.Friction * (input.IsBraking ? vehicle.BrakeMultiplier : 1f);
        velocity.Acceleration += forward * input.Throttle * vehicle.Acceleration;
    }
}
