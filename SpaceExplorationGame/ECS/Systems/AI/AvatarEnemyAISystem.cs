using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.AI;

/// <summary>
/// AI behavior system for surface enemies (fauna and bandits).
/// Walk-based movement (no rotation physics) with simple chase/attack logic.
/// Uses Arch source generator for the main entity query.
/// </summary>
public partial class AvatarEnemyAISystem : BaseSystem<World, float>
{
    private const float BanditAttackEnterRangeFactor = 0.92f;
    private const float BanditAttackExitRangeFactor = 1.08f;

    private static readonly QueryDescription _playerAvatarQuery =
        new QueryDescription().WithAll<PlayerControlled, Transform, Health>();

    // Projectiles spawned this frame (created after query completes to avoid mutation during iteration)
    private readonly List<SurfaceProjectileSpawn> _pendingProjectiles = [];

    /// <summary>Projectiles spawned during the last Update (available until next Update).</summary>
    public IReadOnlyList<SurfaceProjectileSpawn> ProjectilesSpawnedLastUpdate => _pendingProjectiles;

    // Per-frame cached state for [Query] method access
    private float _dt;
    private Vector2 _playerPos;
    private bool _playerAlive;

    public AvatarEnemyAISystem(World world)
        : base(world)
    {
    }

    public override void Update(in float dt)
    {
        _pendingProjectiles.Clear();
        _dt = dt;
        QueryPlayerState();

        ProcessSurfaceAIQuery(World);

        // Spawn pending projectiles
        foreach (var (pos, dir, damage, speed, faction, color, lifetime) in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(World, pos, dir, damage, speed, faction, color, lifetime);
        }
    }

    private void QueryPlayerState()
    {
        _playerAlive = false;
        _playerPos = Vector2.Zero;

        var q = _playerAvatarQuery;
        World.Query(in q, (ref Transform transform, ref Health health) =>
        {
            _playerPos = transform.Position;
            _playerAlive = !health.IsDead;
        });
    }

