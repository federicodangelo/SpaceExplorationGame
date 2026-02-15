using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Applies velocity to transform position for all entities with both components.
/// Also enforces MaxSpeed clamping.
/// </summary>
public partial class VelocitySystem : BaseSystem<World, float>
{
    public VelocitySystem(World world) : base(world) { }

    [Query]
    [All(typeof(Transform), typeof(Velocity))]
    public void ApplyVelocity(ref Transform transform, ref Velocity velocity, [Data] float dt)
    {
        // Clamp linear speed
        if (velocity.Value.LengthSquared() > velocity.MaxSpeed * velocity.MaxSpeed)
        {
            velocity.Value = Vector2.Normalize(velocity.Value) * velocity.MaxSpeed;
        }

        transform.Position += velocity.Value * dt;

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
