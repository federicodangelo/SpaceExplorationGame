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
        velocity.Linear += velocity.Acceleration * dt;

        // Clamp linear speed
        if (velocity.MaxSpeed > 0f &&
            velocity.Linear.LengthSquared() > velocity.MaxSpeed * velocity.MaxSpeed)
        {
            velocity.Linear = Vector2.Normalize(velocity.Linear) * velocity.MaxSpeed;
        }

        // Centralized damping
        if (velocity.Damping < 1f)
        {
            velocity.Linear *= Math.Clamp(velocity.Damping, 0f, 1f);
        }

        // Apply linear velocity to position
        if (velocity.Linear != Vector2.Zero)
        {
            var nextPosition = transform.Position + velocity.Linear * dt;
            bool canMove = velocity.CanMoveTo?.Invoke(nextPosition) ?? true;

            if (canMove)
            {
                transform.Position = nextPosition;
            }
            else
            {
                velocity.Linear = Vector2.Zero;
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