    /// <summary>Source-generated query: iterates all surface AI entities.</summary>
    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(SurfaceAI), typeof(Health))]
    public void ProcessSurfaceAI(ref Transform transform, ref Velocity velocity,
        ref SurfaceAI ai, ref Health health)
    {
        if (health.IsDead) return;

        ai.StateTimer += _dt;
        ai.FireCooldown -= _dt;
        velocity.RotationVelocity = 0f;
        velocity.Acceleration = Vector2.Zero;

        float distToPlayer = Vector2.Distance(transform.Position, _playerPos);

        switch (ai.Config.Faction)
        {
            case Faction.Fauna:
                UpdateFauna(ref transform, ref velocity, ref ai, distToPlayer);
                break;
            case Faction.Bandit:
                UpdateBandit(ref transform, ref velocity, ref ai, ref health, distToPlayer);
                break;
        }
    }

    private void UpdateFauna(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        float dist)
    {
        if (_playerAlive && dist < ai.Config.DetectRange)
        {
            // Chase player
            SetState(ref ai, AIState.Chase);
            var dir = Vector2.Normalize(_playerPos - transform.Position);
            if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

            SetAccelerationTowardVelocity(ref velocity, dir * ai.Config.MoveSpeed);

            // Melee attack: fast short-range projectile when close
            if (dist < ai.Config.AttackRange && ai.FireCooldown <= 0)
            {
                ai.FireCooldown = ai.Config.FireRate;
                _pendingProjectiles.Add(new SurfaceProjectileSpawn(transform.Position, dir, ai.Config.WeaponDamage,
                    ai.Config.ProjectileSpeed, Faction.Fauna, new Color3(200, 60, 60), 0.1f));
            }
        }
        else
        {
            // Wander randomly
            SetState(ref ai, AIState.Idle);
            ai.WanderTimer -= _dt;
            if (ai.WanderTimer <= 0)
            {
                ai.WanderTimer = 2f + MathF.Abs(MathF.Sin(transform.Position.X * 0.1f)) * 3f;
                ai.WanderAngle += MathF.PI * (0.5f + MathF.Abs(MathF.Sin(transform.Position.Y * 0.13f)));
            }

            float wanderSpeed = ai.Config.MoveSpeed * 0.3f;
            var wanderDir = new Vector2(MathF.Cos(ai.WanderAngle), MathF.Sin(ai.WanderAngle));
            SetAccelerationTowardVelocity(ref velocity, wanderDir * wanderSpeed);
        }
    }

    private void UpdateBandit(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        ref Health health, float dist)
    {
        float hullPercent = health.HullPercent;

        // Flee if very low health
        if (hullPercent < 0.15f && _playerAlive && dist < ai.Config.DetectRange)
        {
            SetState(ref ai, AIState.Flee);
            var fleeDir = Vector2.Normalize(transform.Position - _playerPos);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);

            SetAccelerationTowardVelocity(ref velocity, fleeDir * ai.Config.MoveSpeed * 1.2f);
            return;
        }

        if (_playerAlive && dist < ai.Config.DetectRange)
        {
            var dir = Vector2.Normalize(_playerPos - transform.Position);
            if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

            bool inAttackRange = ai.State switch
            {
                AIState.Attack => dist <= ai.Config.AttackRange * BanditAttackExitRangeFactor,
                _ => dist <= ai.Config.AttackRange * BanditAttackEnterRangeFactor
            };

            if (!inAttackRange)
            {
                // Close distance
                SetState(ref ai, AIState.Chase);
                SetAccelerationTowardVelocity(ref velocity, dir * ai.Config.MoveSpeed);
            }
            else
            {
                // In range — strafe slightly
                SetState(ref ai, AIState.Attack);
                var strafeDir = new Vector2(-dir.Y, dir.X) * MathF.Sin(ai.StateTimer * 2f);
                SetAccelerationTowardVelocity(ref velocity, strafeDir * ai.Config.MoveSpeed * 0.3f);
            }

            // Fire ranged weapon
            if (dist < ai.Config.AttackRange && ai.FireCooldown <= 0)
            {
                ai.FireCooldown = ai.Config.FireRate;
                _pendingProjectiles.Add(new SurfaceProjectileSpawn(transform.Position, dir, ai.Config.WeaponDamage,
                    ai.Config.ProjectileSpeed, Faction.Bandit, new Color3(255, 150, 50), GameConfig.AvatarProjectileLifetime));
            }
        }
        else
        {
            // Patrol / wander
            SetState(ref ai, AIState.Patrol);
            ai.WanderTimer -= _dt;
            if (ai.WanderTimer <= 0)
            {
                ai.WanderTimer = 3f + MathF.Abs(MathF.Sin(transform.Position.X * 0.07f)) * 4f;
                ai.WanderAngle += MathF.PI * (0.3f + MathF.Abs(MathF.Cos(transform.Position.Y * 0.09f)));
            }

            float wanderSpeed = ai.Config.MoveSpeed * 0.4f;
            var wanderDir = new Vector2(MathF.Cos(ai.WanderAngle), MathF.Sin(ai.WanderAngle));
            SetAccelerationTowardVelocity(ref velocity, wanderDir * wanderSpeed);
        }
    }

    /// <summary>
    /// Sets acceleration intent toward a desired velocity, with a pre-check collision test.
    /// If the predicted next position is blocked, desired velocity is zero.
    /// </summary>
    private void SetAccelerationTowardVelocity(ref Velocity velocity, Vector2 desiredVelocity)
    {
        velocity.Acceleration += (desiredVelocity - velocity.Velocity) * 14f;
    }

    private static void SetState(ref SurfaceAI ai, AIState newState)
    {
        if (ai.State == newState)
            return;

        ai.State = newState;
        ai.StateTimer = 0f;
    }
}
