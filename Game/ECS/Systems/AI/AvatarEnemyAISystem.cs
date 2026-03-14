using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.AI;

/// <summary>
/// AI behaviour system for surface enemies (fauna and bandits).
/// Walk-based movement with chase/attack/flee logic inspired by <see cref="ShipEnemyAISystem"/>.
/// <list type="bullet">
///   <item>Target memory — pirates keep moving toward the last known player position for a short window after losing sight.</item>
///   <item>Aim inaccuracy wobble — sinusoidal aim offset scaled by <see cref="SurfaceAIConfig.AimInaccuracyRadius"/>.</item>
///   <item>Configurable flee threshold with minimum flee duration (no stutter-flee).</item>
///   <item>Patrol hunts the nearest surface pirate within detection range and opens fire.</item>
///   <item>Trader flees from any nearby pirate instead of blindly wandering.</item>
/// </list>
/// Writes movement and fire intent into <see cref="AvatarInputComponent"/>;
/// <see cref="AvatarSystem"/> handles actual velocity updates and projectile spawning.
/// </summary>
public partial class AvatarEnemyAISystem : BaseSystem<World, float>
{
    private const float AttackEnterRangeFactor = 0.92f;
    private const float AttackExitRangeFactor = 1.08f;
    private const float TargetMemoryDuration = 2.0f;
    private const float MinFleeStateDuration = 1.0f;
    private const float TraderFleeRange = 300f;
    private const float CoverSeekRange = 250f;

    private static readonly QueryDescription _playerAvatarQuery =
        new QueryDescription().WithAll<PlayerControlled, Transform, Velocity, Health>();

    private static readonly QueryDescription _surfaceAIQuery =
        new QueryDescription().WithAll<Transform, SurfaceAI, Health>();

    private static readonly QueryDescription _coverQuery =
        new QueryDescription().WithAll<Transform, CoverObstacle, Health>();

    // Per-frame cached state for [Query] method access
    private float _dt;
    private Vector2 _playerPos;
    private Vector2 _playerVelocity;
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
        _playerVelocity = Vector2.Zero;

