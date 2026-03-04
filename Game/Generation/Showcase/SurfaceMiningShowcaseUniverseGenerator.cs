using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation.Showcase;

public class SurfaceMiningShowcaseUniverseGenerator : ProceduralUniverseGenerator
{
    private const int TargetRockCount = 420;

    public SurfaceMiningShowcaseUniverseGenerator(SeedManager seeds) : base(seeds)
    {
    }

    public override List<StarSystemData> GenerateGalaxy()
    {
        return
        [
            ShowcaseUniverseGeneratorHelpers.BuildSingleSystem(
                name: "Surface Mining Debug",
                starClass: StarClass.G,
                planetCount: 1)
        ];
    }

    public override SolarSystemContent GenerateSolarSystem(StarSystemData starSystem)
    {
        return new SolarSystemContent(
            Planets:
            [
                new PlanetData
                {
                    Index = 0,
                    Name = "Quarry Prime",
                    Type = PlanetType.Rocky,
                    OrbitRadius = 3200f,
                    OrbitSpeed = 0f,
                    StartAngle = 0f,
                    Radius = 170f,
                    Color = new Color3(165, 145, 125),
                    HasSolidSurface = true,
                    MoonCount = 0,
                    HasRings = false,
                    Moons = [],
                    HasSettlement = false,
                }
            ],
            AsteroidBelts: [],
            SpaceStations: ShowcaseUniverseGeneratorHelpers.BuildDebugStations(),
            NpcSpawnConfig: default,
            StartingPosition: new Vector2(
                GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f - (starSystem.StarRadius * 2f + 100f),
                GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f));
    }

    public override PlanetSurfaceData GeneratePlanetSurface(StarSystemData starSystem, PlanetData planet)
    {
        var surfaceData = base.GeneratePlanetSurface(starSystem, planet);

        if (planet.Index != 0)
            return surfaceData;

        // Override NPC spawn config to have no NPCs — just rocks for the showcase
        surfaceData.NpcSpawnConfig = new SurfaceNpcSpawnConfig(0, 0, 0, 1);

        var rng = new SeededRandom(Seeds.GetPlanetSurfaceRandom(starSystem.Index, planet.Index).DeriveChildSeed(8100));
        var resources = new[] { ResourceType.Iron, ResourceType.Nickel, ResourceType.Gold, ResourceType.Platinum };

        float tileSize = GameConfig.TileSize;
        float landingX = surfaceData.LandingZone.X * tileSize;
        float landingY = surfaceData.LandingZone.Y * tileSize;
        float safeRadius = 5f * tileSize;

        int attempts = 0;
        int maxAttempts = TargetRockCount * 15;

        while (surfaceData.RockSpawns.Count < TargetRockCount && attempts < maxAttempts)
        {
            attempts++;

            int tx = rng.NextInt(3, surfaceData.Width - 3);
            int ty = rng.NextInt(3, surfaceData.Height - 3);
            if (SurfaceTerrainRules.IsBlockedForTraversal(surfaceData.Tiles[tx, ty]))
                continue;

            float worldX = tx * tileSize + tileSize * 0.5f;
            float worldY = ty * tileSize + tileSize * 0.5f;

            float dx = worldX - landingX;
            float dy = worldY - landingY;
            if (dx * dx + dy * dy < safeRadius * safeRadius)
                continue;

            bool tooClose = false;
            for (int i = 0; i < surfaceData.RockSpawns.Count; i++)
            {
                var existing = surfaceData.RockSpawns[i];
                float ex = existing.X - worldX;
                float ey = existing.Y - worldY;
                if (ex * ex + ey * ey < (tileSize * 0.8f) * (tileSize * 0.8f))
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;

            var resource = resources[rng.NextInt(0, resources.Length)];
            int amount = rng.NextInt(GameConfig.SurfaceRockMinResource, GameConfig.SurfaceRockMaxResource + 1);
            float size = GameConfig.SurfaceRockMinSize + rng.NextFloat() * (GameConfig.SurfaceRockMaxSize - GameConfig.SurfaceRockMinSize);
            float hp = GameConfig.SurfaceRockMinHp + rng.NextFloat() * (GameConfig.SurfaceRockMaxHp - GameConfig.SurfaceRockMinHp);

            surfaceData.RockSpawns.Add(new RockSpawn(worldX, worldY, resource, amount, size, hp));
        }

        return surfaceData;
    }
}
