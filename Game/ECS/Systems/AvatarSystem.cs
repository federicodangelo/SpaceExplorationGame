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
                spawn.Damage, spawn.Speed, spawn.Faction, spawn.Color, spawn.Lifetime, Vector2.Zero);
        }
    }

    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(AvatarInputComponent), typeof(AvatarComponent))]
    public void ProcessAvatar(Entity entity, ref Transform transform, ref Velocity velocity,
        ref AvatarInputComponent input, ref AvatarComponent avatar, [Data] in float dt)
    {
        // Tick the fire cooldown
        avatar.FireCooldown -= dt;

        // Skip entities that are mounted in a vehicle (VehicleSystem handles them)
        if (avatar.InVehicle) return;

        // Critically-damped spring toward desired velocity (same response as the old PlayerAvatarInputSystem)
        velocity.Acceleration = (input.DesiredVelocity - velocity.Linear) * 18f;
        velocity.RotationVelocity = 0f;

        // Fire if the input requests it and the entity has a weapon and cooldown is ready
        if (input.Shoot && avatar.FireCooldown <= 0f &&
            avatar.WeaponFireRate > 0f && avatar.WeaponDamage > 0f)
        {
            avatar.FireCooldown = avatar.WeaponFireRate;
            var spawnPos = transform.Position + input.AimDirection * 14f;
            _pendingProjectiles.Add(new SurfaceProjectileSpawn(
                spawnPos, input.AimDirection, avatar.WeaponDamage, avatar.WeaponProjectileSpeed,
                avatar.Faction, avatar.ProjectileColor, CombatConfig.AvatarProjectileLifetime, entity));
        }
    }
}
