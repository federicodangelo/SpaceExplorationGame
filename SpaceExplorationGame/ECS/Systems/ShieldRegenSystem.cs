using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Regenerates shields for all entities with Health components after a delay since last hit.
/// Uses Arch source generator for automatic query iteration.
/// </summary>
public partial class ShieldRegenSystem : BaseSystem<World, float>
{
    public ShieldRegenSystem(World world) : base(world) { }

    [Query]
    [All(typeof(Health))]
    public void RegenShields(ref Health health, [Data] float dt)
    {
        if (health.IsDead) return;
        if (health.MaxShield <= 0) return;
        if (health.Shield >= health.MaxShield) return;

        health.TimeSinceLastHit += dt;

        if (health.TimeSinceLastHit >= health.ShieldRegenDelay)
        {
            health.Shield = MathF.Min(health.Shield + health.ShieldRegenRate * dt, health.MaxShield);
        }
    }
}
