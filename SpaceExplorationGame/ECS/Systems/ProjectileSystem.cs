using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Moves projectiles, checks lifetime expiry, and detects collision with Health entities.
/// Plain class with manual queries because it needs to destroy entities during iteration.
/// </summary>
public class ProjectileSystem
{
    private readonly World _world;

    // Collision results per frame
    private readonly List<(Entity Projectile, Entity Target, float Damage)> _hits = [];
    private readonly List<Entity> _expired = [];

    /// <summary>Entities destroyed this frame (for loot/explosion handling).</summary>
    public List<(Entity Entity, Vector2 Position, Faction Faction, LootDrop? Loot)> DestroyedThisFrame { get; } = [];

    /// <summary>Damage events this frame (for visual effects).</summary>
    public List<(Vector2 Position, float Damage, bool ShieldHit)> DamageEventsThisFrame { get; } = [];

    public ProjectileSystem(World world)
    {
        _world = world;
    }

    public void Update(float dt)
    {
        _hits.Clear();
        _expired.Clear();
        DestroyedThisFrame.Clear();
        DamageEventsThisFrame.Clear();

        // 1. Move projectiles and check lifetime — collect data for collision phase
        var projectileData = new List<(Entity Entity, Vector2 Position, Projectile Proj)>();
        var projectileQuery = new QueryDescription().WithAll<Transform, Velocity, Projectile>();
        _world.Query(in projectileQuery, (Entity entity, ref Transform transform, ref Velocity velocity, ref Projectile proj) =>
        {
            proj.Lifetime -= dt;
            if (proj.Lifetime <= 0)
            {
                _expired.Add(entity);
                return;
            }

            // Collect for collision phase (copy values out of the ref context)
            projectileData.Add((entity, transform.Position, proj));
        });

        // 2. Check collisions — done outside the nested query to avoid ref-capture issues
        foreach (var (projEntity, projPos, proj) in projectileData)
        {
            var healthQuery = new QueryDescription().WithAll<Transform, Health>();
            _world.Query(in healthQuery, (Entity target, ref Transform targetTransform, ref Health targetHealth) =>
            {
                if (target == projEntity) return;
                if (targetHealth.IsDead) return;

                // Don't hit entities of the same faction
                if (_world.Has<EnemyAI>(target))
                {
                    var ai = _world.Get<EnemyAI>(target);
                    if (ai.Faction == proj.OwnerFaction) return;
                }
                else if (_world.Has<PlayerControlled>(target) && proj.OwnerFaction == Faction.Player)
                {
                    return;
                }

                // Pirate projectiles should not hit other pirates
                if (proj.OwnerFaction == Faction.Pirate && _world.Has<EnemyAI>(target) && _world.Get<EnemyAI>(target).Faction == Faction.Pirate)
                    return;
                // Patrol/trader projectiles should not hit player or each other (friendly fire off)
                if ((proj.OwnerFaction == Faction.Patrol || proj.OwnerFaction == Faction.Trader) &&
                    (_world.Has<PlayerControlled>(target) ||
                     (_world.Has<EnemyAI>(target) && _world.Get<EnemyAI>(target).Faction != Faction.Pirate)))
                    return;

                // Distance check
                float dist = Vector2.Distance(projPos, targetTransform.Position);
                float targetRadius = 16f; // default collision radius
                if (_world.Has<Sprite>(target))
                {
                    var sprite = _world.Get<Sprite>(target);
                    targetRadius = MathF.Max(sprite.Width, sprite.Height) / 2f;
                }

                if (dist < proj.CollisionRadius + targetRadius)
                {
                    _hits.Add((projEntity, target, proj.Damage));
                }
            });
        }

        // 2. Process hits
        var processedProjectiles = new HashSet<Entity>();
        foreach (var (projectile, target, damage) in _hits)
        {
            if (processedProjectiles.Contains(projectile)) continue;
            if (!_world.IsAlive(target)) continue;
            if (!_world.IsAlive(projectile)) continue;

            processedProjectiles.Add(projectile);

            ref var health = ref _world.Get<Health>(target);
            bool hadShield = health.Shield > 0;
            health.TakeDamage(damage);

            var targetPos = _world.Get<Transform>(target).Position;
            DamageEventsThisFrame.Add((targetPos, damage, hadShield));

            // Destroy the projectile
            if (!_expired.Contains(projectile))
                _expired.Add(projectile);

            // Check if target died
            if (health.IsDead)
            {
                var faction = Faction.Player;
                LootDrop? loot = null;

                if (_world.Has<EnemyAI>(target))
                {
                    faction = _world.Get<EnemyAI>(target).Faction;
                    if (_world.Has<LootDrop>(target))
                        loot = _world.Get<LootDrop>(target);
                }
                else if (_world.Has<PlayerControlled>(target))
                {
                    faction = Faction.Player;
                }

                DestroyedThisFrame.Add((target, targetPos, faction, loot));
            }
        }

        // 3. Destroy expired/hit projectiles
        foreach (var entity in _expired)
        {
            if (_world.IsAlive(entity))
                _world.Destroy(entity);
        }
    }
}
