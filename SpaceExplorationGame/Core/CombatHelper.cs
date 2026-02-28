using System.Diagnostics;
using System.Numerics;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;
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
        return float.Lerp(min, max, NextUnitFloat());
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
            var candidates = ShipPartCatalog.GetPartsUpToTier(maxTier);

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
    /// Process damage events → SFX.
    /// </summary>
    public static void PlayDamageSfx(AudioManager audio, IReadOnlyList<DamageEvent> events,
        Vector2 listenerPos, float volume)
    {
        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            audio.PlaySfxAtDistance(
                evt.ShieldHit ? SfxType.ShieldHit : SfxType.HullDamage,
                evt.Position, listenerPos, volume);
        }
    }

    /// <summary>
    /// Process destroyed entities → explosions + SFX.
    /// </summary>
    /// <param name="audio">Audio manager for SFX.</param>
    /// <param name="explosions">Explosion list to append to.</param>
    /// <param name="destroyed">Destroyed entity events.</param>
    /// <param name="listenerPos">Listener position for distance attenuation.</param>
    /// <param name="npcExplosionColor">Callback that returns explosion color for an NPC faction.</param>
    /// <param name="asteroidSize">Explosion radius for asteroids.</param>
    /// <param name="playerSize">Explosion radius for player death.</param>
    /// <param name="npcSize">Explosion radius for NPC death.</param>
    /// <param name="playerExplosionColor">Explosion color for player death.</param>
    public static void ProcessDestroyedEntities(
        AudioManager audio, List<Explosion> explosions,
        IReadOnlyList<DestroyedEntity> destroyed, Vector2 listenerPos,
        Func<Faction, Color3> npcExplosionColor,
        float asteroidSize = 15f, float playerSize = 50f, float npcSize = 30f,
        Color3? playerExplosionColor = null, float npcSfxVolume = 1f)
    {
        var playerColor = playerExplosionColor ?? new Color3(255, 200, 80);
        for (int i = 0; i < destroyed.Count; i++)
        {
            var d = destroyed[i];
            if (d.Asteroid.HasValue)
            {
                explosions.Add(new Explosion(d.Position, asteroidSize, new Color3(140, 120, 100)));
                audio.PlaySfxAtDistance(SfxType.SmallExplosion, d.Position, listenerPos, 0.5f);
            }
            else if (d.Faction == Faction.Player)
            {
                explosions.Add(new Explosion(d.Position, playerSize, playerColor));
                audio.PlaySfx(SfxType.Explosion, 1.2f);
            }
            else
            {
                explosions.Add(new Explosion(d.Position, npcSize, npcExplosionColor(d.Faction)));
                audio.PlaySfxAtDistance(SfxType.Explosion, d.Position, listenerPos, npcSfxVolume);
            }
        }
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
