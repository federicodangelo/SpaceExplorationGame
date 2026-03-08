using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Smoothly interpolates remote entities (other players and server-controlled NPCs)
/// toward their target network position/rotation each tick.
/// Uses a combination of velocity-based dead-reckoning and exponential smoothing
/// so that movement looks continuous between ~20 Hz server broadcasts.
/// </summary>
public partial class NetInterpolationSystem : BaseSystem<World, float>
{
    /// <summary>
    /// Smoothing factor per second — higher values follow the target more tightly.
    /// At 10, roughly 99.5% of the gap is closed within one broadcast interval (~50ms).
    /// </summary>
    private const float SmoothRate = 10f;

    /// <summary>
    /// Distance beyond which position snaps instantly instead of interpolating.
    /// Prevents slow drift when an entity warps or respawns far away.
    /// </summary>
    private const float SnapDistanceSq = 500f * 500f;

    public NetInterpolationSystem(World world) : base(world) { }

    [Query]
    [All(typeof(Transform), typeof(NetInterpolation))]
    public void Interpolate(ref Transform transform, ref NetInterpolation interp, [Data] float dt)
    {
        if (!interp.HasTarget) return;

        interp.TimeSinceUpdate += dt;

        // Dead-reckon the target using its velocity so we don't fall behind
        var predicted = interp.TargetPosition + interp.TargetVelocity * interp.TimeSinceUpdate;

        float distSq = Vector2.DistanceSquared(transform.Position, predicted);

        if (distSq > SnapDistanceSq)
        {
            // Snap — entity teleported or respawned
            transform.Position = predicted;
            transform.Rotation = interp.TargetRotation;
        }
        else
        {
            // Exponential smoothing
            float t = 1f - MathF.Exp(-SmoothRate * dt);
            transform.Position = Vector2.Lerp(transform.Position, predicted, t);
            transform.Rotation = LerpAngle(transform.Rotation, interp.TargetRotation, t);
        }
    }

    /// <summary>Lerp between two angles (in degrees), taking the shortest arc.</summary>
    private static float LerpAngle(float from, float to, float t)
    {
        float diff = ((to - from) % 360f + 540f) % 360f - 180f;
        return from + diff * t;
    }
}
