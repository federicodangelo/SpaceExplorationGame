using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Centralizes faction interaction rules for combat.
/// </summary>
public static class FactionRules
{
    /// <summary>
    /// Returns true if a projectile fired by <paramref name="attackerFaction"/>
    /// is allowed to hit a target belonging to <paramref name="targetFaction"/>.
    /// </summary>
    public static bool CanHit(Faction attackerFaction, Faction? targetFaction)
    {
        // Same faction never hits itself
        if (targetFaction == attackerFaction) return false;

        // Player projectiles skip player-controlled entities
        if (attackerFaction == Faction.Player && targetFaction == Faction.Player)
            return false;

        // Pirate projectiles should not hit other pirates
        if (attackerFaction == Faction.Pirate && targetFaction == Faction.Pirate)
            return false;

        // Patrol/trader projectiles only hit pirates
        if (attackerFaction is Faction.Patrol or Faction.Trader &&
            (targetFaction == Faction.Player || (targetFaction.HasValue && targetFaction != Faction.Pirate)))
            return false;

        // Fauna/Bandit projectiles should not hit each other
        if (attackerFaction is Faction.Fauna or Faction.Bandit &&
            targetFaction is Faction.Fauna or Faction.Bandit)
            return false;

        return true;
    }
}
