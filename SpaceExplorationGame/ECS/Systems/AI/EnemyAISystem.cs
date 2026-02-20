using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.AI;

/// <summary>
/// AI behavior system for NPC ships (pirates, traders, patrols).
/// Uses Arch source generator for the main entity query; manual queries for target finding.
/// </summary>
public partial class EnemyAISystem : BaseSystem<World, float>
{
    private readonly Func<Vector2> _getPlayerPosition;
    private readonly Func<bool> _isPlayerAlive;
    private readonly float _mapWidth;
    private readonly float _mapHeight;

    // Projectiles spawned this frame (to be created after query completes)
    private readonly List<ProjectileSpawn> _pendingProjectiles = [];

    /// <summary>Projectiles spawned during the last Update (available until next Update).</summary>
    public IReadOnlyList<ProjectileSpawn> ProjectilesSpawnedLastUpdate => _pendingProjectiles;

    // Cached query description for nested target/pirate lookups
    private static readonly QueryDescription _aiEntityQuery = new QueryDescription().WithAll<Transform, EnemyAI, Health>();

    // Per-frame cached state for [Query] method access
    private float _dt;
    private Vector2 _playerPos;
    private bool _playerAlive;

    public EnemyAISystem(World world, Func<Vector2> getPlayerPosition, Func<bool> isPlayerAlive,
        float mapWidth, float mapHeight)
        : base(world)
    {
        _getPlayerPosition = getPlayerPosition;
        _isPlayerAlive = isPlayerAlive;
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
    }

    public override void Update(in float dt)
    {
        _pendingProjectiles.Clear();
        _dt = dt;
        _playerPos = _getPlayerPosition();
        _playerAlive = _isPlayerAlive();

        ProcessEnemyAIQuery(World);

        // Spawn pending projectiles
        foreach (var (pos, dir, damage, speed, lifetime, faction, color) in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(World, pos, dir, damage, speed, faction, color, lifetime);
        }
    }

