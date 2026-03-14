using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Applies shared avatar behaviour for all walking entities: critically-damped movement and weapon firing.
/// Reads <see cref="AvatarInputComponent"/> (written by player input system or enemy AI) and
/// <see cref="AvatarComponent"/> (per-entity stats), then writes to <see cref="Velocity"/> and
/// spawns projectile entities.
/// Analogous to <see cref="ShipSystem"/> for space combat.
/// </summary>
public partial class AvatarSystem : BaseSystem<World, float>
{
    private readonly List<SurfaceProjectileSpawn> _pendingProjectiles = [];

    /// <summary>All avatar projectiles spawned during the last Update (player and NPC). Available until the next Update.</summary>
    public IReadOnlyList<SurfaceProjectileSpawn> ProjectilesSpawnedLastUpdate => _pendingProjectiles;

    public AvatarSystem(World world) : base(world)
    {
    }

    public override void Update(in float dt)
    {
        _pendingProjectiles.Clear();

        ProcessAvatarQuery(World, dt);

        foreach (var spawn in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(World, spawn.OwnerEntity, spawn.Pos, spawn.Dir,
                spawn.Damage, spawn.Speed, spawn.Faction, spawn.Color, spawn.Lifetime, Vector2.Zero,
                spawn.Behavior);
        }
    }

    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(AvatarInputComponent), typeof(AvatarComponent))]
    public void ProcessAvatar(Entity entity, ref Transform transform, ref Velocity velocity,
        ref AvatarInputComponent input, ref AvatarComponent avatar, [Data] in float dt)
    {
        // Tick the fire cooldown
        avatar.FireCooldown -= dt;

        // Tick dodge cooldowns
        avatar.DodgeCooldown -= dt;
        if (avatar.DodgeTimer > 0f)
        {
            avatar.DodgeTimer -= dt;
            // During dodge roll, override velocity with fast dash
            velocity.Acceleration = Vector2.Zero;
            velocity.Linear = avatar.DodgeDirection * CombatConfig.DodgeRollSpeed;
            if (avatar.DodgeTimer <= 0f)
            {
                avatar.DodgeTimer = 0f;
                velocity.Linear = Vector2.Zero;
            }
            return; // Skip normal movement and firing during dodge
        }

        // Skip entities that are mounted in a vehicle (VehicleSystem handles them)
        if (avatar.InVehicle) return;

        // Initiate dodge roll
        if (input.DodgeRoll && avatar.DodgeCooldown <= 0f)
        {
            avatar.DodgeCooldown = CombatConfig.DodgeRollCooldown;
            avatar.DodgeTimer = CombatConfig.DodgeRollDuration;
            avatar.DodgeDirection = input.DesiredVelocity != Vector2.Zero
                ? Vector2.Normalize(input.DesiredVelocity)
                : (input.AimDirection != Vector2.Zero ? input.AimDirection : new Vector2(1, 0));
            velocity.Linear = avatar.DodgeDirection * CombatConfig.DodgeRollSpeed;
            return;
        }

        // Critically-damped spring toward desired velocity (same response as the old PlayerAvatarInputSystem)
        velocity.Acceleration = (input.DesiredVelocity - velocity.Linear) * 18f;
        velocity.RotationVelocity = 0f;

        // Fire if the input requests it and the entity has a weapon and cooldown is ready
        if (input.Shoot && avatar.FireCooldown <= 0f &&
            avatar.WeaponFireRate > 0f && avatar.WeaponDamage > 0f)
        {
            // Check ammo: negative = infinite; MaxAmmo==0 means no ammo system (e.g. NPC, uninitialized) = infinite
            if (avatar.Ammo == 0 && avatar.MaxAmmo > 0)
                return; // out of ammo

            avatar.FireCooldown = avatar.WeaponFireRate;

            // Consume ammo if finite
            if (avatar.Ammo > 0)
                avatar.Ammo--;

            var spawnPos = transform.Position + input.AimDirection * 14f;

            if (avatar.WeaponBehavior == WeaponBehavior.Spread)
            {
                // Fire multiple projectiles in a fan arc
                int count = CombatConfig.AvatarSpreadCount;
                float arcRad = CombatConfig.AvatarSpreadArc * MathF.PI / 180f;
                for (int s = 0; s < count; s++)
                {
                    float t = count > 1 ? (float)s / (count - 1) : 0.5f;
                    float angle = -arcRad / 2f + arcRad * t;
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);
                    var dir = new Vector2(
                        input.AimDirection.X * cos - input.AimDirection.Y * sin,
                        input.AimDirection.X * sin + input.AimDirection.Y * cos);
                    _pendingProjectiles.Add(new SurfaceProjectileSpawn(
                        spawnPos, dir, avatar.WeaponDamage, avatar.WeaponProjectileSpeed,
                        avatar.Faction, avatar.ProjectileColor, CombatConfig.AvatarProjectileLifetime, entity,
                        WeaponBehavior.Standard));
                }
            }
            else
            {
                _pendingProjectiles.Add(new SurfaceProjectileSpawn(
                    spawnPos, input.AimDirection, avatar.WeaponDamage, avatar.WeaponProjectileSpeed,
                    avatar.Faction, avatar.ProjectileColor, CombatConfig.AvatarProjectileLifetime, entity));
            }
        }
    }
}
