using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Builds a unique string key identifying a specific trade location (station or settlement).
/// </summary>
public static class LocationKey
{
    public static string ForStation(int systemIndex, int stationIndex) => $"S:{systemIndex}:{stationIndex}";
    public static string ForSettlement(int systemIndex, int planetIndex, int settlementIndex) => $"T:{systemIndex}:{planetIndex}:{settlementIndex}";

    internal static int GetSeedOffset(string locationKey)
    {
        int hash = 0;
        foreach (char c in locationKey)
            hash = hash * 31 + c;
        return hash & 0x7FFFFFFF;
    }
}

/// <summary>
/// Generates deterministic per-system price multipliers for resources.
/// Systems with asteroid belts have lower resource prices (supply),
/// systems without pay more (demand).
/// </summary>
public static class SystemEconomy
{
    private const float MinMultiplier = 0.5f;
    private const float MaxMultiplier = 2.0f;

    /// <summary>
    /// Get the price multiplier for a resource in a specific system.
    /// Returns a value between 0.5 and 2.0.
    /// </summary>
    public static float GetPriceMultiplier(SeedManager seeds, int systemIndex, ResourceType resource)
    {
        // Deterministic per-system per-resource multiplier
        var rng = new SeededRandom(seeds.GetStarSystemRandom(systemIndex).DeriveChildSeed(7000 + (int)resource));
        float baseMultiplier = rng.NextFloat(MinMultiplier, MaxMultiplier);

        // Supply/demand adjustment: systems with asteroid belts have lower prices
        // (more supply), systems without have higher prices (more demand)
        float supplyDemand = GetSupplyDemandFactor(seeds, systemIndex);

        float final = baseMultiplier * supplyDemand;
        return Math.Clamp(final, MinMultiplier, MaxMultiplier);
    }

    /// <summary>
    /// Get the actual sell price per unit for a resource in a specific system.
    /// </summary>
    public static int GetSellPrice(SeedManager seeds, int systemIndex, ResourceType resource)
    {
        float multiplier = GetPriceMultiplier(seeds, systemIndex, resource);
        int baseValue = ResourceCatalog.Get(resource).ValuePerUnit;
        return Math.Max(1, (int)MathF.Round(baseValue * multiplier));
    }

    /// <summary>
    /// Get the buy price per unit for a resource in a specific system (system-level estimate).
    /// Buy price is 20% higher than sell price (station markup).
    /// </summary>
    public static int GetBuyPrice(SeedManager seeds, int systemIndex, ResourceType resource)
    {
        int sellPrice = GetSellPrice(seeds, systemIndex, resource);
        return Math.Max(2, (int)MathF.Ceiling(sellPrice * 1.2f));
    }

    // ── Per-location overloads (station/settlement specific) ──

    /// <summary>Get the sell price at a specific station or settlement.</summary>
    public static int GetSellPrice(SeedManager seeds, int systemIndex, string locationKey, ResourceType resource)
    {
        int systemPrice = GetSellPrice(seeds, systemIndex, resource);
        float variation = GetLocationVariation(seeds, systemIndex, locationKey, resource);
        return Math.Max(1, (int)MathF.Round(systemPrice * variation));
    }

    /// <summary>Get the buy price at a specific station or settlement (20% markup over local sell).</summary>
    public static int GetBuyPrice(SeedManager seeds, int systemIndex, string locationKey, ResourceType resource)
    {
        int sellPrice = GetSellPrice(seeds, systemIndex, locationKey, resource);
        return Math.Max(2, (int)MathF.Ceiling(sellPrice * 1.2f));
    }