    /// <summary>Source-generated query: iterates all NPC ships with AI.</summary>
    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(EnemyAI), typeof(Health))]
    public void ProcessEnemyAI(Entity entity, ref Transform transform, ref Velocity velocity,
        ref EnemyAI ai, ref Health health)
    {
        if (health.IsDead) return;

        ai.StateTimer += _dt;
        UpdateWeaponCooldowns(ref ai);
        velocity.RotationVelocity = 0f; // Reset each frame; TurnToward sets it when needed
        velocity.Acceleration = Vector2.Zero;

        // Find the best target based on faction
        var (targetPos, hasTarget, targetEntity) = FindTarget(entity, ai.Config.Faction, transform.Position, ai.Config.DetectRange, _playerPos, _playerAlive);

        // State machine
        switch (ai.Config.Faction)
        {
            case Faction.Pirate:
                UpdatePirate(ref transform, ref velocity, ref ai, ref health, _dt, _playerPos, _playerAlive, targetPos, hasTarget);
                break;
            case Faction.Trader:
                UpdateTrader(ref transform, ref velocity, ref ai, ref health, _dt, _playerPos, _playerAlive);
                break;
            case Faction.Patrol:
                UpdatePatrol(ref transform, ref velocity, ref ai, ref health, _dt, targetPos, hasTarget);
                break;
        }
    }

    private void UpdatePirate(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 playerPos, bool playerAlive, Vector2 targetPos, bool hasTarget)
    {
        float maxSpeed = velocity.MaxSpeed;
        float thrust = ai.Config.Acceleration;
        float hullPercent = health.HullPercent;

        // Flee if low health
        if (hullPercent < ai.Config.FleeHealthPercent)
        {
            ai.State = AIState.Flee;
            var fleeDir = Vector2.Normalize(transform.Position - targetPos);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
            velocity.Acceleration += fleeDir * thrust * 0.5f;
            TurnTowardDirection(ref transform, ref velocity, fleeDir, ai.Config.MaxRotationSpeed, dt);
            return;
        }

        if (!hasTarget)
        {
            // Patrol: drift slowly in a random direction
            ai.State = AIState.Patrol;
            if (ai.StateTimer > 3f)
            {
                ai.StateTimer = 0;
                float angle = transform.Rotation * MathF.PI / 180f;
                angle += (float)(Math.Sin(transform.Position.X * 0.01 + transform.Position.Y * 0.01) * 0.5);
                TurnToward(ref transform, ref velocity, angle * 180f / MathF.PI, ai.Config.MaxRotationSpeed, dt);
            }
            float patrolRad = transform.Rotation * MathF.PI / 180f;
            velocity.Acceleration += new Vector2(MathF.Cos(patrolRad), MathF.Sin(patrolRad)) * thrust * 0.2f;
            ApplyDamping(ref velocity, 0.98f, dt);
            return;
        }

        float distToTarget = Vector2.Distance(transform.Position, targetPos);
        float weaponRange = GetWeaponRange(ai.Config.Weapons);

        if (distToTarget <= weaponRange)
        {
            // Attack
            ai.State = AIState.Attack;
            var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
            TurnTowardDirection(ref transform, ref velocity, dirToTarget, ai.Config.MaxRotationSpeed, dt);

            // Maintain engage distance
            if (distToTarget < ai.Config.EngageDistance * 0.7f)
            {
                // Too close — back up slightly
                velocity.Acceleration -= dirToTarget * thrust * 0.3f;
            }
            else if (distToTarget > ai.Config.EngageDistance * 1.3f)
            {
                // Too far — close in
                velocity.Acceleration += dirToTarget * thrust * 0.5f;
            }
            else
            {
                // Strafe
                var strafeDir = new Vector2(-dirToTarget.Y, dirToTarget.X);
                velocity.Acceleration += strafeDir * thrust * 0.3f *
                    MathF.Sign(MathF.Sin(ai.StateTimer * 0.8f));
            }

            TryFireProjectiles(ref transform, ref ai, dirToTarget);
        }
        else if (distToTarget <= ai.Config.DetectRange)
        {
            // Chase
            ai.State = AIState.Chase;
            var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
            TurnTowardDirection(ref transform, ref velocity, dirToTarget, ai.Config.MaxRotationSpeed, dt);
            velocity.Acceleration += dirToTarget * thrust * 0.6f;
        }

        // Apply friction
        ApplyDamping(ref velocity, 0.98f, dt);
    }

    private void UpdateTrader(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 playerPos, bool playerAlive)
    {
        float thrust = ai.Config.Acceleration;
        // Traders mostly just cruise around. They don't attack but will flee from nearby pirates.
        var nearestPirate = FindNearestPirate(transform.Position, 400f);

        if (nearestPirate.HasValue)
        {
            // Flee from pirate
            ai.State = AIState.Flee;
            var fleeDir = Vector2.Normalize(transform.Position - nearestPirate.Value);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
            velocity.Acceleration += fleeDir * thrust * 0.7f;
            TurnTowardDirection(ref transform, ref velocity, fleeDir, ai.Config.MaxRotationSpeed, dt);
        }
        else
        {
            // Cruise
            ai.State = AIState.Patrol;
            if (ai.StateTimer > 5f)
            {
                ai.StateTimer = 0;
                float angle = transform.Rotation * MathF.PI / 180f + 0.3f;
                TurnToward(ref transform, ref velocity, angle * 180f / MathF.PI, ai.Config.MaxRotationSpeed, dt);
            }
            float cruiseRad = transform.Rotation * MathF.PI / 180f;
            velocity.Acceleration += new Vector2(MathF.Cos(cruiseRad), MathF.Sin(cruiseRad)) * thrust * 0.3f;
        }

        ApplyDamping(ref velocity, 0.99f, dt);

        KeepWithinMap(ref transform, ref velocity, thrust);
    }

    private void UpdatePatrol(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 targetPos, bool hasTarget)
    {
        float thrust = ai.Config.Acceleration;
        // Patrols hunt pirates and defend traders
        if (hasTarget)
        {
            float distToTarget = Vector2.Distance(transform.Position, targetPos);
            var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
            float weaponRange = GetWeaponRange(ai.Config.Weapons);

            if (distToTarget <= weaponRange)
            {
                ai.State = AIState.Attack;
                TurnTowardDirection(ref transform, ref velocity, dirToTarget, ai.Config.MaxRotationSpeed, dt);

                // Strafe while attacking
                var strafeDir = new Vector2(-dirToTarget.Y, dirToTarget.X);
                velocity.Acceleration += strafeDir * thrust * 0.3f *
                    MathF.Sign(MathF.Sin(ai.StateTimer * 0.7f));

                if (distToTarget > ai.Config.EngageDistance)
                    velocity.Acceleration += dirToTarget * thrust * 0.4f;

                TryFireProjectiles(ref transform, ref ai, dirToTarget);
            }
            else
            {
                ai.State = AIState.Chase;
                TurnTowardDirection(ref transform, ref velocity, dirToTarget, ai.Config.MaxRotationSpeed, dt);
                velocity.Acceleration += dirToTarget * thrust * 0.5f;
            }
        }
        else
        {
            // Patrol idle
            ai.State = AIState.Patrol;
            if (ai.StateTimer > 4f)
            {
                ai.StateTimer = 0;
                float angle = transform.Rotation * MathF.PI / 180f + 0.4f;
                TurnToward(ref transform, ref velocity, angle * 180f / MathF.PI, ai.Config.MaxRotationSpeed, dt);
            }
            float patrolRad = transform.Rotation * MathF.PI / 180f;
            velocity.Acceleration += new Vector2(MathF.Cos(patrolRad), MathF.Sin(patrolRad)) * thrust * 0.2f;
        }

        ApplyDamping(ref velocity, 0.98f, dt);
        KeepWithinMap(ref transform, ref velocity, thrust);
    }

    private TargetInfo FindTarget(Entity self, Faction selfFaction,
        Vector2 selfPos, float range, Vector2 playerPos, bool playerAlive)
    {
        Entity? bestTarget = null;
        float bestDist = float.MaxValue;
        Vector2 bestPos = Vector2.Zero;

        if (selfFaction == Faction.Pirate)
        {
            // Pirates target player + traders
            if (playerAlive)
            {
                float distToPlayer = Vector2.Distance(selfPos, playerPos);
                if (distToPlayer < range && distToPlayer < bestDist)
                {
                    bestDist = distToPlayer;
                    bestPos = playerPos;
                }
            }

            // Also look for traders
            World.Query(in _aiEntityQuery, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
            {
                if (entity == self || h.IsDead) return;
                if (ai.Config.Faction != Faction.Trader) return;
                float dist = Vector2.Distance(selfPos, t.Position);
                if (dist < range && dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = t.Position;
                    bestTarget = entity;
                }
            });
        }
        else if (selfFaction == Faction.Patrol)
        {
            // Patrols target pirates
            World.Query(in _aiEntityQuery, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
            {
                if (entity == self || h.IsDead) return;
                if (ai.Config.Faction != Faction.Pirate) return;
                float dist = Vector2.Distance(selfPos, t.Position);
                if (dist < range && dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = t.Position;
                    bestTarget = entity;
                }
            });
        }

        return new TargetInfo(bestPos, bestDist < float.MaxValue, bestTarget);
    }

    private Vector2? FindNearestPirate(Vector2 pos, float range)
    {
        float bestDist = range;
        Vector2? bestPos = null;

        var q = _aiEntityQuery;
        World.Query(in q, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
        {
            if (h.IsDead || ai.Config.Faction != Faction.Pirate) return;
            float dist = Vector2.Distance(pos, t.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = t.Position;
            }
        });

        return bestPos;
    }

    private void UpdateWeaponCooldowns(ref EnemyAI ai)
    {
        var weapons = ai.Config.Weapons;
        if (weapons.Length == 0) return;

        if (ai.WeaponCooldowns == null || ai.WeaponCooldowns.Length != weapons.Length)
            ai.WeaponCooldowns = new float[weapons.Length];

        for (int i = 0; i < ai.WeaponCooldowns.Length; i++)
            ai.WeaponCooldowns[i] -= _dt;
    }

    /// <summary>Whether the ship is facing close enough to the target (~18° cone).</summary>
    private static bool IsFacingTarget(ref Transform transform, Vector2 dirToTarget)
    {
        var facing = FacingDirection(transform.Rotation);
        return Vector2.Dot(facing, dirToTarget) > 0.95f;
    }

    /// <summary>Fires any ready weapons when facing the target.</summary>
    private void TryFireProjectiles(ref Transform transform, ref EnemyAI ai, Vector2 dirToTarget)
    {
        if (ai.Config.Weapons.Length == 0) return;
        if (!IsFacingTarget(ref transform, dirToTarget)) return;

        var facing = FacingDirection(transform.Rotation);
        int weaponCount = ai.Config.Weapons.Length;
        float lateralOffset = weaponCount > 1 ? 6f : 0f;

        for (int i = 0; i < weaponCount; i++)
        {
            if (ai.WeaponCooldowns[i] > 0f) continue;

            var weapon = ai.Config.Weapons[i];
            if (weapon.Damage <= 0f || weapon.FireRate <= 0f ||
                weapon.Range <= 0f || weapon.ProjectileSpeed <= 0f)
                continue;

            ai.WeaponCooldowns[i] = weapon.FireRate;
            float lifetime = CombatHelper.ResolveProjectileLifetime(weapon.Range, weapon.ProjectileSpeed);
            float sideOffset = weaponCount > 1 ? (i == 0 ? -lateralOffset : lateralOffset) : 0f;
            FireProjectile(transform.Position, facing, weapon.Damage, weapon.ProjectileSpeed,
                lifetime, ai.Config.Faction, sideOffset);
        }
    }

    private static Vector2 FacingDirection(float rotationDeg)
    {
        float rad = rotationDeg * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }

    private static float GetWeaponRange(IReadOnlyList<ShipWeaponSpec> weapons)
    {
        float maxRange = 0f;
        for (int i = 0; i < weapons.Count; i++)
            maxRange = MathF.Max(maxRange, weapons[i].Range);

        return maxRange;
    }

    private void FireProjectile(Vector2 origin, Vector2 direction, float damage, float speed,
        float lifetime, Faction faction, float lateralOffset)
    {
        // Offset spawn position slightly ahead of the ship
        var lateral = new Vector2(-direction.Y, direction.X);
        var spawnPos = origin + direction * 20f + lateral * lateralOffset;

        // Color by faction
        var color = faction switch
        {
            Faction.Pirate => new Color3(255, 80, 80),     // Red
            Faction.Patrol => new Color3(80, 200, 255),    // Blue
            Faction.Trader => new Color3(255, 255, 80),    // Yellow
            _ => new Color3(255, 255, 255)
        };

        _pendingProjectiles.Add(new ProjectileSpawn(spawnPos, direction, damage, speed, lifetime, faction, color));
    }

    private void KeepWithinMap(ref Transform transform, ref Velocity velocity, float thrust)
    {
        float margin = 120f;
        float edgeSteer = thrust * 0.9f;

        if (transform.Position.X < margin)
            velocity.Acceleration.X += edgeSteer;
        else if (transform.Position.X > _mapWidth - margin)
            velocity.Acceleration.X -= edgeSteer;

        if (transform.Position.Y < margin)
            velocity.Acceleration.Y += edgeSteer;
        else if (transform.Position.Y > _mapHeight - margin)
            velocity.Acceleration.Y -= edgeSteer;
    }

    private static void ApplyDamping(ref Velocity velocity, float frameMultiplier, float dt)
    {
        if (dt <= 0f || frameMultiplier >= 1f) return;

        float factor = Math.Clamp(1f - frameMultiplier, 0f, 1f);
        velocity.Acceleration += -velocity.Velocity * (factor / dt);
    }

    /// <summary>
    /// Smoothly turn toward a desired angle using rotation velocity, clamped by maxRotSpeed.
    /// Sets velocity.RotationVelocity; VelocitySystem applies the actual rotation.
    /// </summary>
    private static void TurnToward(ref Transform transform, ref Velocity velocity,
        float desiredAngleDeg, float maxRotSpeed, float dt)
    {
        float diff = desiredAngleDeg - transform.Rotation;
        // Normalize to [-180, 180]
        diff = ((diff % 360f) + 540f) % 360f - 180f;
        // Set rotation velocity, clamped by max rotation speed
        velocity.RotationVelocity = Math.Clamp(diff / dt, -maxRotSpeed, maxRotSpeed);
    }

    /// <summary>
    /// Smoothly turn toward a direction vector using rotation velocity.
    /// </summary>
    private static void TurnTowardDirection(ref Transform transform, ref Velocity velocity,
        Vector2 direction, float maxRotSpeed, float dt)
    {
        float desiredAngle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
        TurnToward(ref transform, ref velocity, desiredAngle, maxRotSpeed, dt);
    }
}
