using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

public interface IUniverseGenerator
{
    public SeedManager Seeds { get; }

    List<StarSystemData> GenerateGalaxy();

    SolarSystemContent GenerateSolarSystem(StarSystemData starSystem);

    PlanetSurfaceData GeneratePlanetSurface(StarSystemData starSystem, PlanetData planet);

    InteriorData GenerateStationInterior(StarSystemData starSystem, SpaceStationData? station);

    InteriorData GenerateSettlementInterior(StarSystemData starSystem, PlanetData? planet, SettlementData? settlement);

    List<Mission> GenerateBoardMissions(ulong boardSeed, StarSystemData currentSystem, List<StarSystemData> galaxySystems);
}
