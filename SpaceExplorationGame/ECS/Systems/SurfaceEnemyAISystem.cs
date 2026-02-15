using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// AI behavior system for surface enemies (fauna and bandits).
/// Walk-based movement (no rotation physics) with simple chase/attack logic.
/// </summary>
public class SurfaceEnemyAISystem
{
    private readonly World _world;
    private readonly Func<Vector2> _getPlayerPosition;
    private readonly Func<bool> _isPlayerAlive;
    private readonly Func<Vector2, bool>? _canMoveTo;

    // Projectiles spawned this frame (created after query completes to avoid mutation during iteration)
    private readonly List<(Vector2 Pos, Vector2 Dir, float Damage, float Speed, Faction Faction,
        byte R, byte G, byte B, float Lifetime)> _pendingProjectiles = [];

    public SurfaceEnemyAISystem(World world, Func<Vector2> getPlayerPosition,
        Func<bool> isPlayerAlive, Func<Vector2, bool>? canMoveTo = null)
    {
        _world = world;
        _getPlayerPosition = getPlayerPosition;
        _isPlayerAlive = isPlayerAlive;
        _canMoveTo = canMoveTo;
    }

    public void Update(float dt)
    {
        _pendingProjectiles.Clear();

        var playerPos = _getPlayerPosition();
        bool playerAlive = _isPlayerAlive();

        var query = new QueryDescription().WithAll<Transform, Velocity, SurfaceAI, Health>();
        _world.Query(in query, (Entity entity, ref Transform transform, ref Velocity velocity,
            ref SurfaceAI ai, ref Health health) =>
        {
            if (health.IsDead) return;

            ai.StateTimer += dt;
            ai.FireCooldown -= dt;

            float distToPlayer = Vector2.Distance(transform.Position, playerPos);

            switch (ai.Faction)
            {
                case Faction.Fauna:
                    UpdateFauna(ref transform, ref velocity, ref ai, dt, playerPos, playerAlive, distToPlayer);
                    break;
                case Faction.Bandit:
                    UpdateBandit(ref transform, ref velocity, ref ai, ref health, dt, playerPos, playerAlive, distToPlayer);
                    break;
            }
        });

        // Spawn pending projectiles
        foreach (var (pos, dir, damage, speed, faction, r, g, b, lifetime) in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(_world, pos, dir, damage, speed, faction, r, g, b, lifetime);
        }
    }

    private void UpdateFauna(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        float dt, Vector2 playerPos, bool playerAlive, float dist)
    {
        if (playerAlive && dist < ai.DetectRange)
        {
            // Chase player
            ai.State = AIState.Chase;
            var dir = Vector2.Normalize(playerPos - transform.Position);
            if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

            var newPos = transform.Position + dir * ai.MoveSpeed * dt;
            if (_canMoveTo == null || _canMoveTo(newPos))
                transform.Position = newPos;

            velocity.Value = Vector2.Zero; // movement is direct, not physics-based

            // Melee attack: fast short-range projectile when close
            if (dist < ai.AttackRange && ai.FireCooldown <= 0)
            {
                ai.FireCooldown = ai.FireRate;
                _pendingProjectiles.Add((transform.Position, dir, ai.WeaponDamage,
                    ai.ProjectileSpeed, Faction.Fauna, 200, 60, 60, 0.1f));
            }
        }
        else
        {
            // Wander randomly
            ai.State = AIState.Idle;
            ai.WanderTimer -= dt;
            if (ai.WanderTimer <= 0)
            {
                ai.WanderTimer = 2f + MathF.Abs(MathF.Sin(transform.Position.X * 0.1f)) * 3f;
                ai.WanderAngle += MathF.PI * (0.5f + MathF.Abs(MathF.Sin(transform.Position.Y * 0.13f)));
            }

            float wanderSpeed = ai.MoveSpeed * 0.3f;
            var wanderDir = new Vector2(MathF.Cos(ai.WanderAngle), MathF.Sin(ai.WanderAngle));
            var newPos = transform.Position + wanderDir * wanderSpeed * dt;
            if (_canMoveTo == null || _canMoveTo(newPos))
                transform.Position = newPos;

            velocity.Value = Vector2.Zero;
        }
    }

    private void UpdateBandit(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        ref Health health, float dt, Vector2 playerPos, bool playerAlive, float dist)
    {
        float hullPercent = health.HullPercent;

        // Flee if very low health
        if (hullPercent < 0.15f && playerAlive && dist < ai.DetectRange)
        {
            ai.State = AIState.Flee;
            var fleeDir = Vector2.Normalize(transform.Position - playerPos);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);

            var newPos = transform.Position + fleeDir * ai.MoveSpeed * 1.2f * dt;
            if (_canMoveTo == null || _canMoveTo(newPos))
                transform.Position = newPos;

            velocity.Value = Vector2.Zero;
            return;
        }

        if (playerAlive && dist < ai.DetectRange)
        {
            var dir = Vector2.Normalize(playerPos - transform.Position);
            if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

            if (dist > ai.AttackRange * 0.7f)
            {
                // Close distance
                ai.State = AIState.Chase;
                var newPos = transform.Position + dir * ai.MoveSpeed * dt;
                if (_canMoveTo == null || _canMoveTo(newPos))
                    transform.Position = newPos;
            }
            else
            {
                // In range — strafe slightly
                ai.State = AIState.Attack;
                var strafe = new Vector2(-dir.Y, dir.X) * MathF.Sin(ai.StateTimer * 2f) * ai.MoveSpeed * 0.3f * dt;
                var newPos = transform.Position + strafe;
                if (_canMoveTo == null || _canMoveTo(newPos))
                    transform.Position = newPos;
            }

            // Fire ranged weapon
            if (dist < ai.AttackRange && ai.FireCooldown <= 0)
            {
                ai.FireCooldown = ai.FireRate;
                _pendingProjectiles.Add((transform.Position, dir, ai.WeaponDamage,
                    ai.ProjectileSpeed, Faction.Bandit, 255, 150, 50, GameConfig.AvatarProjectileLifetime));
            }

            velocity.Value = Vector2.Zero;
        }
        else
        {
            // Patrol / wander
            ai.State = AIState.Patrol;
            ai.WanderTimer -= dt;
            if (ai.WanderTimer <= 0)
            {
                ai.WanderTimer = 3f + MathF.Abs(MathF.Sin(transform.Position.X * 0.07f)) * 4f;
                ai.WanderAngle += MathF.PI * (0.3f + MathF.Abs(MathF.Cos(transform.Position.Y * 0.09f)));
            }

            float wanderSpeed = ai.MoveSpeed * 0.4f;
            var wanderDir = new Vector2(MathF.Cos(ai.WanderAngle), MathF.Sin(ai.WanderAngle));
            var newPos = transform.Position + wanderDir * wanderSpeed * dt;
            if (_canMoveTo == null || _canMoveTo(newPos))
                transform.Position = newPos;

            velocity.Value = Vector2.Zero;
        }
    }
}
