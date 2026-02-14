using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Updates orbital positions deterministically based on global time.
/// Each orbiting entity computes its position from its parent's position,
/// orbit radius, speed, and base angle.
/// </summary>
public partial class OrbitSystem : BaseSystem<World, float>
{
    private readonly Func<float> _getGlobalTime;
    private readonly Func<Vector2> _getFallbackCenter;

    /// <param name="world">ECS world.</param>
    /// <param name="getGlobalTime">Returns the current global simulation time.</param>
    /// <param name="getFallbackCenter">Returns a fallback position when the orbit parent is dead.</param>
    public OrbitSystem(World world, Func<float> getGlobalTime, Func<Vector2> getFallbackCenter)
        : base(world)
    {
        _getGlobalTime = getGlobalTime;
        _getFallbackCenter = getFallbackCenter;
    }

    [Query]
    [All(typeof(Transform), typeof(Orbit))]
    public void UpdateOrbit(Entity entity, ref Transform transform, ref Orbit orbit)
    {
        float time = _getGlobalTime();
        orbit.CurrentAngle = orbit.BaseAngle + orbit.OrbitSpeed * time;

        Vector2 parentPos;
        if (World.IsAlive(orbit.Parent))
        {
            parentPos = World.Get<Transform>(orbit.Parent).Position;
        }
        else
        {
            parentPos = _getFallbackCenter();
        }

        transform.Position = parentPos + new Vector2(
            MathF.Cos(orbit.CurrentAngle) * orbit.OrbitRadius,
            MathF.Sin(orbit.CurrentAngle) * orbit.OrbitRadius
        );
    }
}
