using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Combat;

/// <summary>A projectile hit event: which projectile hit which target and for how much damage.</summary>
public readonly record struct ProjectileHit(Entity Projectile, Entity Target, float Damage, Faction OwnerFaction, Entity OwnerEntity);

/// <summary>Per-frame snapshot of a live projectile's state for collision checking.</summary>
public readonly record struct ProjectileSnapshot(Entity Entity, Vector2 Position, Projectile Proj);

/// <summary>Per-frame snapshot of a potential hit target.</summary>
public readonly record struct TargetSnapshot(Entity Entity, Vector2 Position, float Radius, Faction? Faction);

/// <summary>An entity killed by projectile damage this frame.</summary>
public readonly record struct KilledEntity(Entity Entity, Vector2 Position, Faction Faction, LootDrop? Loot, AsteroidField? Asteroid, Faction KillerFaction, Entity KillerEntity);

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
    public List<KilledEntity> KilledLastUpdate { get; } = [];

    /// <summary>Damage events from last update.</summary>
    public List<DamageEvent> DamageEventsLastUpdate { get; } = [];

    public ProjectileSystem(World world) : base(world)
    {
    }

    public override void Update(in float dt)
    {
        KilledLastUpdate.Clear();
        DamageEventsLastUpdate.Clear();
        _hits.Clear();
        _expired.Clear();
        _projectileData.Clear();
        _targetData.Clear();
        _processedProjectiles.Clear();
        _dt = dt;

        // 1. Collect potential targets first (needed for tracking missile steering)
        CollectTargetsQuery(World);

        // 2. Collect projectile data and find expired — via source-generated query
        // (tracking missiles need target data to steer)
        CollectProjectilesQuery(World);

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

            if (proj.Behavior == WeaponBehavior.Beam)
            {
                // Beam collision: line segment from projPos in the facing direction
                float rad = 0f;
                if (World.IsAlive(projEntity))
                {
                    var tf = World.Get<Transform>(projEntity);
                    rad = tf.Rotation * MathF.PI / 180f;
                }
                var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
                var beamEnd = projPos + dir * CombatConfig.BeamMaxRange;
                float beamDps = proj.Damage;
                float frameDamage = beamDps * _dt;

                // Check all targets along the beam line
                float queryRadius = CombatConfig.BeamMaxRange + SpatialHash.CellSize;
                var beamMid = (projPos + beamEnd) * 0.5f;
                foreach (int targetIdx in _spatialHash.Query(beamMid, queryRadius))
                {
                    var target = _targetData[targetIdx];
                    if (!FactionRules.CanHit(proj.OwnerFaction, target.Faction))
                        continue;

                    // Point-to-line-segment distance
                    float dist = PointToSegmentDistance(target.Position, projPos, beamEnd);
                    if (dist < CombatConfig.BeamWidth + target.Radius)
                    {
                        _hits.Add(new ProjectileHit(projEntity, target.Entity, frameDamage, proj.OwnerFaction, proj.OwnerEntity));
                    }
                }
                continue; // Beams don't get destroyed on hit
            }

            float queryRadius2 = proj.CollisionRadius + SpatialHash.CellSize; // generous query range
            foreach (int targetIdx in _spatialHash.Query(projPos, queryRadius2))
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
            // Beam projectiles can hit multiple targets per frame, don't track them as single-use
            bool isBeam = World.IsAlive(hit.Projectile) && World.Has<Projectile>(hit.Projectile) &&
                          World.Get<Projectile>(hit.Projectile).Behavior == WeaponBehavior.Beam;

            if (!isBeam && _processedProjectiles.Contains(hit.Projectile)) continue;
            if (!World.IsAlive(hit.Target)) continue;
            if (!World.IsAlive(hit.Projectile)) continue;

            if (!isBeam)
                _processedProjectiles.Add(hit.Projectile);

            ref var health = ref World.Get<Health>(hit.Target);
            bool hadShield = health.Shield > 0;
            float shieldPierce = World.Get<Projectile>(hit.Projectile).ShieldPierce;
            health.TakeDamage(hit.Damage, shieldPierce);

            var targetPos = World.Get<Transform>(hit.Target).Position;
            DamageEventsLastUpdate.Add(new DamageEvent(targetPos, hit.Damage, hadShield, hit.Target, hit.OwnerFaction, hit.OwnerEntity));

            // Destroy the projectile (HashSet handles duplicates automatically) — not for beams
            if (!isBeam)
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

                KilledLastUpdate.Add(new KilledEntity(hit.Target, targetPos, faction, loot, asteroid, hit.OwnerFaction, hit.OwnerEntity));
            }
        }

        // 5. Destroy expired/hit projectiles
        foreach (var entity in _expired)
        {
            if (World.IsAlive(entity))
                World.Destroy(entity);
        }
    }

    /// <summary>Source-generated query: collects projectile data, steers tracking missiles, and marks expired projectiles.</summary>
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

        // Tracking missiles: steer toward nearest valid target
        if (proj.Behavior == WeaponBehavior.Tracking && proj.TrackingTurnRate > 0f && _dt > 0f)
        {
            SteerTrackingMissile(ref transform, ref velocity, ref proj);
        }

        // Beam projectiles: stay attached to owner and face owner's direction
        if (proj.Behavior == WeaponBehavior.Beam && World.IsAlive(proj.OwnerEntity))
        {
            var ownerTf = World.Get<Transform>(proj.OwnerEntity);
            float rad = ownerTf.Rotation * MathF.PI / 180f;
            var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
            transform.Position = ownerTf.Position + dir * 20f;
            transform.Rotation = ownerTf.Rotation;
            velocity.Linear = Vector2.Zero;
        }

        // Collect for collision phase (copy values out of the ref context)
        _projectileData.Add(new ProjectileSnapshot(entity, transform.Position, proj));
    }

    private void SteerTrackingMissile(ref Transform transform, ref Velocity velocity, ref Projectile proj)
    {
        // Find nearest valid target
        Vector2 bestTarget = default;
        float bestDist = float.MaxValue;
        bool hasTarget = false;

        for (int i = 0; i < _targetData.Count; i++)
        {
            var t = _targetData[i];
            if (!FactionRules.CanHit(proj.OwnerFaction, t.Faction)) continue;
            float d = Vector2.Distance(transform.Position, t.Position);
            if (d < bestDist)
            {
                bestDist = d;
                bestTarget = t.Position;
                hasTarget = true;
            }
        }

        // If no target data yet (first pass), query targets before steering
        if (!hasTarget && _targetData.Count == 0)
            return;

        if (!hasTarget) return;

        var toTarget = Vector2.Normalize(bestTarget - transform.Position);
        if (float.IsNaN(toTarget.X)) return;

        float currentAngle = MathF.Atan2(velocity.Linear.Y, velocity.Linear.X);
        float desiredAngle = MathF.Atan2(toTarget.Y, toTarget.X);
        float delta = desiredAngle - currentAngle;
        // Normalize to [-PI, PI]
        delta = ((delta + MathF.PI * 3f) % (MathF.PI * 2f)) - MathF.PI;

        float maxTurn = proj.TrackingTurnRate * MathF.PI / 180f * _dt;
        float turn = Math.Clamp(delta, -maxTurn, maxTurn);
        float newAngle = currentAngle + turn;

        velocity.Linear = new Vector2(MathF.Cos(newAngle), MathF.Sin(newAngle)) * proj.Speed;
        transform.Rotation = newAngle * 180f / MathF.PI;
    }

    /// <summary>Source-generated query: collects potential hit targets for this update.</summary>
    [Query]
    [All(typeof(Transform), typeof(Health))]
    public void CollectTargets(Entity target, ref Transform targetTransform, ref Health targetHealth)
    {
        if (targetHealth.IsDead) return;

        // Ships that are warping in/out are invulnerable
        if (World.Has<WarpEffect>(target)) return;

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

    /// <summary>Shortest distance from point P to line segment AB.</summary>
    private static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float abLenSq = ab.LengthSquared();
        if (abLenSq < 0.001f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / abLenSq, 0f, 1f);
        var closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }
}
