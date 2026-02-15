using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Moves projectiles, checks lifetime expiry, and detects collision with Health entities.
/// Uses Arch source generator for projectile iteration; manual queries for collision detection.
/// </summary>
public partial class ProjectileSystem : BaseSystem<World, float>
{
    // Collision results per frame
    private readonly List<(Entity Projectile, Entity Target, float Damage)> _hits = [];
    private readonly HashSet<Entity> _expired = new();

    // Cached query description for collision checking
    private static readonly QueryDescription _healthQuery = new QueryDescription().WithAll<Transform, Health>();

    // Per-frame data
    private readonly List<(Entity Entity, Vector2 Position, Projectile Proj)> _projectileData = [];
    private float _dt;

    /// <summary>Entities destroyed this frame (for loot/explosion handling).</summary>
    public List<(Entity Entity, Vector2 Position, Faction Faction, LootDrop? Loot, AsteroidField? Asteroid)> DestroyedThisFrame { get; } = [];

    /// <summary>Damage events from last update (for visual effects).</summary>
    public List<(Vector2 Position, float Damage, bool ShieldHit, Entity Target)> DamageEventsLastUpdate { get; } = [];

    public ProjectileSystem(World world) : base(world)
    {
    }

    public override void Update(in float dt)
    {
        _hits.Clear();
        _expired.Clear();
        DestroyedThisFrame.Clear();
        DamageEventsLastUpdate.Clear();
        _projectileData.Clear();
        _dt = dt;

        // 1. Collect projectile data and find expired — via source-generated query
        CollectProjectilesQuery(World);

        // 2. Check collisions — done outside the nested query to avoid ref-capture issues
        foreach (var (projEntity, projPos, proj) in _projectileData)
        {
            World.Query(in _healthQuery, (Entity target, ref Transform targetTransform, ref Health targetHealth) =>
            {
                if (target == projEntity) return;
                if (targetHealth.IsDead) return;

                // Determine target's faction
                Faction? targetFaction = null;
                if (World.Has<EnemyAI>(target))
                    targetFaction = World.Get<EnemyAI>(target).Config.Faction;
                else if (World.Has<SurfaceAI>(target))
                    targetFaction = World.Get<SurfaceAI>(target).Config.Faction;
                else if (World.Has<PlayerControlled>(target))
                    targetFaction = Faction.Player;

                // Don't hit entities of the same faction
                if (targetFaction == proj.OwnerFaction) return;

                // Player projectiles skip player-controlled entities
                if (proj.OwnerFaction == Faction.Player && World.Has<PlayerControlled>(target))
                    return;

                // Pirate projectiles should not hit other pirates
                if (proj.OwnerFaction == Faction.Pirate && targetFaction == Faction.Pirate)
                    return;
                // Patrol/trader projectiles should not hit player or each other (friendly fire off)
                if ((proj.OwnerFaction == Faction.Patrol || proj.OwnerFaction == Faction.Trader) &&
                    (targetFaction == Faction.Player || (targetFaction.HasValue && targetFaction != Faction.Pirate)))
                    return;
                // Fauna/Bandit projectiles should not hit each other 
                if ((proj.OwnerFaction == Faction.Fauna || proj.OwnerFaction == Faction.Bandit) &&
                    (targetFaction == Faction.Fauna || targetFaction == Faction.Bandit))
                    return;

                // Distance check
                float dist = Vector2.Distance(projPos, targetTransform.Position);
                float targetRadius = 16f; // default collision radius
                if (World.Has<Sprite>(target))
                {
                    var sprite = World.Get<Sprite>(target);
                    targetRadius = MathF.Max(sprite.Width, sprite.Height) / 2f;
                }

                if (dist < proj.CollisionRadius + targetRadius)
                {
                    _hits.Add((projEntity, target, proj.Damage));
                }
            });
        }

        // 3. Process hits
        var processedProjectiles = new HashSet<Entity>();
        foreach (var (projectile, target, damage) in _hits)
        {
            if (processedProjectiles.Contains(projectile)) continue;
            if (!World.IsAlive(target)) continue;
            if (!World.IsAlive(projectile)) continue;

            processedProjectiles.Add(projectile);

            ref var health = ref World.Get<Health>(target);
            bool hadShield = health.Shield > 0;
            health.TakeDamage(damage);

            var targetPos = World.Get<Transform>(target).Position;
            DamageEventsLastUpdate.Add((targetPos, damage, hadShield, target));

            // Destroy the projectile (HashSet handles duplicates automatically)
            _expired.Add(projectile);

            // Check if target died
            if (health.IsDead)
            {
                var faction = Faction.Player;
                LootDrop? loot = null;
                AsteroidField? asteroid = null;

                if (World.Has<AsteroidField>(target))
                {
                    asteroid = World.Get<AsteroidField>(target);
                }
                else if (World.Has<EnemyAI>(target))
                {
                    faction = World.Get<EnemyAI>(target).Config.Faction;
                    if (World.Has<LootDrop>(target))
                        loot = World.Get<LootDrop>(target);
                }
                else if (World.Has<SurfaceAI>(target))
                {
                    faction = World.Get<SurfaceAI>(target).Config.Faction;
                    if (World.Has<LootDrop>(target))
                        loot = World.Get<LootDrop>(target);
                }
                else if (World.Has<PlayerControlled>(target))
                {
                    faction = Faction.Player;
                }

                DestroyedThisFrame.Add((target, targetPos, faction, loot, asteroid));
            }
        }

        // 4. Destroy expired/hit projectiles
        foreach (var entity in _expired)
        {
            if (World.IsAlive(entity))
                World.Destroy(entity);
        }
    }

    /// <summary>Source-generated query: collects projectile data and marks expired projectiles.</summary>
    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(Projectile))]
    public void CollectProjectiles(Entity entity, ref Transform transform, ref Velocity velocity, ref Projectile proj)
    {
        proj.Lifetime -= _dt;
        if (proj.Lifetime <= 0)
        {
            _expired.Add(entity);
            return;
        }

        // Collect for collision phase (copy values out of the ref context)
        _projectileData.Add((entity, transform.Position, proj));
    }
}
