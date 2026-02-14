# Space Exploration Game - Architecture Documentation

## Overview
A 2D procedural space exploration game built with C# (.NET 10), SDL3 (via SDL3-CS), and Arch ECS.

## Tech Stack
- **Runtime**: .NET 10
- **Rendering**: SDL3 via [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) NuGet package
- **ECS**: [Arch ECS](https://github.com/genaray/Arch) v2.1.0
- **Graphics**: Currently procedural colored shapes (rectangles, circles, lines) with a minimal bitmap font. Future: pixel art sprites.

## Project Structure

```
SpaceExplorationGame/
├── SpaceExplorationGame.csproj    # .NET 10 project file
├── Program.cs                     # Entry point
├── Core/
│   ├── Game.cs                    # Main game class (SDL window, ECS world, game loop)
│   ├── GameConfig.cs              # All tunable constants
│   ├── GameState.cs               # Abstract base class for game states
│   ├── Camera.cs                  # 2D camera with zoom and viewport
│   ├── InputManager.cs            # Input state tracking (keys, mouse)
│   └── PlayerData.cs              # Persistent player data across state changes
├── ECS/
│   └── Components/
│       └── Components.cs          # All ECS component structs
├── Generation/
│   ├── SeededRandom.cs            # Deterministic xorshift64 PRNG
│   ├── SeedManager.cs             # Hierarchical seed derivation
│   ├── GalaxyGenerator.cs         # Galaxy star system placement & properties
│   ├── SolarSystemGenerator.cs    # Planets, moons, asteroids, stations
│   └── PlanetSurfaceGenerator.cs  # Terrain tilemap generation
├── Rendering/
│   ├── SpriteRenderer.cs          # SDL3 rendering abstraction
│   └── MiniBitmapFont.cs          # Built-in 5x8 pixel font
└── States/
    ├── GalaxyMapState.cs          # Galaxy overview with star system selection
    ├── SolarSystemState.cs        # Space flight within a solar system
    ├── SpaceStationState.cs       # Menu-based space station interaction
    └── PlanetSurfaceState.cs      # Planet surface exploration (tilemap)
```

## Architecture Decisions

### Game States
The game uses a state machine pattern. Each state (`GameState` subclass) owns its logic and rendering. When switching states, all ECS entities are destroyed and recreated — this keeps things simple and avoids stale entity issues.

States:
- **GalaxyMapState**: Bird's-eye view of the galaxy. Click star systems, press Enter to travel.
- **SolarSystemState**: Real-time flight. Player controls ship with WASD. Orbiting planets/moons/stations. Press E near planets/stations to interact.
- **SpaceStationState**: Menu-based UI when docked. Ship customization, missions, etc.
- **PlanetSurfaceState**: Tilemap exploration. Player avatar walks on generated terrain. Press E near ship to board.

### Procedural Generation Seed Hierarchy
```
Galaxy Seed (user-provided or random)
├── Star System 0 seed (derived from galaxy seed + index 0)
│   ├── Planet 0 seed (derived from system seed + index 0)
│   │   └── Surface seed (derived from planet seed + 1000)
│   ├── Planet 1 seed ...
│   └── ...
├── Star System 1 seed ...
└── ...
```
All RNG uses `SeededRandom` (xorshift64) — fully deterministic. Same galaxy seed = same universe every time.

### ECS Usage (Arch)
Components are plain structs defined in `Components.cs`. The game uses Arch's `World.Query()` with lambda syntax for iteration. Key component types:
- `Transform` — position + rotation
- `Velocity` — movement
- `Sprite` — rendering info (currently colored rects)
- `CelestialBody` — star/planet/moon/station properties
- `Orbit` — orbital mechanics (parent entity, radius, speed, angle)
- `PlayerControlled` — tag for player entity
- `Label` — text displayed near entity
- `Interactable` — landing/docking capability

### Rendering
Currently all rendering uses SDL3 draw primitives (filled rects, circles made of scanlines, lines). The `SpriteRenderer` class provides world-space (camera-transformed) and screen-space drawing methods. A built-in `MiniBitmapFont` renders text without requiring TTF files.

### Camera
The `Camera` class handles world-to-screen coordinate conversion with zoom support. Scrollable tilemaps render only visible tiles using `GetVisibleBounds()`.

## Controls

### Galaxy Map
- WASD/Arrows: Pan camera
- Mouse Scroll: Zoom
- Click: Select star system
- Enter: Travel to selected system

### Solar System
- W/Up: Thrust forward
- A/D or Left/Right: Rotate ship
- S/Down: Brake
- Mouse Scroll: Zoom
- E: Interact (land on planet / dock at station)
- M: Return to galaxy map

### Space Station
- Up/Down or W/S: Navigate menu
- Enter: Confirm selection
- Escape: Exit station

### Planet Surface
- WASD/Arrows: Move avatar
- Mouse Scroll: Zoom
- E: Board ship (when near)
- Escape: Leave planet (quick exit)

## Build & Run
```bash
dotnet build
dotnet run
dotnet run -- 12345  # with specific galaxy seed
```

## Current Status (v0.1 - Vertical Slice)
- [x] SDL3 window and game loop (fixed timestep 60fps)
- [x] Arch ECS integration
- [x] Camera with zoom and scrolling
- [x] Deterministic procedural generation (seed system)
- [x] Galaxy map with 40-80 star systems
- [x] Solar system with orbiting planets, moons, asteroids, stations
- [x] Player ship flight with physics
- [x] Space station docking (menu UI)
- [x] Planet surface landing (generated tilemap terrain)
- [x] Full game loop: Galaxy → Solar System → Station/Planet → back

## TODO / Next Steps
- [ ] Pixel art sprites (replace colored shapes)
- [ ] Player vehicle for planet exploration
- [ ] Ship customization system (parts, weapons, shields)
- [ ] Ship/vehicle/avatar upgrade shop in stations
- [ ] Mission system
- [ ] Combat system
- [ ] Fuel consumption and management
- [ ] Sound effects and music (SDL_Mixer)
- [ ] Save/load game
- [ ] Settlement interior (basic 2D map in stations/settlements)
- [ ] FTL travel animation
- [ ] Asteroid mining/collection
- [ ] Multiple ship types
