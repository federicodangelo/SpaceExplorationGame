using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

public class ProceduralUniverseGenerator : IUniverseGenerator
{
    public SeedManager Seeds { get; }

    public ProceduralUniverseGenerator(SeedManager seeds)
    {
        Seeds = seeds;
    }

    public virtual List<StarSystemData> GenerateGalaxy()
    {
        return GalaxyGenerator.Generate(Seeds.GetGalaxyRandom());
    }

    public virtual SolarSystemContent GenerateSolarSystem(StarSystemData starSystem)
    {
        var rng = Seeds.GetStarSystemRandom(starSystem.Index);
        return SolarSystemGenerator.Generate(rng, starSystem);
    }

    public virtual PlanetSurfaceData GeneratePlanetSurface(StarSystemData starSystem, PlanetData planet)
    {
        var rng = Seeds.GetPlanetSurfaceRandom(starSystem.Index, planet.Index);
        return PlanetSurfaceGenerator.Generate(rng, planet, starSystem.DangerLevel);
    }

    public virtual InteriorData GenerateStationInterior(StarSystemData starSystem, SpaceStationData? station)
    {
        var rng = new SeededRandom(
            Seeds.GetStarSystemRandom(starSystem.Index).DeriveChildSeed(2000 + (station?.Index ?? 0)));
        return InteriorGenerator.GenerateStation(rng, station?.Name ?? "SPACE STATION");
    }

    public virtual InteriorData GenerateSettlementInterior(StarSystemData starSystem, PlanetData? planet, SettlementData? settlement)
    {
        var rng = new SeededRandom(
            Seeds.GetPlanetSurfaceRandom(starSystem.Index, planet?.Index ?? 0)
                .DeriveChildSeed(3000 + (settlement?.TileRect.X ?? 0) * 100 + (settlement?.TileRect.Y ?? 0)));
        return InteriorGenerator.GenerateSettlement(rng, settlement?.Name ?? "SETTLEMENT");
    }

    public virtual List<Mission> GenerateBoardMissions(ulong boardSeed, StarSystemData currentSystem, List<StarSystemData> galaxySystems)
    {
        return MissionGenerator.GenerateBoardMissions(Seeds, boardSeed, currentSystem, galaxySystems);
    }
}
