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
        // Clamp to max speed
        if (velocity.Value.LengthSquared() > velocity.MaxSpeed * velocity.MaxSpeed)
        {
            velocity.Value = Vector2.Normalize(velocity.Value) * velocity.MaxSpeed;
        }

        transform.Position += velocity.Value * dt;
    }
}
