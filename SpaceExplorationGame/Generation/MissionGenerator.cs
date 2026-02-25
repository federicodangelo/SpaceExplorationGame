using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Generates deterministic missions for mission boards at stations and settlements.
/// Each board produces a fixed pool of missions based on its seed. 
/// Already-accepted or completed missions are filtered out by the caller.
/// </summary>
public static class MissionGenerator
{
    private const int MissionsPerBoard = 5;

    /// <summary>
    /// Generate the pool of missions available at a specific mission board.
    /// </summary>
    /// <param name="seeds">Seed manager for deterministic generation.</param>
    /// <param name="boardSeed">Unique seed for this mission board (derived from station/settlement).</param>
    /// <param name="currentSystem">The star system where the board is located.</param>
    /// <param name="galaxySystems">All star systems in the galaxy.</param>
    /// <returns>List of missions with status = Available.</returns>
    public static List<Mission> GenerateBoardMissions(
        SeedManager seeds,
        ulong boardSeed,
        StarSystemData currentSystem,
        List<StarSystemData> galaxySystems)
    {
        var rng = new SeededRandom(boardSeed);
        var missions = new List<Mission>(MissionsPerBoard);

        // Collect valid target systems (not the current one)
        var otherSystems = galaxySystems.Where(s => s.Index != currentSystem.Index).ToList();
        if (otherSystems.Count == 0) return missions;

        for (int i = 0; i < MissionsPerBoard; i++)
        {
            // Deterministic ID from board seed + index
            int missionId = (int)(boardSeed ^ (ulong)(i * 7919 + 31)) & 0x7FFFFFFF;

            var missionType = PickMissionType(rng);
            var mission = missionType switch
            {
                MissionType.Delivery => GenerateDeliveryMission(rng, missionId, currentSystem, otherSystems),
                MissionType.Mining => GenerateMiningMission(rng, missionId, currentSystem),
                MissionType.BountyHunt => GenerateBountyMission(rng, missionId, currentSystem),
                MissionType.Exploration => GenerateExplorationMission(rng, seeds, missionId, currentSystem, otherSystems),
                MissionType.Patrol => GeneratePatrolMission(rng, missionId, currentSystem, otherSystems),
                MissionType.SettlementDelivery => GenerateSettlementDeliveryMission(rng, seeds, missionId, currentSystem, otherSystems),
                _ => GeneratePatrolMission(rng, missionId, currentSystem, otherSystems)
            };

            missions.Add(mission);
        }

        return missions;
    }

    /// <summary>Compute a deterministic board seed for a station.</summary>
    public static ulong GetStationBoardSeed(SeedManager seeds, int systemIndex, int stationIndex)
    {
        var systemRng = seeds.GetStarSystemRandom(systemIndex);
        return systemRng.DeriveChildSeed(4000 + stationIndex);
    }

    /// <summary>Compute a deterministic board seed for a settlement.</summary>
    public static ulong GetSettlementBoardSeed(SeedManager seeds, int systemIndex, int planetIndex, int settlementX, int settlementY)
    {
        var surfaceRng = seeds.GetPlanetSurfaceRandom(systemIndex, planetIndex);
        return surfaceRng.DeriveChildSeed(5000 + settlementX * 100 + settlementY);
    }

    private static MissionType PickMissionType(SeededRandom rng)
    {
        float roll = rng.NextFloat();
        return roll switch
        {
            < 0.20f => MissionType.Delivery,
            < 0.35f => MissionType.Mining,
            < 0.50f => MissionType.BountyHunt,
            < 0.65f => MissionType.Exploration,
            < 0.80f => MissionType.Patrol,
            _ => MissionType.SettlementDelivery
        };
    }

    private static Mission GenerateDeliveryMission(SeededRandom rng, int id, StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        var targetSystem = rng.Pick(otherSystems);
        int baseReward = 300 + currentSystem.DangerLevel * 100;
        int reward = rng.NextInt(baseReward, baseReward + 400);

        return new Mission
        {
            Id = id,
            Title = $"Supply Run to {targetSystem.Name}",
            Description = $"Deliver supplies to a station in the {targetSystem.Name} system. Dock at any station there to complete.",
            Type = MissionType.Delivery,
            Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            CreditReward = reward,
            RequiredAmount = 1
        };
    }

