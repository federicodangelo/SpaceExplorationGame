using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

public interface IWorldGenerator
{
    List<StarSystemData> GenerateGalaxy(SeedManager seeds);

    SolarSystemContent GenerateSolarSystem(SeedManager seeds, StarSystemData starSystem);

    PlanetSurfaceData GeneratePlanetSurface(SeedManager seeds, StarSystemData starSystem, PlanetData planet);

    InteriorData GenerateStationInterior(SeedManager seeds, StarSystemData starSystem, SpaceStationData? station);

    InteriorData GenerateSettlementInterior(SeedManager seeds, StarSystemData starSystem, PlanetData? planet, SettlementData? settlement);

    List<Mission> GenerateBoardMissions(SeedManager seeds, ulong boardSeed, StarSystemData currentSystem, List<StarSystemData> galaxySystems);
}
