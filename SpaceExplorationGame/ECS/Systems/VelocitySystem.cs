using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Integrates acceleration and velocity, then applies position/rotation updates
/// for all entities with both components.
/// </summary>
public partial class VelocitySystem : BaseSystem<World, float>
{
    public VelocitySystem(World world) : base(world) { }

    [Query]
    [All(typeof(Transform), typeof(Velocity))]
    public void ApplyVelocity(ref Transform transform, ref Velocity velocity, [Data] float dt)
    {
        // Integrate acceleration into linear velocity
        velocity.Velocity += velocity.Acceleration * dt;

        // Clamp linear speed
        if (velocity.Velocity.LengthSquared() > velocity.MaxSpeed * velocity.MaxSpeed)
        {
            velocity.Velocity = Vector2.Normalize(velocity.Velocity) * velocity.MaxSpeed;
        }

        // Apply linear velocity to position
        if (velocity.Velocity != Vector2.Zero)
        {
            var nextPosition = transform.Position + velocity.Velocity * dt;
            bool canMove = velocity.CanMoveTo?.Invoke(nextPosition) ?? true;

            if (canMove)
            {
                transform.Position = nextPosition;
            }
            else
            {
                velocity.Velocity = Vector2.Zero;
            }
        }

        // Apply rotation velocity
        if (velocity.RotationVelocity != 0f)
        {
            if (velocity.MaxRotationSpeed > 0f &&
                MathF.Abs(velocity.RotationVelocity) > velocity.MaxRotationSpeed)
            {
                velocity.RotationVelocity = MathF.Sign(velocity.RotationVelocity) * velocity.MaxRotationSpeed;
            }

            transform.Rotation += velocity.RotationVelocity * dt;
        }
    }
}
