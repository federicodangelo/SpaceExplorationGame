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
        velocity.Damping = 1f;

        // Find the best target based on faction
        var (targetPos, hasTarget, targetEntity) = FindTarget(entity, ai.Config.Faction, transform.Position, ai.Config.DetectRange, _playerPos, _playerAlive);

        UpdateShipAIByFaction(ref transform, ref velocity, ref ai, ref health, _dt, targetPos, hasTarget);
    }

    private void UpdateShipAIByFaction(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 targetPos, bool hasTarget)
    {
        switch (ai.Config.Faction)
        {
            case Faction.Pirate:
                UpdatePirate(ref transform, ref velocity, ref ai, ref health, dt, targetPos, hasTarget);
                break;
            case Faction.Trader:
                UpdateTrader(ref transform, ref velocity, ref ai, dt);
                break;
            case Faction.Patrol:
                UpdatePatrol(ref transform, ref velocity, ref ai, dt, targetPos, hasTarget);
                break;
        }
    }

    private void UpdatePirate(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 targetPos, bool hasTarget)
    {
        // Flee if low health
        if (health.HullPercent < ai.Config.FleeHealthPercent)
        {
            var fleeFrom = hasTarget ? targetPos : transform.Position - FacingDirection(transform.Rotation);
            ApplyFleeBehavior(ref transform, ref velocity, ref ai, dt, fleeFrom, thrustMultiplier: 0.5f);
            return;
        }

        if (!hasTarget)
        {
            // Patrol: drift slowly in a pseudo-random direction
            float angleOffset = (float)(Math.Sin(transform.Position.X * 0.01 + transform.Position.Y * 0.01) * 0.5);
            ApplyCruiseBehavior(ref transform, ref velocity, ref ai, dt,
                turnInterval: 3f,
                turnOffsetRadians: angleOffset,
                thrustMultiplier: 0.2f,
                damping: 0.999f);
            return;
        }

        ApplyCombatBehavior(ref transform, ref velocity, ref ai, dt, targetPos,
            chaseThrustMultiplier: 0.6f,
            strafeThrustMultiplier: 0.3f,
            strafeFrequency: 0.8f,
            maintainEngageBand: true,
            closeThresholdMultiplier: 0.7f,
            farThresholdMultiplier: 1.3f,
            backoffThrustMultiplier: 0.3f,
            closeInThrustMultiplier: 0.5f,
            closeInWhenOutsideEngageDistance: false);
    }

    private void UpdateTrader(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        float dt)
    {
        // Traders mostly just cruise around. They don't attack but will flee from nearby pirates.
        var nearestPirate = FindNearestPirate(transform.Position, 400f);

        if (nearestPirate.HasValue)
        {
            ApplyFleeBehavior(ref transform, ref velocity, ref ai, dt, nearestPirate.Value, thrustMultiplier: 0.7f);
        }
        else
        {
            ApplyCruiseBehavior(ref transform, ref velocity, ref ai, dt,
                turnInterval: 5f,
                turnOffsetRadians: 0.3f,
                thrustMultiplier: 0.3f,
                damping: 1f);
        }
    }

    private void UpdatePatrol(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        float dt, Vector2 targetPos, bool hasTarget)
    {
        // Patrols hunt pirates and defend traders
        if (hasTarget)
        {
            ApplyCombatBehavior(ref transform, ref velocity, ref ai, dt, targetPos,
                chaseThrustMultiplier: 0.5f,
                strafeThrustMultiplier: 0.3f,
                strafeFrequency: 0.7f,
                maintainEngageBand: false,
                closeThresholdMultiplier: 0f,
                farThresholdMultiplier: 0f,
                backoffThrustMultiplier: 0f,
                closeInThrustMultiplier: 0.4f,
                closeInWhenOutsideEngageDistance: true);
        }
        else
        {
            ApplyCruiseBehavior(ref transform, ref velocity, ref ai, dt,
                turnInterval: 4f,
                turnOffsetRadians: 0.4f,
                thrustMultiplier: 0.2f,
                damping: 0.999f);
        }
    }

    private void ApplyFleeBehavior(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        float dt, Vector2 threatPosition, float thrustMultiplier)
    {
        ai.State = AIState.Flee;
        var fleeDir = Vector2.Normalize(transform.Position - threatPosition);
        if (float.IsNaN(fleeDir.X))
            fleeDir = FacingDirection(transform.Rotation);

        velocity.Acceleration += fleeDir * ai.Config.Acceleration * thrustMultiplier;
        TurnTowardDirection(ref transform, ref velocity, fleeDir, ai.Config.MaxRotationSpeed, dt);
    }

    private void ApplyCruiseBehavior(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        float dt, float turnInterval, float turnOffsetRadians, float thrustMultiplier, float damping)
    {
        ai.State = AIState.Patrol;
        if (ai.StateTimer > turnInterval)
        {
            ai.StateTimer = 0f;
            float angle = transform.Rotation * MathF.PI / 180f + turnOffsetRadians;
            TurnToward(ref transform, ref velocity, angle * 180f / MathF.PI, ai.Config.MaxRotationSpeed, dt);
        }

        var facing = FacingDirection(transform.Rotation);
        velocity.Acceleration += facing * ai.Config.Acceleration * thrustMultiplier;
        velocity.Damping = damping;
    }

    private void ApplyCombatBehavior(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        float dt, Vector2 targetPos, float chaseThrustMultiplier, float strafeThrustMultiplier,
        float strafeFrequency, bool maintainEngageBand, float closeThresholdMultiplier,
        float farThresholdMultiplier, float backoffThrustMultiplier,
        float closeInThrustMultiplier, bool closeInWhenOutsideEngageDistance)
    {
        float distToTarget = Vector2.Distance(transform.Position, targetPos);
        var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
        if (float.IsNaN(dirToTarget.X))
            dirToTarget = FacingDirection(transform.Rotation);

        float weaponRange = GetWeaponRange(ai.Config.Weapons);
        if (distToTarget <= weaponRange)
        {
            ai.State = AIState.Attack;
            TurnTowardDirection(ref transform, ref velocity, dirToTarget, ai.Config.MaxRotationSpeed, dt);

            if (maintainEngageBand)
            {
                if (distToTarget < ai.Config.EngageDistance * closeThresholdMultiplier)
                {
                    velocity.Acceleration -= dirToTarget * ai.Config.Acceleration * backoffThrustMultiplier;
                }
                else if (distToTarget > ai.Config.EngageDistance * farThresholdMultiplier)
                {
                    velocity.Acceleration += dirToTarget * ai.Config.Acceleration * closeInThrustMultiplier;
                }
                else if (strafeThrustMultiplier > 0f)
                {
                    ApplyStrafe(ref velocity, ref ai, dirToTarget, strafeThrustMultiplier, strafeFrequency);
                }
            }
            else
            {
                if (strafeThrustMultiplier > 0f)
                    ApplyStrafe(ref velocity, ref ai, dirToTarget, strafeThrustMultiplier, strafeFrequency);

                if (closeInWhenOutsideEngageDistance && distToTarget > ai.Config.EngageDistance)
                    velocity.Acceleration += dirToTarget * ai.Config.Acceleration * closeInThrustMultiplier;
            }

            velocity.Damping = 0.98f;
            TryFireProjectiles(ref transform, ref ai, dirToTarget);
        }
        else
        {
            ai.State = AIState.Chase;
            TurnTowardDirection(ref transform, ref velocity, dirToTarget, ai.Config.MaxRotationSpeed, dt);
            velocity.Acceleration += dirToTarget * ai.Config.Acceleration * chaseThrustMultiplier;
        }
    }

    private static void ApplyStrafe(ref Velocity velocity, ref EnemyAI ai, Vector2 dirToTarget,
        float strafeThrustMultiplier, float strafeFrequency)
    {
        var strafeDir = new Vector2(-dirToTarget.Y, dirToTarget.X);
        velocity.Acceleration += strafeDir * ai.Config.Acceleration * strafeThrustMultiplier *
            MathF.Sign(MathF.Sin(ai.StateTimer * strafeFrequency));
    }

    private TargetInfo FindTarget(Entity self, Faction selfFaction,
        Vector2 selfPos, float range, Vector2 playerPos, bool playerAlive)
    {
        Entity? bestTarget = null;
        float bestDist = float.MaxValue;
        Vector2 bestPos = Vector2.Zero;

        // Optional player target (virtual target; no entity handle)
        if (playerAlive && ShouldTargetPlayer(selfFaction))
            TrySelectTarget(playerPos, null, selfPos, range, ref bestDist, ref bestPos, ref bestTarget);

        // Ship-vs-ship target selection policy
        World.Query(in _aiEntityQuery, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
        {
            if (entity == self || h.IsDead) return;
            if (!ShouldTargetFaction(selfFaction, ai.Config.Faction)) return;

            TrySelectTarget(t.Position, entity, selfPos, range, ref bestDist, ref bestPos, ref bestTarget);
        });

        return new TargetInfo(bestPos, bestDist < float.MaxValue, bestTarget);
    }

    private static bool ShouldTargetPlayer(Faction selfFaction)
    {
        return selfFaction == Faction.Pirate;
    }

    private static bool ShouldTargetFaction(Faction selfFaction, Faction otherFaction)
    {
        return selfFaction switch
        {
            Faction.Pirate => otherFaction == Faction.Trader,
            Faction.Patrol => otherFaction == Faction.Pirate,
            _ => false
        };
    }

    private static void TrySelectTarget(Vector2 candidatePos, Entity? candidateEntity,
        Vector2 selfPos, float range, ref float bestDist, ref Vector2 bestPos, ref Entity? bestTarget)
    {
        float dist = Vector2.Distance(selfPos, candidatePos);
        if (dist >= range || dist >= bestDist)
            return;

        bestDist = dist;
        bestPos = candidatePos;
        bestTarget = candidateEntity;
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
