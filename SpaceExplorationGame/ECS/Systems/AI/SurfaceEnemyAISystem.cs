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
public partial class SurfaceEnemyAISystem : BaseSystem<World, float>
{
    private readonly Func<Vector2> _getPlayerPosition;
    private readonly Func<bool> _isPlayerAlive;
    private readonly Func<Vector2, bool>? _canMoveTo;

    // Projectiles spawned this frame (created after query completes to avoid mutation during iteration)
    private readonly List<SurfaceProjectileSpawn> _pendingProjectiles = [];

    // Per-frame cached state for [Query] method access
    private float _dt;
    private Vector2 _playerPos;
    private bool _playerAlive;

    public SurfaceEnemyAISystem(World world, Func<Vector2> getPlayerPosition,
        Func<bool> isPlayerAlive, Func<Vector2, bool>? canMoveTo = null)
        : base(world)
    {
        _getPlayerPosition = getPlayerPosition;
        _isPlayerAlive = isPlayerAlive;
        _canMoveTo = canMoveTo;
    }

    public override void Update(in float dt)
    {
        _pendingProjectiles.Clear();
        _dt = dt;
        _playerPos = _getPlayerPosition();
        _playerAlive = _isPlayerAlive();

        ProcessSurfaceAIQuery(World);

        // Spawn pending projectiles
        foreach (var (pos, dir, damage, speed, faction, color, lifetime) in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(World, pos, dir, damage, speed, faction, color, lifetime);
        }
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
            ai.State = AIState.Chase;
            var dir = Vector2.Normalize(_playerPos - transform.Position);
            if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

            SetVelocityWithCollision(ref transform, ref velocity, dir * ai.Config.MoveSpeed);

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
            ai.State = AIState.Idle;
            ai.WanderTimer -= _dt;
            if (ai.WanderTimer <= 0)
            {
                ai.WanderTimer = 2f + MathF.Abs(MathF.Sin(transform.Position.X * 0.1f)) * 3f;
                ai.WanderAngle += MathF.PI * (0.5f + MathF.Abs(MathF.Sin(transform.Position.Y * 0.13f)));
            }

            float wanderSpeed = ai.Config.MoveSpeed * 0.3f;
            var wanderDir = new Vector2(MathF.Cos(ai.WanderAngle), MathF.Sin(ai.WanderAngle));
            SetVelocityWithCollision(ref transform, ref velocity, wanderDir * wanderSpeed);
        }
    }

    private void UpdateBandit(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        ref Health health, float dist)
    {
        float hullPercent = health.HullPercent;

        // Flee if very low health
        if (hullPercent < 0.15f && _playerAlive && dist < ai.Config.DetectRange)
        {
            ai.State = AIState.Flee;
            var fleeDir = Vector2.Normalize(transform.Position - _playerPos);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);

            SetVelocityWithCollision(ref transform, ref velocity, fleeDir * ai.Config.MoveSpeed * 1.2f);
            return;
        }

        if (_playerAlive && dist < ai.Config.DetectRange)
        {
            var dir = Vector2.Normalize(_playerPos - transform.Position);
            if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

            if (dist > ai.Config.AttackRange * 0.7f)
            {
                // Close distance
                ai.State = AIState.Chase;
                SetVelocityWithCollision(ref transform, ref velocity, dir * ai.Config.MoveSpeed);
            }
            else
            {
                // In range — strafe slightly
                ai.State = AIState.Attack;
                var strafeDir = new Vector2(-dir.Y, dir.X) * MathF.Sin(ai.StateTimer * 2f);
                SetVelocityWithCollision(ref transform, ref velocity, strafeDir * ai.Config.MoveSpeed * 0.3f);
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
            ai.State = AIState.Patrol;
            ai.WanderTimer -= _dt;
            if (ai.WanderTimer <= 0)
            {
                ai.WanderTimer = 3f + MathF.Abs(MathF.Sin(transform.Position.X * 0.07f)) * 4f;
                ai.WanderAngle += MathF.PI * (0.3f + MathF.Abs(MathF.Cos(transform.Position.Y * 0.09f)));
            }

            float wanderSpeed = ai.Config.MoveSpeed * 0.4f;
            var wanderDir = new Vector2(MathF.Cos(ai.WanderAngle), MathF.Sin(ai.WanderAngle));
            SetVelocityWithCollision(ref transform, ref velocity, wanderDir * wanderSpeed);
        }
    }

    /// <summary>
    /// Set velocity for VelocitySystem, with a pre-check collision test.
    /// If the predicted next position is blocked, velocity is zeroed.
    /// </summary>
    private void SetVelocityWithCollision(ref Transform transform, ref Velocity velocity,
        Vector2 desiredVelocity)
    {
        if (_canMoveTo != null)
        {
            var nextPos = transform.Position + desiredVelocity * _dt;
            if (!_canMoveTo(nextPos))
            {
                velocity.Value = Vector2.Zero;
                return;
            }
        }
        velocity.Value = desiredVelocity;
    }
}
