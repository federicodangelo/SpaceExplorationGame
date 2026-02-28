using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation.Showcase;

public class StarTypeShowcaseWorldGenerator : ProceduralWorldGenerator
{
    private readonly StarClass _starClass;

    public StarTypeShowcaseWorldGenerator(StarClass starClass)
    {
        _starClass = starClass;
    }

    public override List<StarSystemData> GenerateGalaxy(SeedManager seeds)
    {
        return
        [
            ShowcaseWorldGeneratorHelpers.BuildSingleSystem(
                name: $"Star Debug {_starClass}",
                starClass: _starClass,
                planetCount: Enum.GetValues<PlanetType>().Length)
        ];
    }

    public override SolarSystemContent GenerateSolarSystem(SeedManager seeds, StarSystemData starSystem)
    {
        return new SolarSystemContent(
            Planets: ShowcaseWorldGeneratorHelpers.BuildPlanetTypeShowcasePlanets(),
            AsteroidBelts: [],
            SpaceStations: ShowcaseWorldGeneratorHelpers.BuildDebugStations(),
            NpcShipSpawns: [],
            StartingPosition: new Vector2(
                GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f - (starSystem.StarRadius * 2f + 100f),
                GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f));
    }
}