        var q = _playerAvatarQuery;
        World.Query(in q, (ref Transform transform, ref Velocity velocity, ref Health health) =>
        {
            _playerPos = transform.Position;
            _playerVelocity = velocity.Linear;
            _playerAlive = !health.IsDead;
        });
    }

    /// <summary>Source-generated query: iterates all surface AI entities.</summary>
    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(SurfaceAI), typeof(Health), typeof(AvatarInputComponent), typeof(AvatarComponent))]
    public void ProcessSurfaceAI(Entity entity, ref Transform transform, ref Velocity velocity,
        ref SurfaceAI ai, ref Health health, ref AvatarInputComponent avatarInput, ref AvatarComponent avatar)
    {
        if (health.IsDead) return;

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

        bool wasInCombat = false;

        switch (ai.Config.Faction)
        {
            case Faction.Pirate:
                UpdateHostileNpc(ref transform, ref ai, ref health, ref avatarInput);
                wasInCombat = ai.State is AIState.Chase or AIState.Attack or AIState.Flee;
                break;
            case Faction.Patrol:
                UpdatePatrolNpc(ref transform, ref ai, ref avatarInput);
                wasInCombat = ai.State is AIState.Chase or AIState.Attack;
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

    // ── Faction behaviours ──────────────────────────────────────────────────

    /// <summary>
    /// Hostile NPC (pirate) — chases the player, attacks in range, flees at low health.
    /// Uses target memory to keep pursuing the last known position after losing sight,
    /// and lead-shot aim so fast projectiles actually intercept a moving player.
    /// </summary>
    private void UpdateHostileNpc(ref Transform transform, ref SurfaceAI ai,
        ref Health health, ref AvatarInputComponent avatarInput)
    {
        // Pirates with Friendly+ player reputation just wander
        if (FactionRules.PlayerReputationLevel(Faction.Pirate) >= ReputationLevel.Friendly)
        {
            WanderAround(ref transform, ref ai, ref avatarInput);
            return;
        }

        // Keep fleeing for a minimum duration to prevent stutter-fleeing
        bool keepFleeing = ai.State == AIState.Flee && ai.StateTimer < MinFleeStateDuration;
        if ((health.HullPercent < ai.Config.FleeHealthPercent || keepFleeing) && _playerAlive)
        {
            SetState(ref ai, AIState.Flee);

            // Try to find nearby cover and move behind it relative to the player
            var coverPos = FindNearestCover(transform.Position, CoverSeekRange);
            if (coverPos.HasValue)
            {
                // Move to a point on the far side of the cover from the player
                var coverToPlayer = Vector2.Normalize(_playerPos - coverPos.Value);
                if (float.IsNaN(coverToPlayer.X)) coverToPlayer = new Vector2(1, 0);
                var hideSpot = coverPos.Value - coverToPlayer * 24f;
                var toHide = Vector2.Normalize(hideSpot - transform.Position);
                if (float.IsNaN(toHide.X)) toHide = new Vector2(1, 0);
                avatarInput.DesiredVelocity = toHide * ai.Config.MoveSpeed * 1.2f;
            }
            else
            {
                var fleeDir = Vector2.Normalize(transform.Position - _playerPos);
                if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
                avatarInput.DesiredVelocity = fleeDir * ai.Config.MoveSpeed * 1.2f;
            }
            return;
        }

        // Check live visibility then fall back to memory
        bool liveVisible = _playerAlive &&
            Vector2.Distance(transform.Position, _playerPos) < ai.Config.DetectRange;

        var (targetPos, targetVelocity, hasTarget) =
            ResolveTargetWithMemory(ref ai, _playerPos, _playerVelocity, liveVisible);

        if (!hasTarget)
        {
            WanderAround(ref transform, ref ai, ref avatarInput);
            return;
        }

        float dist = Vector2.Distance(transform.Position, targetPos);
        var dir = Vector2.Normalize(targetPos - transform.Position);
        if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

        bool inAttackRange = ai.State switch
        {
            AIState.Attack => dist <= ai.Config.AttackRange * AttackExitRangeFactor,
            _ => dist <= ai.Config.AttackRange * AttackEnterRangeFactor
        };

        if (!inAttackRange)
        {
            SetState(ref ai, AIState.Chase);
            avatarInput.DesiredVelocity = dir * ai.Config.MoveSpeed;
        }
        else
        {
            SetState(ref ai, AIState.Attack);
            // Strafe perpendicular with alternating direction
            var strafeDir = new Vector2(-dir.Y, dir.X) * MathF.Sign(MathF.Sin(ai.StateTimer * 2f));
            avatarInput.DesiredVelocity = strafeDir * ai.Config.MoveSpeed * 0.3f;
        }

        // Fire with lead-shot aim and inaccuracy wobble — always aim at real player position
        if (dist < ai.Config.AttackRange)
        {
            avatarInput.AimDirection = ComputeAimWithWobble(
                transform.Position, _playerPos,
                ai.Config.AimInaccuracyRadius, ai.StateTimer, dir);
            avatarInput.Shoot = true;
        }
    }

    /// <summary>
    /// Patrol NPC — hunts the nearest surface pirate within detection range and engages in combat.
    /// Falls back to wandering when no pirate is found.
    /// </summary>
    private void UpdatePatrolNpc(ref Transform transform, ref SurfaceAI ai,
        ref AvatarInputComponent avatarInput)
    {
        // When player has Hostile patrol reputation, patrols also target the player
        bool targetPlayer = _playerAlive
            && FactionRules.PlayerReputationLevel(Faction.Patrol) == ReputationLevel.Hostile
            && Vector2.Distance(transform.Position, _playerPos) < ai.Config.DetectRange;

        var nearestPirate = FindNearestSurfacePirate(transform.Position, ai.Config.DetectRange);

        // Pick closest threat: nearest pirate or hostile player
        Vector2? targetPos = nearestPirate;
        if (targetPlayer)
        {
            float playerDist = Vector2.Distance(transform.Position, _playerPos);
            float pirateDist = nearestPirate.HasValue
                ? Vector2.Distance(transform.Position, nearestPirate.Value)
                : float.MaxValue;
            if (playerDist < pirateDist)
                targetPos = _playerPos;
        }

        if (!targetPos.HasValue)
        {
            WanderAround(ref transform, ref ai, ref avatarInput);
            return;
        }

        float dist = Vector2.Distance(transform.Position, targetPos.Value);
        var dir = Vector2.Normalize(targetPos.Value - transform.Position);
        if (float.IsNaN(dir.X)) dir = new Vector2(1, 0);

        bool inAttackRange = ai.State switch
        {
            AIState.Attack => dist <= ai.Config.AttackRange * AttackExitRangeFactor,
            _ => dist <= ai.Config.AttackRange * AttackEnterRangeFactor
        };

        if (!inAttackRange)
        {
            SetState(ref ai, AIState.Chase);
            avatarInput.DesiredVelocity = dir * ai.Config.MoveSpeed;
        }
        else
        {
            SetState(ref ai, AIState.Attack);
            var strafeDir = new Vector2(-dir.Y, dir.X) * MathF.Sign(MathF.Sin(ai.StateTimer * 2f));
            avatarInput.DesiredVelocity = strafeDir * ai.Config.MoveSpeed * 0.25f;
        }

        if (dist < ai.Config.AttackRange)
        {
            avatarInput.AimDirection = ComputeAimWithWobble(
                transform.Position, targetPos.Value,
                ai.Config.AimInaccuracyRadius, ai.StateTimer, dir);
            avatarInput.Shoot = true;
        }
    }

    /// <summary>
    /// Trader NPC — wanders peacefully at reduced speed, but flees from any pirate that
    /// wanders within <see cref="TraderFleeRange"/> world units.
    /// </summary>
    private void UpdateTraderNpc(ref Transform transform, ref SurfaceAI ai,
        ref AvatarInputComponent avatarInput)
    {
        var nearestPirate = FindNearestSurfacePirate(transform.Position, TraderFleeRange);

        if (nearestPirate.HasValue)
        {
            SetState(ref ai, AIState.Flee);
            var fleeDir = Vector2.Normalize(transform.Position - nearestPirate.Value);
            if (float.IsNaN(fleeDir.X)) fleeDir = new Vector2(1, 0);
            avatarInput.DesiredVelocity = fleeDir * ai.Config.MoveSpeed;
        }
        else
        {
            WanderAround(ref transform, ref ai, ref avatarInput);
        }
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
            avatarInput.DesiredVelocity = (toShip / dist) * NpcConfig.SurfaceNpcBoardingSpeed;
        }
    }

    // ── Target memory ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the best available target position: the live position when visible, or the
    /// extrapolated last-known position while memory hasn't expired.
    /// </summary>
    private (Vector2 Position, Vector2 Velocity, bool HasTarget) ResolveTargetWithMemory(
        ref SurfaceAI ai, Vector2 livePos, Vector2 liveVelocity, bool liveVisible)
    {
        if (liveVisible)
        {
            ai.LastKnownTargetPos = livePos;
            ai.LastKnownTargetVelocity = liveVelocity;
            ai.LastKnownTargetTimeLeft = TargetMemoryDuration;
            return (livePos, liveVelocity, true);
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

        return (ai.LastKnownTargetPos, ai.LastKnownTargetVelocity, true);
    }

    // ── Targeting helpers ────────────────────────────────────────────────────

    private Vector2? FindNearestSurfacePirate(Vector2 selfPos, float range)
    {
        float bestDist = range;
        Vector2? bestPos = null;

        var q = _surfaceAIQuery;
        World.Query(in q, (ref Transform t, ref SurfaceAI surfaceAi, ref Health h) =>
        {
            if (h.IsDead || surfaceAi.Config.Faction != Faction.Pirate) return;
            float dist = Vector2.Distance(selfPos, t.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = t.Position;
            }
        });

        return bestPos;
    }

    private Vector2? FindNearestCover(Vector2 selfPos, float range)
    {
        float bestDist = range;
        Vector2? bestPos = null;

        var q = _coverQuery;
        World.Query(in q, (ref Transform t, ref Health h) =>
        {
            if (h.IsDead) return;
            float dist = Vector2.Distance(selfPos, t.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = t.Position;
            }
        });

        return bestPos;
    }

    // ── Aim helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a sinusoidal inaccuracy wobble to the target position and returns a normalized
    /// aim direction toward it. Lead-shot prediction is intentionally omitted — avatars change
    /// direction too quickly for it to be useful.
    /// Falls back to <paramref name="fallback"/> when the target is coincident with the shooter.
    /// </summary>
    private static Vector2 ComputeAimWithWobble(
        Vector2 shooterPos, Vector2 targetPos,
        float inaccuracyRadius, float stateTimer, Vector2 fallback)
    {
        if (inaccuracyRadius > 0f)
        {
            // Two slightly-incommensurate frequencies produce a lissajous-like drift (never repeats predictably)
            float wobbleX = MathF.Sin(stateTimer * 2.3f) * inaccuracyRadius;
            float wobbleY = MathF.Cos(stateTimer * 1.7f) * inaccuracyRadius;
            targetPos += new Vector2(wobbleX, wobbleY);
        }

        var aimDir = Vector2.Normalize(targetPos - shooterPos);
        return float.IsNaN(aimDir.X) ? fallback : aimDir;
    }

    // ── State helpers ────────────────────────────────────────────────────────

    private static void SetState(ref SurfaceAI ai, AIState newState)
    {
        if (ai.State == newState)
            return;

        ai.State = newState;
        ai.StateTimer = 0f;
    }
}
