namespace SpaceExplorationGame.Core;

/// <summary>
/// Shared rules for full ship repairs performed at space stations.
/// </summary>
public static class ShipRepairService
{
    public const int RepairCostPerPoint = 2;

    public static float GetDamage(PlayerData player) =>
        Math.Max(0f, player.ShipMaxHealth - player.ShipHealth);

    public static int GetFullRepairCost(PlayerData player) =>
        (int)(GetDamage(player) * RepairCostPerPoint);

    public static bool NeedsRepair(PlayerData player) => GetDamage(player) > 0f;

    public static bool CanAffordFullRepair(PlayerData player)
    {
        int cost = GetFullRepairCost(player);
        return cost > 0 && player.Credits >= cost;
    }

    public static bool TryRepairFull(PlayerData player)
    {
        int cost = GetFullRepairCost(player);
        if (cost <= 0 || player.Credits < cost)
            return false;

        player.Credits -= cost;
        player.ShipHealth = player.ShipMaxHealth;
        return true;
    }
}
