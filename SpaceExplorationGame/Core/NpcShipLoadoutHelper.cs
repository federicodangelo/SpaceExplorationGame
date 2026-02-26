using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Helper for selecting NPC ship types, loadouts, and derived combat stats.
/// </summary>
public static class NpcShipLoadoutHelper
{
    public static int GetNpcQualityTier(int dangerLevel)
    {
        return Math.Clamp(1 + (dangerLevel - 1) / 2, 1, 3);
    }

    public static ShipType ChooseNpcShipType(Faction faction, int dangerLevel, SeededRandom rng)
    {
        ShipType[] options = faction switch
        {
            Faction.Pirate => dangerLevel >= 4
                ? [ShipTypeCatalog.Fighter, ShipTypeCatalog.Explorer]
                : [ShipTypeCatalog.Scout, ShipTypeCatalog.Fighter],
            Faction.Trader => dangerLevel >= 4
                ? [ShipTypeCatalog.Freighter, ShipTypeCatalog.Explorer]
                : [ShipTypeCatalog.Scout, ShipTypeCatalog.Freighter],
            Faction.Patrol => dangerLevel >= 4
                ? [ShipTypeCatalog.Fighter, ShipTypeCatalog.Explorer]
                : [ShipTypeCatalog.Scout, ShipTypeCatalog.Fighter],
            _ => ShipTypeCatalog.AllTypes
        };

        return options[rng.NextInt(0, options.Length)];
    }

    public static Dictionary<ShipSlotType, ShipPart> BuildNpcLoadout(ShipType shipType, Faction faction,
        int qualityTier, SeededRandom rng)
    {
        var loadout = new Dictionary<ShipSlotType, ShipPart>();
        var fallback = ShipPartCatalog.GetStarterLoadout(shipType);

        foreach (var slot in shipType.AvailableSlots)
        {
            ShipPart chosen = slot switch
            {
                ShipSlotType.Weapon1 => faction == Faction.Trader
                    ? ShipPartCatalog.GetById("weapon_none")!
                    : PickWeaponPart(qualityTier, rng),
                ShipSlotType.Weapon2 => faction == Faction.Trader || qualityTier < 2
                    ? ShipPartCatalog.GetById("weapon_none")!
                    : PickWeaponPart(qualityTier, rng),
                ShipSlotType.Utility or ShipSlotType.Utility2 => PickUtilityPart(faction, qualityTier, rng),
                _ => PickBestPart(ShipPartCatalog.GetPartsForSlot(slot), qualityTier, rng, fallback[slot])
            };

            loadout[slot] = chosen;
        }

        return loadout;
    }

    public static NpcShipStats BuildNpcShipStats(ShipType shipType, Dictionary<ShipSlotType, ShipPart> loadout)
    {
        var stats = ShipStatsHelper.GetCombinedStats(shipType, loadout.Values);
        float maxHull = shipType.BaseHull + stats.MaxHull;
        float maxShield = stats.ShieldStrength;
        float maxSpeed = stats.MaxSpeed;
        float rotationSpeed = stats.RotationSpeed;
        float acceleration = stats.Acceleration;
        float weaponDamage = stats.WeaponDamage;
        float weaponRange = stats.WeaponRange;
        float projectileSpeed = stats.ProjectileSpeed;
        float weaponFireRate = stats.WeaponFireRate;

        return new NpcShipStats(shipType.SpriteSize, maxHull, maxShield, maxSpeed, rotationSpeed, acceleration,
            weaponDamage, weaponFireRate, weaponRange, projectileSpeed);
    }

    public static int ComputeNpcLootCredits(ShipType shipType, Dictionary<ShipSlotType, ShipPart> loadout)
    {
        int value = shipType.SellValue;
        foreach (var part in loadout.Values)
        {
            if (part.Tier > 0)
                value += part.SellValue;
        }

        return Math.Max(GameConfig.BaseLootCredits, value / 12);
    }

    private static ShipPart PickWeaponPart(int qualityTier, SeededRandom rng)
    {
        var fallback = ShipPartCatalog.GetById("weapon_laser")!;
        return PickBestPart(ShipPartCatalog.GetPartsForSlot(ShipSlotType.Weapon1), qualityTier, rng, fallback);
    }

    private static ShipPart PickUtilityPart(Faction faction, int qualityTier, SeededRandom rng)
    {
        if (faction == Faction.Trader)
        {
            string[] options = qualityTier switch
            {
                <= 1 => ["util_cargo_small"],
                2 => ["util_cargo_large", "util_efficiency"],
                _ => ["util_cargo_large", "util_efficiency", "util_booster"]
            };

            return ShipPartCatalog.GetById(options[rng.NextInt(0, options.Length)])!;
        }

        if (qualityTier <= 1)
            return ShipPartCatalog.GetById("util_none")!;

        return PickBestPart(ShipPartCatalog.GetPartsForSlot(ShipSlotType.Utility), qualityTier, rng,
            ShipPartCatalog.GetById("util_none")!);
    }

    private static ShipPart PickBestPart(ShipPart[] parts, int qualityTier, SeededRandom rng, ShipPart fallback)
    {
        int bestTier = -1;
        int count = 0;
        ShipPart[] candidates = new ShipPart[parts.Length];

        foreach (var part in parts)
        {
            if (part.Tier <= 0 || part.Tier > qualityTier)
                continue;

            if (part.Tier > bestTier)
            {
                bestTier = part.Tier;
                count = 0;
            }

            if (part.Tier == bestTier)
                candidates[count++] = part;
        }

        if (count == 0)
            return fallback;

        return candidates[rng.NextInt(0, count)];
    }

}
