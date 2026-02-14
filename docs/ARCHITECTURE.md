# Space Exploration Game - Architecture Documentation

## Overview
A 2D procedural space exploration game built with C# (.NET 10), SDL3 (via SDL3-CS), and Arch ECS.

## Tech Stack
- **Runtime**: .NET 10
- **Rendering**: SDL3 via [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) NuGet package
- **ECS**: [Arch ECS](https://github.com/genaray/Arch) v2.1.0
- **Graphics**: Procedural pixel art textures generated at runtime (sphere-shaded planets, glow-gradient stars, pixel-art ship/avatar/station sprites) plus SDL3 draw primitives and a minimal bitmap font.

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
│   ├── InputManager.cs            # Input state tracking (keys, mouse, edge detection)
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
│   ├── SpriteRenderer.cs          # SDL3 rendering abstraction (primitives + textures)
│   ├── TextureManager.cs          # Procedural pixel art texture generation
│   └── MiniBitmapFont.cs          # Built-in 5x8 pixel font
└── States/
    ├── GalaxyMapState.cs          # Galaxy overview with star system selection
    ├── SolarSystemState.cs        # Space flight within a solar system
    ├── SpaceStationState.cs       # Menu-based space station interaction
    ├── PlanetLandingState.cs      # Orbital landing site selection
    └── PlanetSurfaceState.cs      # Planet surface exploration (tilemap)
```

## Architecture Decisions

### Game States
The game uses a state machine pattern. Each state (`GameState` subclass) owns its logic and rendering. When switching states, all ECS entities are destroyed and recreated — this keeps things simple and avoids stale entity issues.

States:
- **GalaxyMapState**: Bird's-eye view of the galaxy. Click to select star systems, double-click or Enter to travel. Mouse drag to pan. Nebula clouds and glow-textured stars.
- **SolarSystemState**: Real-time flight. Player controls ship with WASD. Orbiting planets/moons/stations rendered with sphere-shaded textures. Press E near planets/stations to interact. Unified nearest-center interaction detection.
- **SpaceStationState**: Menu-based UI when docked. Grid-pattern background, corner-accented frame. Refuels ship on docking.
- **PlanetLandingState**: Orbital view for landing site selection. Shows full terrain map as a texture (1px = 1 tile) with settlement markers. The player clicks to choose a landing site; reticle with terrain info panel shows selected terrain type and position. Supports zoom, pan, WASD cursor nudge. Cannot land on water/lava. Confirms with Enter/E, cancels with Escape.
- **PlanetSurfaceState**: Tilemap exploration with per-tile brightness variation and terrain detail sprites (trees, rocks, water shimmer). Player avatar walks on generated terrain. Lands at the site chosen in PlanetLandingState (or map center by default). Press E near ship to board.

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
The `SpriteRenderer` class provides both SDL3 draw primitives (filled rects, scanline circles, lines) and texture-based rendering with rotation and alpha support. The `TextureManager` generates all game sprites procedurally at startup as SDL textures from pixel arrays:
- **Ship**: Triangular pixel-art sprite with cockpit highlight and engine pods
- **Engine flame**: Orange-yellow gradient cone rendered behind the ship when thrusting
- **Planets/moons**: Sphere-shaded textures with diffuse + specular lighting, surface noise, and edge darkening — unique per body
- **Stars**: Radial gradient glow from white core to star color
- **Stations**: Cross-shaped design with central hub, outer ring, solar panels, and docking indicators; slowly rotates
- **Asteroids**: Irregular rocky blobs with angular distortion
- **Avatar**: Tiny humanoid in green suit with blue visor
- **Landed ship**: Oval hull with cockpit and landing struts

A built-in `MiniBitmapFont` renders text without requiring TTF files. All HUD panels use semi-transparent dark backgrounds for readability.

### Camera
The `Camera` class handles world-to-screen coordinate conversion with zoom support. Scrollable tilemaps render only visible tiles using `GetVisibleBounds()`.

## Controls

### Galaxy Map
- WASD/Arrows/Mouse Drag: Pan camera
- Mouse Scroll: Zoom
- Click: Select star system
- Double-Click/Enter: Travel to selected system

### Solar System
- W/Up: Thrust forward
- A/D or Left/Right: Rotate ship
- S/Down: Brake
- Mouse Scroll: Zoom
- E: Interact (enter orbit view for planets / dock at station)
- M: Return to galaxy map

### Planet Landing (Orbital View)
- Click: Select landing site
- WASD/Arrows: Nudge cursor
- Mouse Scroll: Zoom map
- Left-Drag: Pan map
- Enter/E: Confirm landing
- Escape: Cancel (return to solar system)

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

## Current Status (v0.2 - Graphics & Polish)
- [x] SDL3 window and game loop (fixed timestep 60fps)
- [x] Arch ECS integration
- [x] Camera with zoom and scrolling
- [x] Deterministic procedural generation (seed system)
- [x] Galaxy map with 40-80 star systems (mouse panning, double-click travel, nebula clouds)
- [x] Solar system with orbiting planets, moons, asteroids, stations
- [x] Player ship flight with physics
- [x] Space station docking (menu UI, auto-refuel)
- [x] Planet/moon surface landing (generated tilemap terrain with detail variation)
- [x] Full game loop: Galaxy → Solar System → Station/Planet → back
- [x] FTL fuel system with range limits and fuel gauge
- [x] Global time for deterministic orbit positions across state changes
- [x] Moon landing support (moons are fully explorable surfaces)
- [x] Unified nearest-center interaction detection
- [x] Orbital landing site selection (planet overview map with terrain, settlements, cursor)
- [x] Procedural pixel art textures (ships, planets, stars, stations, avatar, asteroids)
- [x] HUD backgrounds for readability
- [x] Terrain tile variation with detail sprites

## TODO / Next Steps
- [ ] Player vehicle for planet exploration
- [ ] Ship customization system (parts, weapons, shields)
- [ ] Ship/vehicle/avatar upgrade shop in stations
- [ ] Mission system
- [ ] Combat system
- [ ] Fuel consumption during local flight
- [ ] Sound effects and music (SDL_Mixer)
- [ ] Save/load game
- [ ] Settlement interior (basic 2D map in stations/settlements)
- [ ] FTL travel animation
- [ ] Asteroid mining/collection
- [ ] Multiple ship types