    /// <summary>Get the max stock for a resource at a specific station or settlement.</summary>
    public static int GetMaxStock(SeedManager seeds, int systemIndex, string locationKey, ResourceType resource)
    {
        int locSeed = LocationKey.GetSeedOffset(locationKey);
        var rng = new SeededRandom(
            new SeededRandom(seeds.GetStarSystemRandom(systemIndex).DeriveChildSeed(locSeed))
                .DeriveChildSeed(9000 + (int)resource));
        int baseStock = rng.NextInt(5, 30);

        float supplyFactor = GetSupplyDemandFactor(seeds, systemIndex);
        if (supplyFactor < 1.0f)
            baseStock = (int)(baseStock * 1.5f);

        return baseStock;
    }

    /// <summary>±15% price variation per location within a system.</summary>
    private static float GetLocationVariation(SeedManager seeds, int systemIndex, string locationKey, ResourceType resource)
    {
        int locSeed = LocationKey.GetSeedOffset(locationKey);
        var rng = new SeededRandom(
            new SeededRandom(seeds.GetStarSystemRandom(systemIndex).DeriveChildSeed(locSeed))
                .DeriveChildSeed(7500 + (int)resource));
        return rng.NextFloat(0.85f, 1.15f);
    }

    /// <summary>
    /// Get the total sell value of a resource stack in a specific system.
    /// </summary>
    public static int GetSellValue(SeedManager seeds, int systemIndex, ResourceType resource, int amount)
    {
        return GetSellPrice(seeds, systemIndex, resource) * amount;
    }

    /// <summary>
    /// Find the best system to sell a resource (highest price) among known systems.
    /// Returns (systemIndex, pricePerUnit) pairs sorted by price descending.
    /// </summary>
    public static List<(int SystemIndex, string SystemName, int PricePerUnit)> GetBestSellSystems(
        SeedManager seeds, List<StarSystemData> galaxyData, ResourceType resource, int currentSystem, int maxResults = 3)
    {
        var results = new List<(int SystemIndex, string SystemName, int PricePerUnit)>();

        foreach (var system in galaxyData)
        {
            if (system.Index == currentSystem) continue;
            int price = GetSellPrice(seeds, system.Index, resource);
            results.Add((system.Index, system.Name, price));
        }

        results.Sort((a, b) => b.PricePerUnit.CompareTo(a.PricePerUnit));
        if (results.Count > maxResults)
            results.RemoveRange(maxResults, results.Count - maxResults);

        return results;
    }

    /// <summary>
    /// Supply/demand factor based on system properties.
    /// Systems with asteroid belts (mining supply) get a factor below 1.0 for mineable resources.
    /// Systems with more planets/stations (demand) get a factor above 1.0.
    /// </summary>
    private static float GetSupplyDemandFactor(SeedManager seeds, int systemIndex)
    {
        var systemRng = seeds.GetStarSystemRandom(systemIndex);
        // Regenerate system content to check for asteroid belts
        // We use the same seed derivation as SolarSystemGenerator to check belt presence
        // without generating the full system content.
        var checkRng = new SeededRandom(systemRng.DeriveChildSeed(8000));

        // Derive belt presence from the system seed (mirrors SolarSystemGenerator logic):
        // 50% chance if planet count >= 4
        var galaxyRng = seeds.GetGalaxyRandom();
        // We need PlanetCount — derive it the same way GalaxyGenerator does
        var sysDataRng = new SeededRandom(galaxyRng.DeriveChildSeed(systemIndex));
        // Skip name generation (consume some state)
        for (int i = 0; i < 4; i++) sysDataRng.NextInt(100);
        int planetCount = sysDataRng.NextInt(3, 9);

        // Use checkRng to deterministically decide belt presence per system
        bool hasAsteroidBelt = checkRng.NextBool(0.5f) && planetCount >= 4;

        if (hasAsteroidBelt)
        {
            // Supply: prices are lower (0.7-0.9x) for mineable resources
            return 0.7f + checkRng.NextFloat(0f, 0.2f);
        }
        else
        {
            // Demand: prices are higher (1.1-1.3x) for resources
            return 1.1f + checkRng.NextFloat(0f, 0.2f);
        }
    }
}
