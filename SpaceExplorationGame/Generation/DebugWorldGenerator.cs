using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation;

public enum DebugGenerationScenario
{
    PlanetTypeShowcase,
    StarTypeShowcase,
}

public class DebugWorldGenerator : ProceduralWorldGenerator
{
    private readonly DebugGenerationScenario _scenario;
    private readonly StarClass _starClass;

    public DebugWorldGenerator(DebugGenerationScenario scenario, StarClass starClass = StarClass.G)
    {
        _scenario = scenario;
        _starClass = starClass;
    }

    public override List<StarSystemData> GenerateGalaxy(SeedManager seeds)
    {
        var (starColor, starRadius) = GetDebugStarProperties(_scenario == DebugGenerationScenario.PlanetTypeShowcase ? StarClass.G : _starClass);
        var systemName = _scenario == DebugGenerationScenario.PlanetTypeShowcase
            ? "Render Debug"
            : $"Star Debug {_starClass}";

        return
        [
            new StarSystemData
            {
                Index = 0,
                Name = systemName,
                GalaxyPosition = new Vector2(GameConfig.GalaxyWidth * GameConfig.TileSize / 2f,
                    GameConfig.GalaxyHeight * GameConfig.TileSize / 2f),
                StarClass = _scenario == DebugGenerationScenario.PlanetTypeShowcase ? StarClass.G : _starClass,
                StarRadius = starRadius,
                StarColor = starColor,
                PlanetCount = Enum.GetValues<PlanetType>().Length,
                HasSpaceStation = true,
                DangerLevel = 1,
            }
        ];
    }

    public override SolarSystemContent GenerateSolarSystem(SeedManager seeds, StarSystemData starSystem)
    {
        var planets = BuildPlanetTypeShowcasePlanets();
        var belts = new List<AsteroidBeltData>();
        var stations = new List<SpaceStationData>
        {
            new SpaceStationData
            {
                Index = 0,
                Name = "Debug Station",
                OrbitParentPlanetIndex = -1,
                OrbitRadius = 5200f,
                OrbitSpeed = 0.001f,
                StartAngle = 1.2f,
            }
        };

        return new SolarSystemContent(planets, belts, stations);
    }

    private static ColoredRadius GetDebugStarProperties(StarClass starClass)
    {
        return starClass switch
        {
            StarClass.O => new ColoredRadius(new Color3(100, 140, 255), 170f),
            StarClass.B => new ColoredRadius(new Color3(150, 180, 255), 160f),
            StarClass.A => new ColoredRadius(new Color3(220, 220, 255), 150f),
            StarClass.F => new ColoredRadius(new Color3(255, 255, 220), 145f),
            StarClass.G => new ColoredRadius(new Color3(255, 235, 120), 140f),
            StarClass.K => new ColoredRadius(new Color3(255, 180, 90), 135f),
            StarClass.M => new ColoredRadius(new Color3(255, 105, 70), 130f),
            _ => new ColoredRadius(new Color3(255, 255, 255), 140f),
        };
    }

    private static List<PlanetData> BuildPlanetTypeShowcasePlanets()
    {
        PlanetType[] types = Enum.GetValues<PlanetType>();
        var planets = new List<PlanetData>(types.Length);

        float baseOrbit = 2100f;
        float spacing = 760f;

        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var color = GetDebugPlanetColor(type);
            float radius = type switch
            {
                PlanetType.GasGiant or PlanetType.IceGiant => 215f,
                PlanetType.Terrestrial or PlanetType.Ocean => 175f,
                _ => 155f,
            };

            var moons = new List<MoonData>
            {
                new MoonData
                {
                    Index = 0,
                    Name = $"{type} Moon",
                    OrbitRadius = radius + 330f,
                    OrbitSpeed = 0f,
                    StartAngle = MathF.PI * 0.5f,
                    Radius = 58f,
                    Color = new Color3(175, 175, 185),
                    Type = type,
                }
            };

            planets.Add(new PlanetData
            {
                Index = i,
                Name = type.ToString(),
                Type = type,
                OrbitRadius = baseOrbit + i * spacing,
                OrbitSpeed = 0f,
                StartAngle = 0f,
                Radius = radius,
                Color = color,
                HasSolidSurface = type is not (PlanetType.GasGiant or PlanetType.IceGiant),
                MoonCount = moons.Count,
                HasRings = type is PlanetType.GasGiant or PlanetType.IceGiant,
                Moons = moons,
                HasSettlement = type is PlanetType.Terrestrial or PlanetType.Desert,
            });
        }

        return planets;
    }

    private static Color3 GetDebugPlanetColor(PlanetType type) => type switch
    {
        PlanetType.Rocky => new Color3(155, 135, 115),
        PlanetType.Terrestrial => new Color3(80, 160, 110),
        PlanetType.Desert => new Color3(210, 175, 95),
        PlanetType.GasGiant => new Color3(225, 185, 115),
        PlanetType.IceGiant => new Color3(120, 185, 245),
        PlanetType.Volcanic => new Color3(230, 90, 45),
        PlanetType.Ocean => new Color3(50, 120, 210),
        PlanetType.Frozen => new Color3(205, 225, 245),
        _ => new Color3(180, 180, 180),
    };
}
