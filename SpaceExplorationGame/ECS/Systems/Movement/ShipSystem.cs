using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Movement;

/// <summary>
/// Applies shared ship behavior for all ships: movement intent consumption and weapon firing.
/// </summary>
public partial class ShipSystem : BaseSystem<World, float>
{
    private readonly List<ProjectileSpawn> _pendingProjectiles = [];
    public IReadOnlyList<ProjectileSpawn> ProjectilesSpawnedLastUpdate => _pendingProjectiles;

    public ShipSystem(World world) : base(world)
    {
    }

    public override void Update(in float dt)
    {
        _pendingProjectiles.Clear();

        ProcessShipQuery(World, dt);

        foreach (var spawn in _pendingProjectiles)
        {
            EntityFactory.CreateProjectile(World, spawn.OwnerEntity, spawn.Pos, spawn.Dir, spawn.Damage, spawn.Speed,
                spawn.Faction, spawn.Color, spawn.Lifetime, spawn.InheritedVelocity);
        }
    }

    [Query]
    [All(typeof(Transform), typeof(Velocity), typeof(ShipInputComponent), typeof(ShipComponent))]
    public void ProcessShip(Entity entity, ref Transform transform, ref Velocity velocity, ref ShipInputComponent input, ref ShipComponent ship, [Data] in float dt)
    {
        velocity.Acceleration = Vector2.Zero;
        velocity.RotationVelocity = Math.Clamp(input.RotationSpeed,
            -ship.MaxRotationSpeed, ship.MaxRotationSpeed);

        velocity.MaxSpeed = ship.MaxSpeed;
        velocity.MaxRotationSpeed = ship.MaxRotationSpeed;
        velocity.Damping = 1f;

        Vector2 wantedAccelerationDirection = input.AccelerationDirection;
        if (wantedAccelerationDirection != Vector2.Zero)
        {
            ApplyDirectionalBrake(
                ref velocity,
                wantedAccelerationDirection,
                ship.MaxAcceleration,
                minSpeedForBrake: 50f,
                misalignmentThreshold: 0.25f,
                maxBrakeMultiplier: 1.2f);

            velocity.Acceleration += wantedAccelerationDirection * ship.MaxAcceleration;
        }

        TickWeaponCooldowns(ref ship, dt);
        if (input.Shoot)
        {
            TryFireProjectiles(entity, ref transform, ref velocity, ref ship);
        }
    }

    private void TickWeaponCooldowns(ref ShipComponent ship, float dt)
    {
        EnsureCooldownArray(ref ship);
        for (int i = 0; i < ship.WeaponCooldowns.Length; i++)
            ship.WeaponCooldowns[i] -= dt;
    }

    private static void EnsureCooldownArray(ref ShipComponent ship)
    {
        if (ship.Weapons == null)
            ship.Weapons = Array.Empty<ShipWeaponSpec>();

        if (ship.WeaponCooldowns == null || ship.WeaponCooldowns.Length != ship.Weapons.Length)
            ship.WeaponCooldowns = new float[ship.Weapons.Length];
    }

    private void TryFireProjectiles(Entity firingEntity, ref Transform transform, ref Velocity velocity,
        ref ShipComponent ship)
    {
        EnsureCooldownArray(ref ship);
        if (ship.Weapons.Length == 0)
            return;

        Vector2 facing = FacingDirection(transform.Rotation);

        int weaponCount = ship.Weapons.Length;
        float lateralOffset = weaponCount > 1 ? 6f : 0f;

        for (int i = 0; i < weaponCount; i++)
        {
            if (ship.WeaponCooldowns[i] > 0f)
                continue;

            var weapon = ship.Weapons[i];
            if (weapon.Damage <= 0f || weapon.FireRate <= 0f ||
                weapon.Range <= 0f || weapon.ProjectileSpeed <= 0f)
                continue;

            ship.WeaponCooldowns[i] = weapon.FireRate;
            float lifetime = CombatHelper.ResolveProjectileLifetime(weapon.Range, weapon.ProjectileSpeed);
            float sideOffset = weaponCount > 1 ? (i == 0 ? -lateralOffset : lateralOffset) : 0f;
            Color3 color = CombatHelper.ResolveProjectileColor(ship.Faction);
            FireProjectile(transform.Position, facing, weapon.Damage, weapon.ProjectileSpeed,
                lifetime, ship.Faction, color, sideOffset, velocity.Velocity, firingEntity);
        }
    }

    private void FireProjectile(Vector2 origin, Vector2 direction, float damage, float speed,
        float lifetime, Faction faction, Color3 color, float lateralOffset, Vector2 inheritedVelocity, Entity firingEntity)
    {
        var lateral = new Vector2(-direction.Y, direction.X);
        var spawnPos = origin + direction * 20f + lateral * lateralOffset;

        _pendingProjectiles.Add(new ProjectileSpawn(spawnPos, direction, damage, speed,
            lifetime, faction, color, inheritedVelocity, firingEntity));
    }

    private static Vector2 FacingDirection(float rotationDeg)
    {
        float rad = rotationDeg * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }

    private static void ApplyDirectionalBrake(ref Velocity velocity, Vector2 desiredDirection,
        float baseAcceleration, float minSpeedForBrake, float misalignmentThreshold, float maxBrakeMultiplier)
    {
        if (desiredDirection == Vector2.Zero)
            return;

        desiredDirection = Vector2.Normalize(desiredDirection);

        float speed = velocity.Velocity.Length();
        if (speed < minSpeedForBrake)
            return;

        var moveDir = velocity.Velocity / speed;
        float alignment = Vector2.Dot(moveDir, desiredDirection);
        if (alignment >= misalignmentThreshold)
            return;

        float t = (misalignmentThreshold - alignment) / (misalignmentThreshold + 1f);
        float brakeMultiplier = 0.5f + t * (maxBrakeMultiplier - 0.5f);
        velocity.Acceleration -= moveDir * baseAcceleration * brakeMultiplier;
    }
}
