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

            // 20% chance to generate a chained mission instead of a standalone one
            Mission mission;
            if (rng.NextFloat() < 0.2f)
            {
                mission = GenerateChainedMission(rng, missionId, currentSystem, otherSystems);
            }
            else
            {
                mission = missionType switch
                {
                    MissionType.Delivery => GenerateDeliveryMission(rng, missionId, currentSystem, otherSystems),
                    MissionType.Mining => GenerateMiningMission(rng, missionId, currentSystem, otherSystems),
                    MissionType.BountyHunt => GenerateBountyMission(rng, missionId, currentSystem, otherSystems),
                    MissionType.Exploration => GenerateExplorationMission(rng, seeds, missionId, currentSystem, otherSystems),
                    MissionType.Patrol => GeneratePatrolMission(rng, missionId, currentSystem, otherSystems),
                    MissionType.SettlementDelivery => GenerateSettlementDeliveryMission(rng, seeds, missionId, currentSystem, otherSystems),
                    _ => GeneratePatrolMission(rng, missionId, currentSystem, otherSystems)
                };
            }

            missions.Add(mission);
        }

        return missions;
    }

    /// <summary>Compute a deterministic board seed for a station.</summary>
    public static ulong GetSpaceStationBoardSeed(SeedManager seeds, int systemIndex, int spaceStationIndex)
    {
        var systemRng = seeds.GetStarSystemRandom(systemIndex);
        return systemRng.DeriveChildSeed(4000 + spaceStationIndex);
    }

    /// <summary>Compute a deterministic board seed for a settlement.</summary>
    public static ulong GetSettlementBoardSeed(SeedManager seeds, int systemIndex, int planetIndex, int settlementX, int settlementY)
    {
        var surfaceRng = seeds.GetPlanetSurfaceRandom(systemIndex, planetIndex);
        return surfaceRng.DeriveChildSeed(5000 + settlementX * 100 + settlementY);
    }

    private static Mission GenerateChainedMission(SeededRandom rng, int baseId, StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        // Chain patterns: Delivery→Bounty (2 steps) or Delivery→Mining→Return (3 steps)
        int chainId = baseId;
        bool threeStep = rng.NextFloat() < 0.4f;
        int totalSteps = threeStep ? 3 : 2;

        var targetSystem = rng.Pick(otherSystems);
        int baseReward = 200 + currentSystem.DangerLevel * 80;

        if (threeStep)
        {
            // Step 3: Return delivery to origin (final step, no NextChainMission)
            int step3Id = baseId ^ 0x30000;
            int step3Reward = (int)(baseReward * 2.0f) + rng.NextInt(200, 500);
            var step3 = new Mission
            {
                Id = step3Id,
                Title = "Chain: Return Report",
                Description = $"Return to {currentSystem.Name} to deliver your report and collect the full reward.",
                Type = MissionType.Delivery,
                Target = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                CreditReward = step3Reward,
                RequiredAmount = 1,
                ChainId = chainId,
                ChainStep = 2,
                ChainTotal = totalSteps,
            };

            // Step 2: Mine resources in the target system
            var resource = (ResourceType)rng.NextInt(0, Enum.GetValues<ResourceType>().Length);
            var resInfo = ResourceCatalog.Get(resource);
            int amount = rng.NextInt(5, 10);
            int step2Id = baseId ^ 0x20000;
            int step2Reward = baseReward + rng.NextInt(100, 300);
            var step2 = new Mission
            {
                Id = step2Id,
                Title = $"Chain: Mine {resInfo.Name}",
                Description = $"Mine {amount} units of {resInfo.Name} in the {targetSystem.Name} system.",
                Type = MissionType.Mining,
                Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
                TurnIn = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
                Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                TargetResource = resource,
                RequiredAmount = amount,
                CreditReward = step2Reward,
                ChainId = chainId,
                ChainStep = 1,
                ChainTotal = totalSteps,
                NextChainMission = step3,
            };

            // Step 1: Deliver to target system
            int step1Reward = baseReward + rng.NextInt(50, 200);
            return new Mission
            {
                Id = baseId,
                Title = $"Contract: {targetSystem.Name} Operation",
                Description = $"Travel to {targetSystem.Name} to begin a multi-part contract. Dock at any station there.",
                Type = MissionType.Delivery,
                Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
                TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                CreditReward = step1Reward,
                RequiredAmount = 1,
                ChainId = chainId,
                ChainStep = 0,
                ChainTotal = totalSteps,
                NextChainMission = step2,
            };
        }
        else
        {
            // 2-step: Delivery→Bounty
            int killCount = rng.NextInt(2, 4 + currentSystem.DangerLevel);
            int step2Id = baseId ^ 0x20000;
            int step2Reward = killCount * 250 + rng.NextInt(200, 500);
            var step2 = new Mission
            {
                Id = step2Id,
                Title = $"Chain: Hunt {killCount} Pirates",
                Description = $"Destroy {killCount} pirates in the {targetSystem.Name} system to complete the contract.",
                Type = MissionType.BountyHunt,
                Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
                TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                RequiredAmount = killCount,
                CreditReward = step2Reward,
                ChainId = chainId,
                ChainStep = 1,
                ChainTotal = totalSteps,
            };

            // Step 1: Travel to target
            int step1Reward = baseReward + rng.NextInt(50, 200);
            return new Mission
            {
                Id = baseId,
                Title = $"Contract: {targetSystem.Name} Bounty",
                Description = $"Travel to {targetSystem.Name} to begin a bounty contract. Dock at any station there.",
                Type = MissionType.Delivery,
                Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
                TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
                CreditReward = step1Reward,
                RequiredAmount = 1,
                ChainId = chainId,
                ChainStep = 0,
                ChainTotal = totalSteps,
                NextChainMission = step2,
            };
        }
    }

    private static MissionType PickMissionType(SeededRandom rng)
    {
        float roll = rng.NextFloat();
        return roll switch
        {
            < 0.15f => MissionType.Delivery,
            < 0.28f => MissionType.Mining,
            < 0.42f => MissionType.BountyHunt,
            < 0.55f => MissionType.Exploration,
            < 0.68f => MissionType.Patrol,
            _ => MissionType.SettlementDelivery
        };
    }

    private static Mission GenerateDeliveryMission(SeededRandom rng, int id, StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        var targetSystem = rng.Pick(otherSystems);
        int baseReward = 300 + currentSystem.DangerLevel * 100;
        int reward = rng.NextInt(baseReward, baseReward + 400);

        // 40% chance to be timed
        float deadline = 0;
        string description = $"Deliver supplies to a station in the {targetSystem.Name} system. Dock at any station there to complete.";
        if (rng.NextFloat() < 0.4f)
        {
            deadline = 600f; // 10 minutes base
            reward = (int)(reward * 1.35f);
            description += " TIME LIMIT!";
        }

        return new Mission
        {
            Id = id,
            Title = $"Supply Run to {targetSystem.Name}",
            Description = description,
            Type = MissionType.Delivery,
            Target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name),
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            CreditReward = reward,
            RequiredAmount = 1,
            DeadlineSeconds = deadline,
        };
    }

    private static Mission GenerateMiningMission(SeededRandom rng, int id, StarSystemData currentSystem,
        List<StarSystemData> otherSystems)
    {
        var resource = (ResourceType)rng.NextInt(0, Enum.GetValues<ResourceType>().Length);
        var resInfo = ResourceCatalog.Get(resource);
        int amount = rng.NextInt(5, 15 + currentSystem.DangerLevel * 3);
        int reward = amount * resInfo.ValuePerUnit + rng.NextInt(100, 300);

        // 50% chance to be location-specific (mine in a target system)
        bool locationSpecific = otherSystems.Count > 0 && rng.NextFloat() < 0.5f;
        GalaxyLocation target;
        string description;

        if (locationSpecific)
        {
            var targetSystem = rng.Pick(otherSystems);
            target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name);
            description = $"Mine {amount} units of {resInfo.Name} in the {targetSystem.Name} system.";
            reward = (int)(reward * 1.3f); // bonus for location constraint
        }
        else
        {
            target = GalaxyLocation.None;
            description = $"Mine {amount} units of {resInfo.Name}. Mine asteroids or surface rocks anywhere.";
        }

        return new Mission
        {
            Id = id,
            Title = $"Mining Contract: {resInfo.Name}",
            Description = description,
            Type = MissionType.Mining,
            Target = target,
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            TargetResource = resource,
            RequiredAmount = amount,
            CreditReward = reward
        };
    }

    private static Mission GenerateBountyMission(SeededRandom rng, int id, StarSystemData currentSystem,
        List<StarSystemData> otherSystems)
    {
        int killCount = rng.NextInt(2, 4 + currentSystem.DangerLevel);
        int reward = killCount * 200 + currentSystem.DangerLevel * 150 + rng.NextInt(0, 300);

        // 60% chance to be location-specific (hunt in a target system)
        bool locationSpecific = otherSystems.Count > 0 && rng.NextFloat() < 0.6f;
        GalaxyLocation target;
        string description;

        if (locationSpecific)
        {
            var targetSystem = rng.Pick(otherSystems);
            target = GalaxyLocation.ForSystem(targetSystem.Index, targetSystem.Name);
            description = $"Destroy {killCount} pirate ships in the {targetSystem.Name} system.";
            reward = (int)(reward * 1.25f); // bonus for location constraint
        }
        else
        {
            target = GalaxyLocation.None;
            description = $"Destroy {killCount} pirate ships anywhere in the galaxy.";
        }

        // 30% chance to be timed (delivery-style urgency)
        float deadline = 0;
        if (rng.NextFloat() < 0.3f)
        {
            deadline = 300f + killCount * 60f; // 5+ minutes base, +1 min per kill
            reward = (int)(reward * 1.4f); // time pressure bonus
            description += " TIME LIMIT!";
        }

        return new Mission
        {
            Id = id,
            Title = locationSpecific ? $"Bounty: {killCount} Pirates" : $"Pirate Bounty: {killCount} Ships",
            Description = description,
            Type = MissionType.BountyHunt,
            Target = target,
            TurnIn = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            Origin = GalaxyLocation.ForSystem(currentSystem.Index, currentSystem.Name),
            RequiredAmount = killCount,
            CreditReward = reward,
            DeadlineSeconds = deadline,
        };
    }

    private static Mission GenerateExplorationMission(SeededRandom rng, SeedManager seeds,
        int id, StarSystemData currentSystem, List<StarSystemData> otherSystems)
    {
        // Pick a target system and find a landable planet
        var targetSystem = rng.Pick(otherSystems);

        // Generate only planet data (skip stations, NPCs, asteroids)
        var sysRng = seeds.GetStarSystemRandom(targetSystem.Index);
        var planets = SolarSystemGenerator.GeneratePlanetsOnly(sysRng, targetSystem);

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
        var planets = SolarSystemGenerator.GeneratePlanetsOnly(sysRng, targetSystem);

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
