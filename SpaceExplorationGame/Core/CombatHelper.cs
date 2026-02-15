using System.Numerics;
using SpaceExplorationGame.ECS.Components;
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
        IReadOnlyList<(Vector2 Position, float Damage, bool ShieldHit, Arch.Core.Entity Target)> damageEvents)
    {
        foreach (var (pos, damage, shieldHit, _) in damageEvents)
            popups.Add(new DamagePopup(pos, damage, shieldHit));
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
    /// Tick down a combat message timer, clearing the message when it expires.
    /// </summary>
    public static void UpdateCombatMessageTimer(ref string? combatMessage, ref float combatMessageTimer, float dt)
    {
        if (combatMessageTimer > 0)
        {
            combatMessageTimer -= dt;
            if (combatMessageTimer <= 0) combatMessage = null;
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
}
