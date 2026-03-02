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
public partial class ShipEnemyAISystem : BaseSystem<World, float>
{
    private const float TargetMemoryDuration = 2.25f;
    private const float MinFleeStateDuration = 1.25f;
    private const float AttackEnterRangeFactor = 0.92f;
    private const float AttackExitRangeFactor = 1.08f;

    private readonly float _mapWidth;
    private readonly float _mapHeight;

    // Cached query description for nested target/pirate lookups
    private static readonly QueryDescription _aiEntityQuery = new QueryDescription().WithAll<Transform, Velocity, EnemyAI, Health>();
    private static readonly QueryDescription _playerShipQuery = new QueryDescription().WithAll<PlayerControlled, Transform, Velocity, Health, ShipComponent>();

    // Per-frame cached state for [Query] method access
    private float _dt;
    private Vector2 _playerPos;
    private Vector2 _playerVelocity;
    private bool _playerAlive;

    private readonly record struct TargetSelection(Vector2 Position, Vector2 Velocity, bool HasTarget);

    public ShipEnemyAISystem(World world, float mapWidth, float mapHeight)
        : base(world)
    {
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
    }

    public override void Update(in float dt)
    {
        _dt = dt;
        QueryPlayerState();

        ProcessEnemyAIQuery(World);
    }

    private void QueryPlayerState()
    {
        _playerAlive = false;
        _playerPos = Vector2.Zero;
        _playerVelocity = Vector2.Zero;

        var q = _playerShipQuery;
        World.Query(in q, (ref Transform transform, ref Velocity velocity, ref Health health) =>
        {
            _playerPos = transform.Position;
            _playerVelocity = velocity.Linear;
            _playerAlive = !health.IsDead;
        });
    }

