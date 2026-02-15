using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// AI behavior system for NPC ships (pirates, traders, patrols).
/// Plain class with manual queries — needs access to player position and entity creation for firing.
/// </summary>
public class EnemyAISystem
{
    private readonly World _world;
    private readonly Func<Vector2> _getPlayerPosition;
    private readonly Func<bool> _isPlayerAlive;
    private readonly float _mapWidth;
    private readonly float _mapHeight;

    // Projectiles spawned this frame (to be created after query completes)
    private readonly List<(Vector2 Pos, Vector2 Dir, float Damage, float Speed, Faction Faction, byte R, byte G, byte B)> _pendingProjectiles = [];

    public EnemyAISystem(World world, Func<Vector2> getPlayerPosition, Func<bool> isPlayerAlive,
        float mapWidth, float mapHeight)
    {
        _world = world;
        _getPlayerPosition = getPlayerPosition;
        _isPlayerAlive = isPlayerAlive;
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
    }

    public void Update(float dt)
    {
        _pendingProjectiles.Clear();

        var playerPos = _getPlayerPosition();
        bool playerAlive = _isPlayerAlive();

        var query = new QueryDescription().WithAll<Transform, Velocity, EnemyAI, Health>();
        _world.Query(in query, (Entity entity, ref Transform transform, ref Velocity velocity,
            ref EnemyAI ai, ref Health health) =>
        {
            if (health.IsDead) return;

            ai.StateTimer += dt;
            ai.FireCooldown -= dt;

            // Find the best target based on faction
            var (targetPos, hasTarget, targetEntity) = FindTarget(entity, ai.Faction, transform.Position, ai.DetectRange, playerPos, playerAlive);

            // State machine
            switch (ai.Faction)
            {
                case Faction.Pirate:
                    UpdatePirate(ref transform, ref velocity, ref ai, ref health, dt, playerPos, playerAlive, targetPos, hasTarget);
                    break;
                case Faction.Trader:
                    UpdateTrader(ref transform, ref velocity, ref ai, ref health, dt, playerPos, playerAlive);
                    break;
                case Faction.Patrol:
                    UpdatePatrol(ref transform, ref velocity, ref ai, ref health, dt, targetPos, hasTarget);
                    break;
            }
        });

        // Spawn pending projectiles
        foreach (var (pos, dir, damage, speed, faction, r, g, b) in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(_world, pos, dir, damage, speed, faction, r, g, b);
        }
    }

    private void UpdatePirate(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 playerPos, bool playerAlive, Vector2 targetPos, bool hasTarget)
    {
        float hullPercent = health.HullPercent;

        // Flee if low health
        if (hullPercent < ai.FleeHealthPercent)
        {
            ai.State = AIState.Flee;
            var fleeDir = Vector2.Normalize(transform.Position - targetPos);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
            velocity.Value += fleeDir * GameConfig.PirateSpeed * 0.5f * dt;
            transform.Rotation = MathF.Atan2(fleeDir.Y, fleeDir.X) * 180f / MathF.PI;
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
                transform.Rotation = angle * 180f / MathF.PI;
            }
            float patrolRad = transform.Rotation * MathF.PI / 180f;
            velocity.Value += new Vector2(MathF.Cos(patrolRad), MathF.Sin(patrolRad)) * GameConfig.PirateSpeed * 0.2f * dt;

            // Clamp speed to patrol speed
            if (velocity.Value.LengthSquared() > 100f * 100f)
                velocity.Value = Vector2.Normalize(velocity.Value) * 100f;
            return;
        }

        float distToTarget = Vector2.Distance(transform.Position, targetPos);

        if (distToTarget <= ai.WeaponRange)
        {
            // Attack
            ai.State = AIState.Attack;
            var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
            transform.Rotation = MathF.Atan2(dirToTarget.Y, dirToTarget.X) * 180f / MathF.PI;

            // Maintain engage distance
            if (distToTarget < ai.EngageDistance * 0.7f)
            {
                // Too close — back up slightly
                velocity.Value -= dirToTarget * GameConfig.PirateSpeed * 0.3f * dt;
            }
            else if (distToTarget > ai.EngageDistance * 1.3f)
            {
                // Too far — close in
                velocity.Value += dirToTarget * GameConfig.PirateSpeed * 0.5f * dt;
            }
            else
            {
                // Strafe
                var strafeDir = new Vector2(-dirToTarget.Y, dirToTarget.X);
                velocity.Value += strafeDir * GameConfig.PirateSpeed * 0.3f * dt *
                    MathF.Sign(MathF.Sin(ai.StateTimer * 0.8f));
            }

            // Fire
            if (ai.FireCooldown <= 0)
            {
                ai.FireCooldown = ai.FireRate;
                FireProjectile(transform.Position, dirToTarget, ai.WeaponDamage, ai.ProjectileSpeed, ai.Faction);
            }
        }
        else if (distToTarget <= ai.DetectRange)
        {
            // Chase
            ai.State = AIState.Chase;
            var dirToTarget = Vector2.Normalize(targetPos - transform.Position);
            transform.Rotation = MathF.Atan2(dirToTarget.Y, dirToTarget.X) * 180f / MathF.PI;
            velocity.Value += dirToTarget * GameConfig.PirateSpeed * 0.6f * dt;
        }

