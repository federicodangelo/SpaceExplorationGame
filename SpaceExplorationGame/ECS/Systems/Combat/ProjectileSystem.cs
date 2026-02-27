using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Combat;

/// <summary>A projectile hit event: which projectile hit which target and for how much damage.</summary>
public readonly record struct ProjectileHit(Entity Projectile, Entity Target, float Damage, Faction OwnerFaction, Entity OwnerEntity);

/// <summary>Per-frame snapshot of a live projectile's state for collision checking.</summary>
public readonly record struct ProjectileSnapshot(Entity Entity, Vector2 Position, Projectile Proj);

/// <summary>Per-frame snapshot of a potential hit target.</summary>
public readonly record struct TargetSnapshot(Entity Entity, Vector2 Position, float Radius, Faction? Faction);

/// <summary>An entity destroyed by projectile damage this frame.</summary>
public readonly record struct DestroyedEntity(Entity Entity, Vector2 Position, Faction Faction, LootDrop? Loot, AsteroidField? Asteroid, Faction KillerFaction, Entity KillerEntity);

/// <summary>A damage event from a projectile hit (for visual effects).</summary>
public readonly record struct DamageEvent(Vector2 Position, float Damage, bool ShieldHit, Entity Target, Faction OwnerFaction, Entity OwnerEntity);

/// <summary>
/// Moves projectiles, checks lifetime expiry, and detects collision with Health entities.
/// Uses Arch source generator for projectile iteration; manual queries for collision detection.
/// </summary>
public partial class ProjectileSystem : BaseSystem<World, float>
{
    // Collision results per frame
    private readonly List<ProjectileHit> _hits = [];
    private readonly HashSet<Entity> _expired = [];
    private readonly List<ProjectileSnapshot> _projectileData = [];
    private readonly List<TargetSnapshot> _targetData = [];
    private readonly HashSet<Entity> _processedProjectiles = [];
    private readonly SpatialHash _spatialHash = new();
    private float _dt;

    /// <summary>Entities destroyed by projectile hits last update.</summary>
    public List<DestroyedEntity> DestroyedLastUpdate { get; } = [];

    /// <summary>Damage events from last update.</summary>
    public List<DamageEvent> DamageEventsLastUpdate { get; } = [];

    public ProjectileSystem(World world) : base(world)
    {
    }

    public override void Update(in float dt)
    {
        DestroyedLastUpdate.Clear();
        DamageEventsLastUpdate.Clear();
        _hits.Clear();
        _expired.Clear();
        _projectileData.Clear();
        _targetData.Clear();
        _processedProjectiles.Clear();
        _dt = dt;

        // 1. Collect projectile data and find expired — via source-generated query
        CollectProjectilesQuery(World);

        // 2. Collect potential targets (only if there are projectiles)
        if (_projectileData.Count > 0)
            CollectTargetsQuery(World);

        // 2b. Build spatial hash from targets for fast neighbour lookup
        _spatialHash.Clear();
        for (int i = 0; i < _targetData.Count; i++)
            _spatialHash.Insert(_targetData[i].Position, i);

        // 3. Check collisions via spatial hash
        foreach (var snapshot in _projectileData)
        {
            var projEntity = snapshot.Entity;
            var projPos = snapshot.Position;
            var proj = snapshot.Proj;

            float queryRadius = proj.CollisionRadius + SpatialHash.CellSize; // generous query range
            foreach (int targetIdx in _spatialHash.Query(projPos, queryRadius))
            {
                var target = _targetData[targetIdx];
                if (target.Entity == projEntity) continue;

                // Faction-based hit filtering
                if (!FactionRules.CanHit(proj.OwnerFaction, target.Faction))
                    continue;

                float dist = Vector2.Distance(projPos, target.Position);
                if (dist < proj.CollisionRadius + target.Radius)
                {
                    _hits.Add(new ProjectileHit(projEntity, target.Entity, proj.Damage, proj.OwnerFaction, proj.OwnerEntity));
                }
            }
        }

        // 4. Process hits
        foreach (var hit in _hits)
        {
            if (_processedProjectiles.Contains(hit.Projectile)) continue;
            if (!World.IsAlive(hit.Target)) continue;
            if (!World.IsAlive(hit.Projectile)) continue;

            _processedProjectiles.Add(hit.Projectile);

            ref var health = ref World.Get<Health>(hit.Target);
            bool hadShield = health.Shield > 0;
            health.TakeDamage(hit.Damage);

            var targetPos = World.Get<Transform>(hit.Target).Position;
            DamageEventsLastUpdate.Add(new DamageEvent(targetPos, hit.Damage, hadShield, hit.Target, hit.OwnerFaction, hit.OwnerEntity));

            // Destroy the projectile (HashSet handles duplicates automatically)
            _expired.Add(hit.Projectile);

            // Check if target died
            if (health.IsDead)
            {
                var faction = Faction.Player;
                LootDrop? loot = null;
                AsteroidField? asteroid = null;

                if (World.Has<AsteroidField>(hit.Target))
                {
                    asteroid = World.Get<AsteroidField>(hit.Target);
                }
                else if (World.Has<EnemyAI>(hit.Target))
                {
                    faction = World.Get<EnemyAI>(hit.Target).Config.Faction;
                    if (World.Has<LootDrop>(hit.Target))
                        loot = World.Get<LootDrop>(hit.Target);
                }
                else if (World.Has<SurfaceAI>(hit.Target))
                {
                    faction = World.Get<SurfaceAI>(hit.Target).Config.Faction;
                    if (World.Has<LootDrop>(hit.Target))
                        loot = World.Get<LootDrop>(hit.Target);
                }
                else if (World.Has<PlayerControlled>(hit.Target))
                {
                    faction = Faction.Player;
                }

                DestroyedLastUpdate.Add(new DestroyedEntity(hit.Target, targetPos, faction, loot, asteroid, hit.OwnerFaction, hit.OwnerEntity));
            }
        }

        // 5. Destroy expired/hit projectiles
        foreach (var entity in _expired)
        {
            if (World.IsAlive(entity))
                World.Destroy(entity);
        }
    }

    /// <summary>Source-generated query: collects projectile data and marks expired projectiles.</summary>
    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(Projectile))]
    public void CollectProjectiles(Entity entity, ref Transform transform, ref Projectile proj)
    {
        proj.Lifetime -= _dt;
        if (proj.Lifetime <= 0)
        {
            _expired.Add(entity);
            return;
        }

        // Collect for collision phase (copy values out of the ref context)
        _projectileData.Add(new ProjectileSnapshot(entity, transform.Position, proj));
    }

    /// <summary>Source-generated query: collects potential hit targets for this update.</summary>
    [Query]
    [All(typeof(Transform), typeof(Health))]
    public void CollectTargets(Entity target, ref Transform targetTransform, ref Health targetHealth)
    {
        if (targetHealth.IsDead) return;

        Faction? targetFaction = null;
        if (World.Has<EnemyAI>(target))
            targetFaction = World.Get<EnemyAI>(target).Config.Faction;
        else if (World.Has<SurfaceAI>(target))
            targetFaction = World.Get<SurfaceAI>(target).Config.Faction;
        else if (World.Has<PlayerControlled>(target))
            targetFaction = Faction.Player;

        float targetRadius = 16f; // default collision radius
        if (World.Has<Sprite>(target))
        {
            var sprite = World.Get<Sprite>(target);
            targetRadius = MathF.Max(sprite.Width, sprite.Height) / 2f;
        }

        _targetData.Add(new TargetSnapshot(target, targetTransform.Position, targetRadius, targetFaction));
    }
}
