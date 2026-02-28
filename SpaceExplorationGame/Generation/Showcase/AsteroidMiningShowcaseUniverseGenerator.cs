using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation.Showcase;

public class AsteroidMiningShowcaseUniverseGenerator : ProceduralUniverseGenerator
{
    public AsteroidMiningShowcaseUniverseGenerator(SeedManager seeds) : base(seeds)
    {
    }

    public override List<StarSystemData> GenerateGalaxy()
    {
        return
        [
            ShowcaseUniverseGeneratorHelpers.BuildSingleSystem(
                name: "Asteroid Mining Debug",
                starClass: StarClass.G,
                planetCount: 2)
        ];
    }

    public override SolarSystemContent GenerateSolarSystem(StarSystemData starSystem)
    {
        return new SolarSystemContent(
            Planets: BuildPlanets(),
            AsteroidBelts: BuildBelts(),
            SpaceStations: ShowcaseUniverseGeneratorHelpers.BuildDebugStations(),
            NpcShipSpawns: [],
            StartingPosition: new Vector2(
                GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f - (starSystem.StarRadius * 2f + 100f),
                GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f));
    }

    private static List<AsteroidBeltData> BuildBelts()
    {
        return
        [
            new AsteroidBeltData
            {
                InnerRadius = 900f,
                OuterRadius = 1400f,
                AsteroidCount = 80,
            },
            new AsteroidBeltData
            {
                InnerRadius = 1750f,
                OuterRadius = 2300f,
                AsteroidCount = 100,
            },
        ];
    }

    private static List<PlanetData> BuildPlanets()
    {
        return
        [
            new PlanetData
            {
                Index = 0,
                Name = "Minera",
                Type = PlanetType.Rocky,
                OrbitRadius = 3200f,
                OrbitSpeed = 0f,
                StartAngle = 0f,
                Radius = 165f,
                Color = new Color3(165, 145, 125),
                HasSolidSurface = true,
                MoonCount = 0,
                HasRings = false,
                Moons = [],
                HasSettlement = true,
            },
            new PlanetData
            {
                Index = 1,
                Name = "Garnet",
                Type = PlanetType.Desert,
                OrbitRadius = 4700f,
                OrbitSpeed = 0f,
                StartAngle = MathF.PI * 0.35f,
                Radius = 190f,
                Color = new Color3(205, 165, 100),
                HasSolidSurface = true,
                MoonCount = 0,
                HasRings = false,
                Moons = [],
                HasSettlement = false,
            },
        ];
    }
}