        // Apply friction
        velocity.Value *= 0.98f;
    }

    private void UpdateTrader(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 playerPos, bool playerAlive)
    {
        // Traders mostly just cruise around. They don't attack but will flee from nearby pirates.
        var nearestPirate = FindNearestPirate(transform.Position, 400f);

        if (nearestPirate.HasValue)
        {
            // Flee from pirate
            ai.State = AIState.Flee;
            var fleeDir = Vector2.Normalize(transform.Position - nearestPirate.Value);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
            velocity.Value += fleeDir * GameConfig.TraderSpeed * 0.7f * dt;
            transform.Rotation = MathF.Atan2(fleeDir.Y, fleeDir.X) * 180f / MathF.PI;
        }
        else
        {
            // Cruise
            ai.State = AIState.Patrol;
            if (ai.StateTimer > 5f)
            {
                ai.StateTimer = 0;
                float angle = transform.Rotation * MathF.PI / 180f + 0.3f;
                transform.Rotation = angle * 180f / MathF.PI;
            }
            float cruiseRad = transform.Rotation * MathF.PI / 180f;
            velocity.Value += new Vector2(MathF.Cos(cruiseRad), MathF.Sin(cruiseRad)) * GameConfig.TraderSpeed * 0.3f * dt;
        }

        // Clamp speed
        float maxSpd = GameConfig.TraderSpeed;
        if (velocity.Value.LengthSquared() > maxSpd * maxSpd)
            velocity.Value = Vector2.Normalize(velocity.Value) * maxSpd;

        velocity.Value *= 0.99f;

        // Keep within map bounds
        ClampToMap(ref transform);
    }

    private void UpdatePatrol(ref Transform transform, ref Velocity velocity, ref EnemyAI ai,
        ref Health health, float dt, Vector2 targetPos, bool hasTarget)
    {
        // Patrols hunt pirates and defend traders
        if (hasTarget)
        {
            float distToTarget = Vector2.Distance(transform.Position, targetPos);
            var dirToTarget = Vector2.Normalize(targetPos - transform.Position);

            if (distToTarget <= ai.WeaponRange)
            {
                ai.State = AIState.Attack;
                transform.Rotation = MathF.Atan2(dirToTarget.Y, dirToTarget.X) * 180f / MathF.PI;

                // Strafe while attacking
                var strafeDir = new Vector2(-dirToTarget.Y, dirToTarget.X);
                velocity.Value += strafeDir * GameConfig.PatrolSpeed * 0.3f * dt *
                    MathF.Sign(MathF.Sin(ai.StateTimer * 0.7f));

                if (distToTarget > ai.EngageDistance)
                    velocity.Value += dirToTarget * GameConfig.PatrolSpeed * 0.4f * dt;

                if (ai.FireCooldown <= 0)
                {
                    ai.FireCooldown = ai.FireRate;
                    FireProjectile(transform.Position, dirToTarget, ai.WeaponDamage, ai.ProjectileSpeed, ai.Faction);
                }
            }
            else
            {
                ai.State = AIState.Chase;
                transform.Rotation = MathF.Atan2(dirToTarget.Y, dirToTarget.X) * 180f / MathF.PI;
                velocity.Value += dirToTarget * GameConfig.PatrolSpeed * 0.5f * dt;
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
                transform.Rotation = angle * 180f / MathF.PI;
            }
            float patrolRad = transform.Rotation * MathF.PI / 180f;
            velocity.Value += new Vector2(MathF.Cos(patrolRad), MathF.Sin(patrolRad)) * GameConfig.PatrolSpeed * 0.2f * dt;
        }

        // Clamp speed
        float maxSpd = GameConfig.PatrolSpeed;
        if (velocity.Value.LengthSquared() > maxSpd * maxSpd)
            velocity.Value = Vector2.Normalize(velocity.Value) * maxSpd;

        velocity.Value *= 0.98f;
        ClampToMap(ref transform);
    }

    private (Vector2 Position, bool HasTarget, Entity? Entity) FindTarget(Entity self, Faction selfFaction,
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
            var traderQuery = new QueryDescription().WithAll<Transform, EnemyAI, Health>();
            _world.Query(in traderQuery, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
            {
                if (entity == self || h.IsDead) return;
                if (ai.Faction != Faction.Trader) return;
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
            var pirateQuery = new QueryDescription().WithAll<Transform, EnemyAI, Health>();
            _world.Query(in pirateQuery, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
            {
                if (entity == self || h.IsDead) return;
                if (ai.Faction != Faction.Pirate) return;
                float dist = Vector2.Distance(selfPos, t.Position);
                if (dist < range && dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = t.Position;
                    bestTarget = entity;
                }
            });
        }

        return (bestPos, bestDist < float.MaxValue, bestTarget);
    }

    private Vector2? FindNearestPirate(Vector2 pos, float range)
    {
        float bestDist = range;
        Vector2? bestPos = null;

        var q = new QueryDescription().WithAll<Transform, EnemyAI, Health>();
        _world.Query(in q, (Entity entity, ref Transform t, ref EnemyAI ai, ref Health h) =>
        {
            if (h.IsDead || ai.Faction != Faction.Pirate) return;
            float dist = Vector2.Distance(pos, t.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = t.Position;
            }
        });

        return bestPos;
    }

    private void FireProjectile(Vector2 origin, Vector2 direction, float damage, float speed, Faction faction)
    {
        // Offset spawn position slightly ahead of the ship
        var spawnPos = origin + direction * 20f;

        // Color by faction
        var (r, g, b) = faction switch
        {
            Faction.Pirate => ((byte)255, (byte)80, (byte)80),     // Red
            Faction.Patrol => ((byte)80, (byte)200, (byte)255),    // Blue
            Faction.Trader => ((byte)255, (byte)255, (byte)80),    // Yellow
            _ => ((byte)255, (byte)255, (byte)255)
        };

        _pendingProjectiles.Add((spawnPos, direction, damage, speed, faction, r, g, b));
    }

    private void ClampToMap(ref Transform transform)
    {
        float margin = 100f;
        transform.Position.X = Math.Clamp(transform.Position.X, margin, _mapWidth - margin);
        transform.Position.Y = Math.Clamp(transform.Position.Y, margin, _mapHeight - margin);
    }
}
