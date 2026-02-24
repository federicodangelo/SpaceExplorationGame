using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

public class ProceduralWorldGenerator : IWorldGenerator
{
    public virtual List<StarSystemData> GenerateGalaxy(SeedManager seeds)
    {
        return GalaxyGenerator.Generate(seeds.GetGalaxyRandom());
    }

    public virtual SolarSystemContent GenerateSolarSystem(SeedManager seeds, StarSystemData starSystem)
    {
        var rng = seeds.GetStarSystemRandom(starSystem.Index);
        return SolarSystemGenerator.Generate(rng, starSystem);
    }

    public virtual PlanetSurfaceData GeneratePlanetSurface(SeedManager seeds, StarSystemData starSystem, PlanetData planet)
    {
        var rng = seeds.GetPlanetSurfaceRandom(starSystem.Index, planet.Index);
        return PlanetSurfaceGenerator.Generate(rng, planet);
    }

    public virtual InteriorData GenerateStationInterior(SeedManager seeds, StarSystemData starSystem, SpaceStationData? station)
    {
        var rng = new SeededRandom(
            seeds.GetStarSystemRandom(starSystem.Index).DeriveChildSeed(2000 + (station?.Index ?? 0)));
        return InteriorGenerator.GenerateStation(rng, station?.Name ?? "STATION");
    }

    public virtual InteriorData GenerateSettlementInterior(SeedManager seeds, StarSystemData starSystem, PlanetData? planet, SettlementData? settlement)
    {
        var rng = new SeededRandom(
            seeds.GetPlanetSurfaceRandom(starSystem.Index, planet?.Index ?? 0)
                .DeriveChildSeed(3000 + (settlement?.TileRect.X ?? 0) * 100 + (settlement?.TileRect.Y ?? 0)));
        return InteriorGenerator.GenerateSettlement(rng, settlement?.Name ?? "SETTLEMENT");
    }

    public virtual List<Mission> GenerateBoardMissions(SeedManager seeds, ulong boardSeed, StarSystemData currentSystem, List<StarSystemData> galaxySystems)
    {
        return MissionGenerator.GenerateBoardMissions(seeds, boardSeed, currentSystem, galaxySystems);
    }
}
