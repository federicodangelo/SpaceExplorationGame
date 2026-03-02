namespace SpaceExplorationGame.Core;

/// <summary>
/// Helper for computing ship stats from a type and part set.
/// </summary>
public static class ShipStatsHelper
{
    /// <summary>Sum up stats from any ship type + part set. Acceleration/MaxSpeed are reduced by ship weight.</summary>
    public static ShipPartStats GetCombinedStats(ShipType shipType, IEnumerable<ShipPart> parts)
    {
        float accel = 0, maxSpd = 0, rot = 0, hull = 0, fuel = 0, ftl = 0;
        float shield = 0, dmg = 0, fuelEff = 0, cargo = 0;
        float weaponRange = 0, projectileSpeed = 0;
        float weaponFireRate = 0f;

        foreach (var part in parts)
        {
            var s = part.Stats;
            accel += s.Acceleration;
            maxSpd += s.MaxSpeed;
            rot += s.RotationSpeed;
            hull += s.MaxHull;
            fuel += s.MaxFuel;
            ftl += s.FtlRange;
            shield += s.ShieldStrength;
            dmg += s.WeaponDamage;
            if (s.WeaponFireRate > 0f)
                weaponFireRate = weaponFireRate <= 0f ? s.WeaponFireRate : Math.Min(weaponFireRate, s.WeaponFireRate);
            weaponRange = Math.Max(weaponRange, s.WeaponRange);
            projectileSpeed = Math.Max(projectileSpeed, s.ProjectileSpeed);
            fuelEff += s.FuelEfficiency;
            cargo += s.CargoCapacity;
        }

        // Apply ship weight: heavier ships are slower
        float weight = shipType.Weight;
        accel /= weight;
        maxSpd /= weight;

        return new ShipPartStats(accel, maxSpd, rot, hull, fuel, ftl, shield, dmg,
            weaponFireRate, weaponRange, projectileSpeed, fuelEff, cargo);
    }
}
