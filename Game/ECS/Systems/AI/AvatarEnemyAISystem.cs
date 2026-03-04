using System.Numerics;
using Arch.Core;
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
    private Entity _currentAIEntity;
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
        foreach (var spawn in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(World, spawn.OwnerEntity, spawn.Pos, spawn.Dir, spawn.Damage, spawn.Speed,
                spawn.Faction, spawn.Color, spawn.Lifetime, Vector2.Zero);
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
    public void ProcessSurfaceAI(Entity entity, ref Transform transform, ref Velocity velocity,
        ref SurfaceAI ai, ref Health health)
    {
        if (health.IsDead) return;
        _currentAIEntity = entity;

        ai.StateTimer += _dt;
        ai.FireCooldown -= _dt;
        velocity.RotationVelocity = 0f;
        velocity.Acceleration = Vector2.Zero;

        // NPCs that are boarding their ship walk straight toward it and ignore normal AI
        if (World.Has<SurfaceNpcState>(entity))
        {
            ref var npcState = ref World.Get<SurfaceNpcState>(entity);
            if (npcState.Phase == SurfaceNpcPhase.BoardingShip)
            {
                UpdateBoardingNpc(ref transform, ref velocity, ref npcState);
                return;
            }
        }

        float distToPlayer = Vector2.Distance(transform.Position, _playerPos);
        bool wasInCombat = false;

        switch (ai.Config.Faction)
        {
            case Faction.Pirate:
                UpdateHostileNpc(ref transform, ref velocity, ref ai, ref health, distToPlayer);
                wasInCombat = ai.State is AIState.Chase or AIState.Attack or AIState.Flee;
                break;
            case Faction.Patrol:
                UpdatePatrolNpc(ref transform, ref velocity, ref ai, ref health, distToPlayer);
                break;
            case Faction.Trader:
                UpdateTraderNpc(ref transform, ref velocity, ref ai, distToPlayer);
                break;
        }

        // Track inactivity for departure logic
        if (World.Has<SurfaceNpcState>(entity))
        {
            ref var npcState = ref World.Get<SurfaceNpcState>(entity);
            if (npcState.Phase == SurfaceNpcPhase.OnFoot)
            {
                if (wasInCombat)
                    npcState.InactivityTimer = 0f;
                else
                    npcState.InactivityTimer += _dt;
            }
        }
    }

    /// <summary>Hostile NPC (pirate) — chases and attacks the player, flees at low health.</summary>
    private void UpdateHostileNpc(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
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
                SetState(ref ai, AIState.Chase);
                SetAccelerationTowardVelocity(ref velocity, dir * ai.Config.MoveSpeed);
            }
            else
            {
                SetState(ref ai, AIState.Attack);
                var strafeDir = new Vector2(-dir.Y, dir.X) * MathF.Sin(ai.StateTimer * 2f);
                SetAccelerationTowardVelocity(ref velocity, strafeDir * ai.Config.MoveSpeed * 0.3f);
            }

            if (dist < ai.Config.AttackRange && ai.FireCooldown <= 0)
            {
                ai.FireCooldown = ai.Config.FireRate;
                _pendingProjectiles.Add(new SurfaceProjectileSpawn(transform.Position, dir, ai.Config.WeaponDamage,
                    ai.Config.ProjectileSpeed, ai.Config.Faction, new Color3(255, 150, 50), GameConfig.AvatarProjectileLifetime, _currentAIEntity));
            }
        }
        else
        {
            WanderAround(ref transform, ref velocity, ref ai);
        }
    }

    /// <summary>Patrol NPC — wanders around, attacks pirates if detected (not player unless provoked). Friendly to player.</summary>
    private void UpdatePatrolNpc(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        ref Health health, float distToPlayer)
    {
        // Patrols just wander — they don't target the player
        // (Future: they could chase nearby pirates if we add NPC-vs-NPC targeting)
        WanderAround(ref transform, ref velocity, ref ai);
    }

    /// <summary>Trader NPC — wanders toward settlements, never fights.</summary>
    private void UpdateTraderNpc(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai,
        float distToPlayer)
    {
        // Traders just wander peacefully
        WanderAround(ref transform, ref velocity, ref ai);
    }

    /// <summary>Shared wander/patrol behavior.</summary>
    private void WanderAround(ref Transform transform, ref Velocity velocity, ref SurfaceAI ai)
    {
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

    /// <summary>NPC is walking back to its ship to board it. Once close enough, the manager handles takeoff.</summary>
    private void UpdateBoardingNpc(ref Transform transform, ref Velocity velocity, ref SurfaceNpcState npcState)
    {
        if (!World.IsAlive(npcState.ShipEntity)) return;

        var shipPos = World.Get<Transform>(npcState.ShipEntity).Position;
        var toShip = shipPos - transform.Position;
        float dist = toShip.Length();

        if (dist < 8f)
        {
            // Close enough — stop moving, manager will handle the transition to takeoff
            velocity.Acceleration = Vector2.Zero;
            velocity.Linear = Vector2.Zero;
        }
        else
        {
            var dir = toShip / dist;
            SetAccelerationTowardVelocity(ref velocity, dir * GameConfig.SurfaceNpcBoardingSpeed);
        }
    }

    /// <summary>
    /// Sets acceleration intent toward a desired velocity, with a pre-check collision test.
    /// If the predicted next position is blocked, desired velocity is zero.
    /// </summary>
    private void SetAccelerationTowardVelocity(ref Velocity velocity, Vector2 desiredVelocity)
    {
        velocity.Acceleration += (desiredVelocity - velocity.Linear) * 14f;
    }

    private static void SetState(ref SurfaceAI ai, AIState newState)
    {
        if (ai.State == newState)
            return;

        ai.State = newState;
        ai.StateTimer = 0f;
    }
}
