using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Centralizes faction interaction rules for combat.
/// Call <see cref="SetPlayerReputation"/> once per frame before running combat systems.
/// </summary>
public static class FactionRules
{
    /// <summary>
    /// The local player's reputation, set each frame by the simulation.
    /// When null, original rules apply (backward-compatible for tests/server).
    /// </summary>
    private static FactionReputation? _playerReputation;

    /// <summary>Set the player's reputation for this frame's combat checks.</summary>
    public static void SetPlayerReputation(FactionReputation? reputation)
    {
        _playerReputation = reputation;
    }

    /// <summary>
    /// Get the player's current reputation level with a faction.
    /// Returns Neutral if no reputation is set (e.g. server/tests).
    /// </summary>
    public static ReputationLevel PlayerReputationLevel(Faction faction)
    {
        return _playerReputation?.GetLevel(faction) ?? ReputationLevel.Neutral;
    }

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

        // ── Reputation-based overrides ──────────────────────────────
        if (_playerReputation != null)
        {
            // Pirates with Friendly+ standing don't attack the player
            if (attackerFaction == Faction.Pirate && targetFaction == Faction.Player
                && _playerReputation.GetLevel(Faction.Pirate) >= ReputationLevel.Friendly)
                return false;

            // Patrols attack the player when standing is Hostile
            if (attackerFaction == Faction.Patrol && targetFaction == Faction.Player
                && _playerReputation.GetLevel(Faction.Patrol) == ReputationLevel.Hostile)
                return true;
        }

        // Patrol/trader projectiles only hit pirates
        if (attackerFaction is Faction.Patrol or Faction.Trader &&
            (targetFaction == Faction.Player || (targetFaction.HasValue && targetFaction != Faction.Pirate)))
            return false;

        // On surfaces, pirate projectiles should not hit other pirates (handled by same-faction check above)
        // Patrol/trader projectiles only hit pirates (handled above)

        return true;
    }
}
