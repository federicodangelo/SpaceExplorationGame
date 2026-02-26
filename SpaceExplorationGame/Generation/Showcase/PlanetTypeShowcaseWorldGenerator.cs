using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation.Showcase;

public class PlanetTypeShowcaseWorldGenerator : ProceduralWorldGenerator
{
    public override List<StarSystemData> GenerateGalaxy(SeedManager seeds)
    {
        return
        [
            ShowcaseWorldGeneratorHelpers.BuildSingleSystem(
                name: "Render Debug",
                starClass: StarClass.G,
                planetCount: Enum.GetValues<PlanetType>().Length)
        ];
    }

    public override SolarSystemContent GenerateSolarSystem(SeedManager seeds, StarSystemData starSystem)
    {
        return new SolarSystemContent(
            Planets: ShowcaseWorldGeneratorHelpers.BuildPlanetTypeShowcasePlanets(),
            AsteroidBelts: [],
            Stations: ShowcaseWorldGeneratorHelpers.BuildDebugStations(),
            NpcShipSpawns: [],
            StartingPosition: new Vector2(
                GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f - (starSystem.StarRadius * 2f + 100f),
                GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f));
    }
}