    private static Mission GenerateMiningMission(SeededRandom rng, int id, StarSystemData currentSystem)
    {
        var resource = (ResourceType)rng.NextInt(0, Enum.GetValues<ResourceType>().Length);
        var resInfo = ResourceCatalog.Get(resource);
        int amount = rng.NextInt(5, 15 + currentSystem.DangerLevel * 3);
        int reward = amount * resInfo.ValuePerUnit + rng.NextInt(100, 300);

        return new Mission
        {
            Id = id,
            Title = $"Mining Contract: {resInfo.Name}",
            Description = $"Mine {amount} units of {resInfo.Name}. Mine asteroids or surface rocks anywhere.",
            Type = MissionType.Mining,
            Target = GalaxyLocation.None,
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            TargetResource = resource,
            RequiredAmount = amount,
            CreditReward = reward
        };
    }

    private static Mission GenerateBountyMission(SeededRandom rng, int id, StarSystemData currentSystem)
    {
        int killCount = rng.NextInt(2, 4 + currentSystem.DangerLevel);
        int reward = killCount * 200 + currentSystem.DangerLevel * 150 + rng.NextInt(0, 300);

        return new Mission
        {
            Id = id,
            Title = $"Pirate Bounty: {killCount} Ships",
            Description = $"Destroy {killCount} pirate ships anywhere in the galaxy.",
            Type = MissionType.BountyHunt,
            Target = GalaxyLocation.None,
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            RequiredAmount = killCount,
            CreditReward = reward
        };
    }

    private static Mission GenerateExplorationMission(SeededRandom rng, SeedManager seeds,
        int id, StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        // Pick a target system and find a landable planet
        var targetSystem = rng.Pick(otherSystems);

        // Generate that system's planets to find a valid target
        var sysRng = seeds.GetStarSystemRandom(targetSystem.Index);
        var (planets, _, _) = SolarSystemGenerator.Generate(sysRng, targetSystem);

        // Look for solid-surface planets
        var landable = planets.Where(p => p.HasSolidSurface).ToList();
        if (landable.Count == 0)
        {
            // Fallback to patrol mission
            return GeneratePatrolMission(rng, id, currentSystem, otherSystems);
        }

        var targetPlanet = rng.Pick(landable);
        int reward = 400 + currentSystem.DangerLevel * 120 + rng.NextInt(100, 400);

        return new Mission
        {
            Id = id,
            Title = $"Explore {targetPlanet.Name}",
            Description = $"Land on {targetPlanet.Name} in the {targetSystem.Name} system.",
            Type = MissionType.Exploration,
            Target = GalaxyLocation.ForPlanet(targetSystem.Index, targetSystem.Name,
                targetPlanet.Index, targetPlanet.Name),
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            RequiredAmount = 1,
            CreditReward = reward
        };
    }

    private static Mission GeneratePatrolMission(SeededRandom rng, int id,
        StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        var targetSystem = rng.Pick(otherSystems);
        int reward = 200 + currentSystem.DangerLevel * 80 + rng.NextInt(50, 250);

        return new Mission
        {
            Id = id,
            Title = $"Patrol {targetSystem.Name}",
            Description = $"Travel to the {targetSystem.Name} system.",
            Type = MissionType.Patrol,
            Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            RequiredAmount = 1,
            CreditReward = reward
        };
    }

    private static Mission GenerateSettlementDeliveryMission(SeededRandom rng, SeedManager seeds,
        int id, StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        // Pick a target system and find a planet with a settlement
        var targetSystem = rng.Pick(otherSystems);

        var sysRng = seeds.GetStarSystemRandom(targetSystem.Index);
        var (planets, _, _) = SolarSystemGenerator.Generate(sysRng, targetSystem);

        // Look for planets with settlements
        var settled = planets.Where(p => p.HasSettlement).ToList();
        if (settled.Count == 0)
        {
            // Fallback to delivery mission
            return GenerateDeliveryMission(rng, id, currentSystem, otherSystems);
        }

        var targetPlanet = rng.Pick(settled);
        int reward = 500 + currentSystem.DangerLevel * 130 + rng.NextInt(100, 500);

        return new Mission
        {
            Id = id,
            Title = $"Settlement Supply: {targetPlanet.Name}",
            Description = $"Deliver supplies to a settlement on {targetPlanet.Name} in the {targetSystem.Name} system. Enter any settlement there.",
            Type = MissionType.SettlementDelivery,
            Target = GalaxyLocation.ForSettlement(targetSystem.Index, targetSystem.Name,
                targetPlanet.Index, targetPlanet.Name),
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            RequiredAmount = 1,
            CreditReward = reward
        };
    }
}
