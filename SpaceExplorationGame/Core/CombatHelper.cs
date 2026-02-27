using System.Diagnostics;
using System.Numerics;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Shared combat utilities used by both SolarSystemState and PlanetSurfaceState.
/// Eliminates duplication of damage popup creation, loot processing, and combat message handling.
/// </summary>
public static class CombatHelper
{

    /// <summary>
    /// Create damage popups from projectile system damage events.
    /// </summary>
    public static void CreateDamagePopups(
        List<DamagePopup> popups,
        IReadOnlyList<DamageEvent> damageEvents)
    {
        for (int i = 0; i < damageEvents.Count; i++)
        {
            var evt = damageEvents[i];
            var offset = ComputePopupOffset(8f);
            float duration = ComputePopupDuration(0.9f, 1.2f);
            popups.Add(new DamagePopup(evt.Position + offset, evt.Damage, evt.ShieldHit, duration));
        }
    }

    private static Vector2 ComputePopupOffset(float maxOffset)
    {
        float rx = NextUnitFloat();
        float ry = NextUnitFloat();
        return new Vector2((rx * 2f - 1f) * maxOffset, (ry * 2f - 1f) * maxOffset);
    }

    private static float ComputePopupDuration(float min, float max)
    {
        return min + (max - min) * NextUnitFloat(); ;
    }

    private static float NextUnitFloat()
    {
        return Random.Shared.NextSingle();
    }

    /// <summary>
    /// Process loot from a destroyed enemy — awards credits, resources, and optionally equipment parts.
    /// Returns a display message describing the loot received.
    /// </summary>
    /// <param name="game">Game instance for accessing player data.</param>
    /// <param name="loot">Loot drop data from the destroyed entity.</param>
    /// <param name="rng">Seeded random for deterministic rolls.</param>
    /// <param name="resourceAmountMax">Exclusive upper bound for resource amount roll (default 4 for surface).</param>
    /// <param name="enablePartDrops">If true, rolls for ship part drops (space combat only).</param>
    public static string ProcessLootDrop(Game game, LootDrop loot, SeededRandom rng,
        int resourceAmountMax = 4, bool enablePartDrops = false)
    {
        // Credits
        int credits = rng.NextInt(loot.MinCredits, loot.MaxCredits + 1);
        game.Player.Credits += credits;
        game.Audio.PlaySfx(SfxType.PickupCredits, 0.6f);
        string message = $"+{credits} CREDITS";

        // Resource drop
        if (rng.NextFloat() < loot.ResourceDropChance)
        {
            var resource = (ResourceType)rng.NextInt(0, Enum.GetValues<ResourceType>().Length);
            int amount = rng.NextInt(1, resourceAmountMax);
            int added = game.Player.AddCargo(resource, amount);
            if (added > 0)
            {
                var resName = ResourceCatalog.Get(resource).Name;
                message += $"  +{added} {resName.ToUpper()}";
                game.Audio.PlaySfx(SfxType.PickupItem, 0.5f);
            }
        }

        // Part drop (ship parts — space combat only)
        if (enablePartDrops && rng.NextFloat() < loot.PartDropChance)
        {
            int maxTier = Math.Min(3, 1 + loot.DangerLevel / 2);
            var candidates = ShipPartCatalog.AllParts
                .Where(p => p.Tier > 0 && p.Tier <= maxTier)
                .ToArray();

            if (candidates.Length > 0)
            {
                var droppedPart = candidates[rng.NextInt(0, candidates.Length)];
                if (!game.Player.OwnedParts.Contains(droppedPart) &&
                    !game.Player.EquippedParts.ContainsValue(droppedPart))
                {
                    game.Player.OwnedParts.Add(droppedPart);
                    message += $"  +{droppedPart.Name.ToUpper()}!";
                }
            }
        }

        return message;
    }

    /// <summary>
    /// Update damage popups and explosions (timers, removal).
    /// </summary>
    public static void UpdateVisualEffects(List<DamagePopup> popups, List<Explosion> explosions, float dt)
    {
        ProjectileRenderer.UpdateDamageEffects(popups, dt);
        ProjectileRenderer.UpdateExplosions(explosions, dt);
    }

    public static float ResolveProjectileLifetime(float range, float speed)
    {
        Debug.Assert(range > 0f && speed > 0f, "Missing weapon range or projectile speed in EnemyAIConfig. Check EntityFactory.CreateEnemyShip for defaults.");
        if (range > 0f && speed > 0f)
            return Math.Max(0.1f, range / speed);
        return 0.1f;
    }

    public static Color3 ResolveProjectileColor(Faction faction)
    {
        return faction switch
        {
            Faction.Player => new Color3(100, 255, 100),
            Faction.Pirate => new Color3(255, 80, 80),
            Faction.Patrol => new Color3(80, 200, 255),
            Faction.Trader => new Color3(255, 255, 80),
            _ => new Color3(255, 255, 255)
        };
    }

    public static ShipWeaponSpec[] BuildWeaponSpecs(Dictionary<ShipSlotType, ShipPart> equippedParts)
    {
        var weapons = new List<ShipWeaponSpec>(2);

        AddWeaponFromSlot(equippedParts, ShipSlotType.Weapon1, weapons);
        AddWeaponFromSlot(equippedParts, ShipSlotType.Weapon2, weapons);

        return [.. weapons];
    }

    private static void AddWeaponFromSlot(Dictionary<ShipSlotType, ShipPart> equippedParts,
        ShipSlotType slot, List<ShipWeaponSpec> weapons)
    {
        if (!equippedParts.TryGetValue(slot, out var part))
            return;

        var stats = part.Stats;
        if (stats.WeaponDamage <= 0f || stats.WeaponFireRate <= 0f ||
            stats.WeaponRange <= 0f || stats.ProjectileSpeed <= 0f)
            return;

        weapons.Add(new ShipWeaponSpec(
            stats.WeaponDamage,
            stats.WeaponFireRate,
            stats.WeaponRange,
            stats.ProjectileSpeed));
    }

}
