using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation.Showcase;

public class PlanetTypeShowcaseUniverseGenerator : ProceduralUniverseGenerator
{
    public PlanetTypeShowcaseUniverseGenerator(SeedManager seeds) : base(seeds)
    {
    }

    public override List<StarSystemData> GenerateGalaxy()
    {
        return
        [
            ShowcaseUniverseGeneratorHelpers.BuildSingleSystem(
                name: "Render Debug",
                starClass: StarClass.G,
                planetCount: Enum.GetValues<PlanetType>().Length)
        ];
    }

    public override SolarSystemContent GenerateSolarSystem(StarSystemData starSystem)
    {
        return new SolarSystemContent(
            Planets: ShowcaseUniverseGeneratorHelpers.BuildPlanetTypeShowcasePlanets(),
            AsteroidBelts: [],
            SpaceStations: ShowcaseUniverseGeneratorHelpers.BuildDebugStations(),
            NpcSpawnConfig: default,
            StartingPosition: new Vector2(
                GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f - (starSystem.StarRadius * 2f + 100f),
                GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f));
    }
}
