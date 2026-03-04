using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.AI;

/// <summary>
/// AI behaviour system for surface enemies (fauna and bandits).
/// Walk-based movement with simple chase/attack logic.
/// Writes movement and fire intent into <see cref="AvatarInputComponent"/>;
/// <see cref="AvatarSystem"/> handles actual velocity updates and projectile spawning.
/// </summary>
public partial class AvatarEnemyAISystem : BaseSystem<World, float>
{
    private const float BanditAttackEnterRangeFactor = 0.92f;
    private const float BanditAttackExitRangeFactor = 1.08f;

    private static readonly QueryDescription _playerAvatarQuery =
        new QueryDescription().WithAll<PlayerControlled, Transform, Health>();

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
        _dt = dt;
        QueryPlayerState();
        ProcessSurfaceAIQuery(World);
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
    [All(typeof(Transform), typeof(Velocity), typeof(SurfaceAI), typeof(Health), typeof(AvatarInputComponent))]
    public void ProcessSurfaceAI(Entity entity, ref Transform transform, ref Velocity velocity,
        ref SurfaceAI ai, ref Health health, ref AvatarInputComponent avatarInput)
    {
        if (health.IsDead) return;
        _currentAIEntity = entity;

        ai.StateTimer += _dt;

        // Clear per-tick intent; movement methods below will fill it in
        avatarInput.DesiredVelocity = Vector2.Zero;
        avatarInput.Shoot = false;

        // NPCs that are boarding their ship walk straight toward it and ignore normal AI
        if (World.Has<SurfaceNpcState>(entity))
        {
            ref var npcState = ref World.Get<SurfaceNpcState>(entity);
            if (npcState.Phase == SurfaceNpcPhase.BoardingShip)
            {
                UpdateBoardingNpc(ref transform, ref velocity, ref avatarInput, ref npcState);
                return;
            }
        }

        float distToPlayer = Vector2.Distance(transform.Position, _playerPos);
        bool wasInCombat = false;

        switch (ai.Config.Faction)
        {
            case Faction.Pirate:
                UpdateHostileNpc(ref transform, ref ai, ref health, ref avatarInput, distToPlayer);
                wasInCombat = ai.State is AIState.Chase or AIState.Attack or AIState.Flee;
                break;
            case Faction.Patrol:
                UpdatePatrolNpc(ref transform, ref ai, ref avatarInput);
                break;
            case Faction.Trader:
                UpdateTraderNpc(ref transform, ref ai, ref avatarInput);
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
    private void UpdateHostileNpc(ref Transform transform, ref SurfaceAI ai,
        ref Health health, ref AvatarInputComponent avatarInput, float dist)
    {
        float hullPercent = health.HullPercent;

        // Flee if very low health
        if (hullPercent < 0.15f && _playerAlive && dist < ai.Config.DetectRange)
        {
            SetState(ref ai, AIState.Flee);
            var fleeDir = Vector2.Normalize(transform.Position - _playerPos);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
            avatarInput.DesiredVelocity = fleeDir * ai.Config.MoveSpeed * 1.2f;
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
                avatarInput.DesiredVelocity = dir * ai.Config.MoveSpeed;
            }
            else
            {
                SetState(ref ai, AIState.Attack);
                var strafeDir = new Vector2(-dir.Y, dir.X) * MathF.Sin(ai.StateTimer * 2f);
                avatarInput.DesiredVelocity = strafeDir * ai.Config.MoveSpeed * 0.3f;
            }

            // Signal fire intent — AvatarSystem checks cooldown and spawns the projectile
            if (dist < ai.Config.AttackRange)
            {
                avatarInput.Shoot = true;
                avatarInput.AimDirection = dir;
            }
        }
        else
        {
            WanderAround(ref transform, ref ai, ref avatarInput);
        }
    }

    /// <summary>Patrol NPC — wanders around. Friendly to player.</summary>
    private void UpdatePatrolNpc(ref Transform transform, ref SurfaceAI ai,
        ref AvatarInputComponent avatarInput)
    {
        WanderAround(ref transform, ref ai, ref avatarInput);
    }

    /// <summary>Trader NPC — wanders peacefully, never fights.</summary>
    private void UpdateTraderNpc(ref Transform transform, ref SurfaceAI ai,
        ref AvatarInputComponent avatarInput)
    {
        WanderAround(ref transform, ref ai, ref avatarInput);
    }

    /// <summary>Shared wander/patrol behaviour.</summary>
    private void WanderAround(ref Transform transform, ref SurfaceAI ai, ref AvatarInputComponent avatarInput)
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
        avatarInput.DesiredVelocity = wanderDir * wanderSpeed;
    }

    /// <summary>NPC is walking back to its ship to board it. Once close enough, the manager handles takeoff.</summary>
    private void UpdateBoardingNpc(ref Transform transform, ref Velocity velocity,
        ref AvatarInputComponent avatarInput, ref SurfaceNpcState npcState)
    {
        if (!World.IsAlive(npcState.ShipEntity)) return;

        var shipPos = World.Get<Transform>(npcState.ShipEntity).Position;
        var toShip = shipPos - transform.Position;
        float dist = toShip.Length();

        if (dist < 8f)
        {
            // Close enough — hard-stop; AvatarSystem will see DesiredVelocity=0 → zero acceleration
            avatarInput.DesiredVelocity = Vector2.Zero;
            velocity.Linear = Vector2.Zero;
        }
        else
        {
            var dir = toShip / dist;
            avatarInput.DesiredVelocity = dir * NpcConfig.SurfaceNpcBoardingSpeed;
        }
    }

    private static void SetState(ref SurfaceAI ai, AIState newState)
    {
        if (ai.State == newState)
            return;

        ai.State = newState;
        ai.StateTimer = 0f;
    }
}