    /// <summary>Source-generated query: iterates all NPC ships with AI.</summary>
    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(EnemyAI), typeof(Health), typeof(ShipInputComponent), typeof(ShipComponent))]
    public void ProcessEnemyAI(Entity entity, ref Transform transform,
        ref Velocity velocity, ref EnemyAI ai, ref Health health, ref ShipInputComponent shipInput, ref ShipComponent ship)
    {
        if (health.IsDead)
        {
            ai.HasCruiseTarget = false;
            ai.LastKnownTargetTimeLeft = 0f;
            return;
        }

        // Ships that are warping in/out don't run AI
        if (World.Has<WarpEffect>(entity))
        {
            shipInput.AccelerationDirection = Vector2.Zero;
            shipInput.RotationSpeed = 0f;
            shipInput.Shoot = false;
            return;
        }

        ai.StateTimer += _dt;
        shipInput.AccelerationDirection = Vector2.Zero;
        shipInput.RotationSpeed = 0f;
        shipInput.Shoot = false;

        // Find the best target based on faction
        var liveTarget = FindTarget(entity, ai.Config.Faction, transform.Position,
            ai.Config.DetectRange, _playerPos, _playerVelocity, _playerAlive);
        var target = ResolveTargetWithMemory(ref ai, liveTarget);

        UpdateShipAIByFaction(ref transform, ref ai, ref health, ref shipInput, ref ship, _dt,
            velocity.Linear, target.Position, target.Velocity, target.HasTarget);
    }

    private void UpdateShipAIByFaction(ref Transform transform, ref EnemyAI ai,
        ref Health health, ref ShipInputComponent shipInput, ref ShipComponent ship,
        float dt, Vector2 selfVelocity, Vector2 targetPos, Vector2 targetVelocity, bool hasTarget)
    {
        switch (ai.Config.Faction)
        {
            case Faction.Pirate:
                UpdatePirate(ref transform, ref ai, ref health, ref shipInput, ref ship, selfVelocity, targetPos, targetVelocity, hasTarget);
                break;
            case Faction.Trader:
                UpdateTrader(ref transform, ref ai, ref shipInput, ref ship, dt);
                break;
            case Faction.Patrol:
                UpdatePatrol(ref transform, ref ai, ref shipInput, ref ship, dt,
                    selfVelocity, targetPos, targetVelocity, hasTarget);
                break;
        }
    }

    private void UpdatePirate(ref Transform transform, ref EnemyAI ai,
        ref Health health, ref ShipInputComponent shipInput, ref ShipComponent ship,
        Vector2 selfVelocity, Vector2 targetPos, Vector2 targetVelocity, bool hasTarget)
    {
        // Flee if low health
        bool keepFleeing = ai.State == AIState.Flee && ai.StateTimer < MinFleeStateDuration;
        if (health.HullPercent < ai.Config.FleeHealthPercent || keepFleeing)
        {
            var fleeFrom = hasTarget ? targetPos : transform.Position - FacingDirection(transform.Rotation);
            ApplyFleeBehavior(ref transform, ref ai, ref shipInput, ship.MaxRotationSpeed, _dt,
                fleeFrom, thrustMultiplier: 0.5f);
            return;
        }

        if (!hasTarget)
        {
            // Patrol: drift slowly in a pseudo-random direction
            ApplyCruiseBehavior(ref transform, ref ai, ref shipInput, ship.MaxRotationSpeed, _dt,
                thrustMultiplier: 0.2f);
            return;
        }

        ApplyCombatBehavior(ref transform, ref ai, ref shipInput, ref ship, _dt, selfVelocity, targetPos, targetVelocity,
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

    private void UpdateTrader(ref Transform transform, ref EnemyAI ai, ref ShipInputComponent shipInput, ref ShipComponent ship, float dt)
    {
        // Traders mostly just cruise around. They don't attack but will flee from nearby pirates.
        var nearestPirate = FindNearestPirate(transform.Position, 400f);

        if (nearestPirate.HasValue)
        {
            ApplyFleeBehavior(ref transform, ref ai, ref shipInput, ship.MaxRotationSpeed, dt,
                nearestPirate.Value, thrustMultiplier: 0.7f);
        }
        else
        {
            ApplyCruiseBehavior(ref transform, ref ai, ref shipInput, ship.MaxRotationSpeed, dt,
                thrustMultiplier: 0.3f);
        }
    }

    private void UpdatePatrol(ref Transform transform, ref EnemyAI ai, ref ShipInputComponent shipInput, ref ShipComponent ship,
        float dt, Vector2 selfVelocity, Vector2 targetPos, Vector2 targetVelocity, bool hasTarget)
    {
        // Patrols hunt pirates and defend traders
        if (hasTarget)
        {
            ApplyCombatBehavior(ref transform, ref ai, ref shipInput, ref ship, dt, selfVelocity, targetPos, targetVelocity,
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
            ApplyCruiseBehavior(ref transform, ref ai, ref shipInput, ship.MaxRotationSpeed, dt,
                thrustMultiplier: 0.2f);
        }
    }

    private static void ApplyFleeBehavior(ref Transform transform, ref EnemyAI ai,
        ref ShipInputComponent shipInput, float maxRotationSpeed, float dt,
        Vector2 threatPosition, float thrustMultiplier)
    {
        SetState(ref ai, AIState.Flee);
        var fleeDir = Vector2.Normalize(transform.Position - threatPosition);
        if (float.IsNaN(fleeDir.X))
            fleeDir = FacingDirection(transform.Rotation);

        shipInput.AccelerationDirection += fleeDir * thrustMultiplier;
        shipInput.RotationSpeed = ComputeWantedRotationSpeed(transform.Rotation, fleeDir, dt, maxRotationSpeed);
    }

    private void ApplyCruiseBehavior(ref Transform transform,
        ref EnemyAI ai, ref ShipInputComponent shipInput,
        float maxRotationSpeed, float dt, float thrustMultiplier)
    {
        SetState(ref ai, AIState.Patrol);

        var targetPos = ai.CruiseTarget;
        if (!ai.HasCruiseTarget ||
            !IsInsideCruiseBounds(targetPos) ||
            Vector2.Distance(transform.Position, targetPos) < 180f)
        {
            targetPos = PickRandomCruiseTarget(transform.Position, minDistance: 350f);
            ai.CruiseTarget = targetPos;
            ai.HasCruiseTarget = true;
        }

        var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
        if (float.IsNaN(dirToTarget.X))
            dirToTarget = FacingDirection(transform.Rotation);

        shipInput.RotationSpeed = ComputeWantedRotationSpeed(transform.Rotation, dirToTarget, dt, maxRotationSpeed);
        shipInput.AccelerationDirection += dirToTarget * thrustMultiplier;
    }

    private bool IsInsideCruiseBounds(Vector2 pos)
    {
        const float edgePadding = 128f;
        float minX = edgePadding;
        float minY = edgePadding;
        float maxX = MathF.Max(minX, _mapWidth - edgePadding);
        float maxY = MathF.Max(minY, _mapHeight - edgePadding);

        return pos.X >= minX && pos.X <= maxX && pos.Y >= minY && pos.Y <= maxY;
    }

    private Vector2 PickRandomCruiseTarget(Vector2 currentPos, float minDistance)
    {
        const float edgePadding = 128f;
        if (_mapWidth <= edgePadding * 2f || _mapHeight <= edgePadding * 2f)
            return currentPos;

        float minX = edgePadding;
        float minY = edgePadding;
        float maxX = _mapWidth - edgePadding;
        float maxY = _mapHeight - edgePadding;

        for (int i = 0; i < 8; i++)
        {
            var candidate = new Vector2(
                Random.Shared.NextSingle() * (maxX - minX) + minX,
                Random.Shared.NextSingle() * (maxY - minY) + minY);

            if (Vector2.Distance(currentPos, candidate) >= minDistance)
                return candidate;
        }

        return new Vector2(_mapWidth * 0.5f, _mapHeight * 0.5f);
    }

    private static void ApplyCombatBehavior(ref Transform transform, ref EnemyAI ai,
        ref ShipInputComponent shipInput, ref ShipComponent ship,
        float dt, Vector2 selfVelocity, Vector2 targetPos, Vector2 targetVelocity, float chaseThrustMultiplier,
        float strafeThrustMultiplier,
        float strafeFrequency, bool maintainEngageBand, float closeThresholdMultiplier,
        float farThresholdMultiplier, float backoffThrustMultiplier,
        float closeInThrustMultiplier, bool closeInWhenOutsideEngageDistance)
    {
        float distToTarget = Vector2.Distance(transform.Position, targetPos);
        var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
        if (float.IsNaN(dirToTarget.X))
            dirToTarget = FacingDirection(transform.Rotation);

        float weaponRange = GetWeaponRange(ship.Weapons);
        bool inAttackRange = ai.State switch
        {
            AIState.Attack => distToTarget <= weaponRange * AttackExitRangeFactor,
            _ => distToTarget <= weaponRange * AttackEnterRangeFactor
        };

        if (inAttackRange)
        {
            SetState(ref ai, AIState.Attack);
            var aimDir = ComputeAimDirection(transform.Position, targetPos, targetVelocity, selfVelocity,
                GetFastestProjectileSpeed(ship.Weapons), dirToTarget);
            shipInput.RotationSpeed = ComputeWantedRotationSpeed(transform.Rotation, aimDir, dt, ship.MaxRotationSpeed);

            if (maintainEngageBand)
            {
                if (distToTarget < ai.Config.EngageDistance * closeThresholdMultiplier)
                {
                    shipInput.AccelerationDirection -= dirToTarget * backoffThrustMultiplier;
                }
                else if (distToTarget > ai.Config.EngageDistance * farThresholdMultiplier)
                {
                    shipInput.AccelerationDirection += dirToTarget * closeInThrustMultiplier;
                }
                else if (strafeThrustMultiplier > 0f)
                {
                    ApplyStrafe(ref shipInput, ref ai, dirToTarget, strafeThrustMultiplier, strafeFrequency);
                }
            }
            else
            {
                if (strafeThrustMultiplier > 0f)
                    ApplyStrafe(ref shipInput, ref ai, dirToTarget, strafeThrustMultiplier, strafeFrequency);

                if (closeInWhenOutsideEngageDistance && distToTarget > ai.Config.EngageDistance)
                    shipInput.AccelerationDirection += dirToTarget * closeInThrustMultiplier;
            }

            shipInput.Shoot = true;
        }
        else
        {
            SetState(ref ai, AIState.Chase);
            shipInput.RotationSpeed = ComputeWantedRotationSpeed(transform.Rotation, dirToTarget, dt, ship.MaxRotationSpeed);
            shipInput.AccelerationDirection += dirToTarget * chaseThrustMultiplier;
        }
    }

    private static void ApplyStrafe(ref ShipInputComponent shipInput, ref EnemyAI ai, Vector2 dirToTarget,
        float strafeThrustMultiplier, float strafeFrequency)
    {
        var strafeDir = new Vector2(-dirToTarget.Y, dirToTarget.X);
        shipInput.AccelerationDirection += strafeDir * strafeThrustMultiplier *
            MathF.Sign(MathF.Sin(ai.StateTimer * strafeFrequency));
    }

    private static float ComputeWantedRotationSpeed(float currentRotation, Vector2 targetDirection,
        float dt, float maxRotationSpeed)
    {
        if (targetDirection == Vector2.Zero || dt <= 0f)
            return 0f;

        float targetRotation = MathF.Atan2(targetDirection.Y, targetDirection.X) * 180f / MathF.PI;
        float delta = targetRotation - currentRotation;
        delta = ((delta % 360f) + 540f) % 360f - 180f;
        float requiredRotationSpeed = delta / dt;
        return Math.Clamp(requiredRotationSpeed, -maxRotationSpeed, maxRotationSpeed);
    }

    private TargetSelection ResolveTargetWithMemory(ref EnemyAI ai, TargetSelection liveTarget)
    {
        if (liveTarget.HasTarget)
        {
            ai.LastKnownTargetPos = liveTarget.Position;
            ai.LastKnownTargetVelocity = liveTarget.Velocity;
            ai.LastKnownTargetTimeLeft = TargetMemoryDuration;
            return liveTarget;
        }

        if (ai.LastKnownTargetTimeLeft <= 0f)
            return default;

        ai.LastKnownTargetTimeLeft -= _dt;
        ai.LastKnownTargetPos += ai.LastKnownTargetVelocity * _dt;

        if (ai.LastKnownTargetTimeLeft <= 0f)
        {
            ai.LastKnownTargetTimeLeft = 0f;
            return default;
        }

        return new TargetSelection(ai.LastKnownTargetPos, ai.LastKnownTargetVelocity, true);
    }

    private TargetSelection FindTarget(Entity self, Faction selfFaction,
        Vector2 selfPos, float range, Vector2 playerPos, Vector2 playerVelocity, bool playerAlive)
    {
        Entity? bestTarget = null;
        float bestDist = float.MaxValue;
        Vector2 bestPos = Vector2.Zero;
        Vector2 bestVelocity = Vector2.Zero;

        // Optional player target (virtual target; no entity handle)
        if (playerAlive && ShouldTargetPlayer(selfFaction))
            TrySelectTarget(playerPos, playerVelocity, null, selfPos, range,
                ref bestDist, ref bestPos, ref bestVelocity, ref bestTarget);

        // Ship-vs-ship target selection policy
        World.Query(in _aiEntityQuery, (Entity entity, ref Transform t, ref Velocity v, ref EnemyAI ai, ref Health h) =>
        {
            if (entity == self || h.IsDead) return;
            if (World.Has<WarpEffect>(entity)) return; // warping ships are not valid targets
            if (!ShouldTargetFaction(selfFaction, ai.Config.Faction)) return;

            TrySelectTarget(t.Position, v.Linear, entity, selfPos, range,
                ref bestDist, ref bestPos, ref bestVelocity, ref bestTarget);
        });

        return new TargetSelection(bestPos, bestVelocity, bestDist < float.MaxValue);
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

    private static void TrySelectTarget(Vector2 candidatePos, Vector2 candidateVelocity,
        Entity? candidateEntity, Vector2 selfPos, float range, ref float bestDist,
        ref Vector2 bestPos, ref Vector2 bestVelocity, ref Entity? bestTarget)
    {
        float dist = Vector2.Distance(selfPos, candidatePos);
        if (dist >= range || dist >= bestDist)
            return;

        bestDist = dist;
        bestPos = candidatePos;
        bestVelocity = candidateVelocity;
        bestTarget = candidateEntity;
    }

    private Vector2? FindNearestPirate(Vector2 pos, float range)
    {
        float bestDist = range;
        Vector2? bestPos = null;

        World.Query(in _aiEntityQuery, (Entity entity, ref Transform t, ref Velocity v, ref EnemyAI ai, ref Health h) =>
        {
            if (h.IsDead || ai.Config.Faction != Faction.Pirate) return;
            if (World.Has<WarpEffect>(entity)) return; // warping ships are not targetable
            float dist = Vector2.Distance(pos, t.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = t.Position;
            }
        });

        return bestPos;
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

    private static float GetFastestProjectileSpeed(IReadOnlyList<ShipWeaponSpec> weapons)
    {
        float maxSpeed = 0f;
        for (int i = 0; i < weapons.Count; i++)
            maxSpeed = MathF.Max(maxSpeed, weapons[i].ProjectileSpeed);

        return maxSpeed;
    }

    private static Vector2 ComputeAimDirection(Vector2 shooterPos, Vector2 targetPos,
        Vector2 targetVelocity, Vector2 shooterVelocity, float projectileSpeed, Vector2 fallbackDirection)
    {
        if (projectileSpeed <= 0f)
            return fallbackDirection;

        var toTarget = targetPos - shooterPos;
        float dist = toTarget.Length();
        if (dist <= 0.001f)
            return fallbackDirection;

        float leadTime = Math.Clamp(dist / projectileSpeed, 0f, 1.5f);
        var relativeTargetVelocity = targetVelocity - shooterVelocity;
        var predictedPos = targetPos + relativeTargetVelocity * leadTime;
        var aimDir = Vector2.Normalize(predictedPos - shooterPos);
        if (float.IsNaN(aimDir.X))
            return fallbackDirection;

        return aimDir;
    }

    private static void SetState(ref EnemyAI ai, AIState newState)
    {
        if (ai.State == newState)
            return;

        ai.State = newState;
        ai.StateTimer = 0f;
    }
}
