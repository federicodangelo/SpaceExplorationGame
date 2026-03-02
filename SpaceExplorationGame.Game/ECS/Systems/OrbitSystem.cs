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
    private readonly Vector2 _fallbackCenter;

    /// <param name="world">ECS world.</param>
    /// <param name="fallbackCenter">Position to orbit around if parent entity is missing.</param>
    public OrbitSystem(World world, Vector2 fallbackCenter)
        : base(world)
    {
        _fallbackCenter = fallbackCenter;
    }

    [Query]
    [All(typeof(Transform), typeof(Orbit))]
    public void UpdateOrbit([Data] float globalTime, ref Transform transform, ref Orbit orbit)
    {
        orbit.CurrentAngle = orbit.BaseAngle + orbit.OrbitSpeed * globalTime;

        Vector2 parentPos;
        if (World.IsAlive(orbit.Parent))
        {
            parentPos = World.Get<Transform>(orbit.Parent).Position;
        }
        else
        {
            parentPos = _fallbackCenter;
        }

        transform.Position = parentPos + new Vector2(
            MathF.Cos(orbit.CurrentAngle) * orbit.OrbitRadius,
            MathF.Sin(orbit.CurrentAngle) * orbit.OrbitRadius
        );
    }
}
