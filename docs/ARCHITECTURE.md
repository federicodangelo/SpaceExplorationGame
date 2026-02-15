# Space Exploration Game - Architecture Documentation

## Overview
A 2D procedural space exploration game built with C# (.NET 10), SDL3 (via SDL3-CS), and Arch ECS.

## Tech Stack
- **Runtime**: .NET 10
- **Rendering**: SDL3 via [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) NuGet package
- **ECS**: [Arch ECS](https://github.com/genaray/Arch) v2.1.0 with [Arch.System](https://github.com/genaray/Arch.Extended) v1.1.0 and Arch.System.SourceGenerator v2.1.0
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
│   ├── PlayerData.cs              # Persistent player data across state changes
│   ├── ICustomizablePart.cs       # Common interface for all equipment parts
│   ├── ShipParts.cs               # Ship types, equipment data model, stats, and part catalog
│   ├── AvatarParts.cs             # Avatar customization data model, stats, and part catalog
│   ├── VehicleParts.cs            # Vehicle customization data model, stats, and part catalog
│   └── MiningResources.cs         # Resource types, cargo model, mineable asteroid data
├── ECS/
│   ├── Components/
│   │   └── Components.cs          # All ECS component structs (incl. combat: Health, Projectile, EnemyAI, Faction, LootDrop)
│   └── Systems/
│       ├── OrbitSystem.cs          # Deterministic orbital position updates
│       ├── VelocitySystem.cs       # Velocity → position integration with speed clamping
│       ├── PlayerMovementSystem.cs  # WASD/arrow movement with pluggable collision
│       ├── CameraFollowSystem.cs   # Smooth camera follow + mouse wheel zoom
│       ├── LabelRenderSystem.cs    # Centered text labels below entities
│       ├── InteractionProximitySystem.cs  # Nearest interactable entity detection
│       ├── VehicleMovementSystem.cs # Thrust/rotation physics for planet rover
│       ├── TileMapRenderer.cs      # Shared tilemap rendering utility
│       ├── ProjectileSystem.cs     # Projectile movement, collision detection, damage application
│       ├── ShieldRegenSystem.cs    # Shield regeneration after damage delay
│       └── EnemyAISystem.cs        # AI state machine for NPC ships (pirate/trader/patrol)
├── Generation/
│   ├── SeededRandom.cs            # Deterministic xorshift64 PRNG
│   ├── SeedManager.cs             # Hierarchical seed derivation
│   ├── GalaxyGenerator.cs         # Galaxy star system placement & properties
│   ├── SolarSystemGenerator.cs    # Planets, moons, asteroids, stations
│   ├── PlanetSurfaceGenerator.cs  # Terrain tilemap generation
│   └── InteriorGenerator.cs       # Station/settlement interior layouts
├── Rendering/
│   ├── SpriteRenderer.cs          # SDL3 rendering abstraction (primitives + textures)
│   ├── TextureManager.cs          # Low-level texture creation utilities (CreateTextureFromPixels, SetPixelBlock)
│   ├── MiniBitmapFont.cs          # Built-in 5x8 pixel font
│   ├── AvatarRenderer.cs          # Player avatar texture & rendering (IDisposable, owns texture)
│   ├── VehicleRenderer.cs         # Player vehicle texture & rendering (IDisposable, owns texture)
│   ├── SpaceshipRenderer.cs       # Player ship textures & rendering (IDisposable, owns textures per type)
│   ├── EnemyShipRenderer.cs       # NPC faction ship textures & rendering (IDisposable, pirate/trader/patrol)
│   ├── StationRenderer.cs         # Space station texture & rendering (IDisposable, owns texture)
│   ├── AsteroidRenderer.cs        # Asteroid texture & rendering (IDisposable, owns texture)
│   ├── PlanetRenderer.cs          # Planet/moon texture factory & rendering (IDisposable, tracks textures)
│   ├── StarRenderer.cs            # Star texture factory & rendering (IDisposable, tracks textures)
│   ├── SolarSystemRenderer.cs     # Solar system static helpers (background stars, orbits, HUD, panels)
│   ├── PlanetSurfaceRenderer.cs   # Planet surface static helpers (terrain, settlements, HUD)
│   ├── ProjectileRenderer.cs      # Projectile trail rendering, damage popups, explosion effects (static)
│   ├── InteriorRenderer.cs        # Interior static helpers (tiles, NPCs, labels, minimap)
│   └── SettlementRenderer.cs      # Settlement rendering helper
├── States/
│   ├── MainMenuState.cs           # Starting point selection menu
│   ├── SolarSystemState.cs        # Space flight within a solar system + overlays
│   ├── PlanetLandingState.cs      # Orbital landing site selection
│   ├── PlanetSurfaceState.cs      # Planet surface exploration (tilemap)
│   └── InteriorState.cs           # Walkable station/settlement interiors
└── UI/
    ├── MenuWidget.cs              # Reusable scrollable menu widget
    └── Overlays/
        ├── GalaxyMapOverlay.cs        # Galaxy map overlay (rendered atop SolarSystem)
        ├── SpaceStationOverlay.cs     # Station docking overlay (rendered atop SolarSystem)
        ├── RepairOverlay.cs           # Ship repair overlay
        ├── MissionOverlay.cs          # Mission board overlay
        ├── CustomizationOverlayBase.cs # Abstract base for all customization overlays
        ├── ShipCustomizationOverlay.cs # Ship equipment management UI (dynamic slots per ship type)
        ├── ShipDealerOverlay.cs       # Ship hull purchase/trade-in UI
        ├── AvatarCustomizationOverlay.cs # Avatar customization management UI
        └── VehicleCustomizationOverlay.cs # Vehicle customization management UI
```

## Command Line Options

```
dotnet run -- [seed] [--start <location>]
```

| Argument | Description |
|---|---|
| `seed` | Optional integer seed for deterministic world generation. If omitted, a random seed is used. |
| `--start <location>` | Skip the main menu and jump directly to a game state. Useful for testing. |

**Start locations** (name or number):

| Name | # | Description |
|---|---|---|
| `galaxy` | `0` | Galaxy Map — bird's-eye view of all star systems |
| `system` | `1` | Star System — ship flight in a random solar system |
| `planet` | `2` | Planet Surface — surface exploration on a random planet |
| `station` | `3` | Space Station — menu interaction at a random station |
| `settlement` | `4` | Settlement — planet surface spawned at a settlement |

**Examples:**
```
dotnet run                              # Random seed, main menu
dotnet run -- 12345                     # Seed 12345, main menu
dotnet run -- --start system            # Random seed, jump to star system
dotnet run -- 42 --start settlement     # Seed 42, jump to settlement
dotnet run -- --start 3                 # Random seed, jump to station (by number)
```

## Architecture Decisions

### Game States
The game uses a state machine pattern. Each state (`GameState` subclass) owns its logic and rendering. When switching states, all ECS entities are destroyed and recreated — this keeps things simple and avoids stale entity issues.

States:
- **MainMenuState**: Starting point selection. Animated starfield background, 5 options: Galaxy Map, Star System, Planet Surface, Space Station, Settlement. Mouse hover/click and keyboard navigation. Picks random systems/planets/stations for non-galaxy-map starts.
- **SolarSystemState**: Real-time flight with combat. Player controls ship with WASD. Orbiting planets/moons/stations rendered with sphere-shaded textures. Press E near planets/stations to interact. Press M to open the **GalaxyMapOverlay**. Press Space to fire weapons (when not mining). NPC ships (pirates, traders, patrols) spawn based on per-system danger level. Pirates attack the player and traders; patrols hunt pirates and defend traders. Destroyed enemies drop credits, resources, and equipment parts. Player death respawns at the nearest station with hull/cargo/credit penalties. When docking at a station, a **SpaceStationOverlay** opens on top of the solar system view (orbits keep animating in the background). Uses OrbitSystem, VelocitySystem, CameraFollowSystem, LabelRenderSystem, InteractionProximitySystem, ProjectileSystem, ShieldRegenSystem, and EnemyAISystem.
- **GalaxyMapOverlay** (overlay, not a game state): Full-screen overlay drawn over SolarSystemState. Bird's-eye view of the galaxy. Click to select star systems, double-click or Enter to travel. Mouse drag to pan. Nebula clouds and glow-textured stars. Shows FTL range and fuel range circles. Traveling to a different system spends fuel and transitions to a new SolarSystemState. Selecting the current system closes the overlay. Opened with M key, closed with M or Escape.
- **SpaceStationOverlay** (overlay, not a game state): Semi-transparent menu drawn over SolarSystemState when docked. Refuels ship on docking. Menu options: Repair, Missions, Ship Customization, Ship Dealer, Avatar Customization, Vehicle Customization, Walk Station, Exit. Displays current ship type name in status area. Walk Station transitions to InteriorState; Exit closes the overlay and returns to free flight.
- **PlanetLandingState**: Orbital view for landing site selection. Shows full terrain map as a texture (1px = 1 tile) with settlement markers. The player clicks to choose a landing site; reticle with terrain info panel shows selected terrain type and position. Supports zoom, pan, WASD cursor nudge. Cannot land on water/lava. Confirms with Enter/E, cancels with Escape.
- **PlanetSurfaceState**: Tilemap exploration with per-tile brightness variation and terrain detail sprites (trees, rocks, water shimmer). Player avatar walks on generated terrain. Lands at the site chosen in PlanetLandingState (or map center by default). Press V to mount/dismount a rover vehicle for faster travel. Must dismount before entering settlements or boarding the ship. Press E near ship to board, E near settlement to enter interior. Avatar walk speed and vehicle physics are dynamically computed from equipped avatar/vehicle parts. Uses PlayerMovementSystem (with terrain collision), CameraFollowSystem, and TileMapRenderer.
- **InteriorState**: Walkable tile-based interior for both space stations and settlements. Procedurally generated rooms connected by corridors (stations) or streets (settlements). Features NPCs with dialogue, repair stations, mission boards (placeholder), and customization terminals (ship, avatar, vehicle). Station docking bays have four terminals: exit door, ship customization, avatar customization, and vehicle customization. Avatar walk speed is dynamically computed from equipped avatar parts. Minimap shows room layout, NPCs, and interactable objects with color-coded dots. Uses PlayerMovementSystem (with walkability collision), CameraFollowSystem, and TileMapRenderer.

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
Station interiors derive seeds from system seed + 2000 + station index. Settlement interiors derive from surface seed + 3000 + position hash.

### ECS Usage (Arch)
Components are plain structs defined in `Components.cs`. The game uses Arch's `World.Query()` with lambda syntax for ad-hoc iteration, plus dedicated **systems** for recurring logic. Key component types:
- `Transform` — position + rotation
- `Velocity` — movement
- `Sprite` — rendering info (currently colored rects)
- `CelestialBody` — star/planet/moon/station properties
- `Orbit` — orbital mechanics (parent entity, radius, speed, angle)
- `PlayerControlled` — tag for player entity
- `Label` — text displayed near entity
- `Interactable` — landing/docking capability
- `Health` — hull + shield HP, shield regen rate/delay, damage tracking
- `Projectile` — damage, speed, lifetime, collision radius, owner faction, RGB color
- `EnemyAI` — faction, AI state machine, fire rate, weapon stats, detection ranges
- `LootDrop` — credit ranges, resource/part drop chances, danger level scaling
- `Faction` — enum: Player, Pirate, Trader, Patrol
- `AIState` — enum: Idle, Patrol, Chase, Attack, Flee, Defend

### ECS Systems
Systems live in `ECS/Systems/` and encapsulate reusable game logic, reducing duplication across states.

Most systems extend `BaseSystem<World, float>` and use Arch's source generator via `[Query]` and `[All(typeof(T))]` attributes on partial methods. The source generator auto-implements `Update()` to iterate matching entities.

> **Important**: The source generator overrides `Update()` without calling `BeforeUpdate()` / `AfterUpdate()`. Do not rely on those lifecycle hooks for per-frame state reset. Systems that need lifecycle control should be plain classes with manual `World.Query()` calls instead.

| System | Base Class | Queries | Used By |
|---|---|---|---|
| **OrbitSystem** | `BaseSystem` (source gen) | `Transform + Orbit` | SolarSystemState |
| **VelocitySystem** | `BaseSystem` (source gen) | `Transform + Velocity` | SolarSystemState |
| **PlayerMovementSystem** | `BaseSystem` (source gen) | `PlayerControlled + Transform` | PlanetSurfaceState, InteriorState |
| **CameraFollowSystem** | `BaseSystem` (source gen) | `PlayerControlled + Transform` | SolarSystemState, PlanetSurfaceState, InteriorState |
| **LabelRenderSystem** | `BaseSystem` (source gen) | `Transform + Label` | SolarSystemState |
| **InteractionProximitySystem** | Plain class (manual query) | `Transform + CelestialBody + Interactable` | SolarSystemState |
| **VehicleMovementSystem** | Plain class (manual) | Single entity | PlanetSurfaceState |
| **TileMapRenderer** | Static utility | N/A (callback-driven) | PlanetSurfaceState, InteriorState |
| **ProjectileSystem** | Plain class (manual query) | `Transform + Velocity + Projectile`, `Transform + Health` | SolarSystemState |
| **ShieldRegenSystem** | `BaseSystem` (source gen) | `Health` | SolarSystemState |
| **EnemyAISystem** | Plain class (manual query) | `Transform + Velocity + EnemyAI + Health` | SolarSystemState |

- **OrbitSystem**: Computes deterministic orbital positions from global time. Accepts `Func<float>` for time and `Func<Vector2>` for fallback center.
- **VelocitySystem**: Integrates velocity into position each frame with `MaxSpeed` clamping.
- **PlayerMovementSystem**: Handles WASD/arrow input with configurable speed. Exposes a `Func<Vector2, bool>? CanMoveTo` delegate for collision checking (terrain, walls).
- **CameraFollowSystem**: Lerps camera toward the player entity and handles mouse-wheel zoom.
- **LabelRenderSystem**: Draws centered text labels below entities using the bitmap font.
- **InteractionProximitySystem**: Finds the nearest interactable entity to a given position. Implemented as a plain class with `FindNearest(Vector2)` because the source generator's `Update()` does not call `BeforeUpdate()`, which prevented per-frame distance reset.
- **VehicleMovementSystem**: Handles vehicle physics — thrust along facing direction, A/D rotation, braking, friction. Plain class with `Update(float dt)`, configurable physics params, and `CanMoveTo` collision delegate.
- **TileMapRenderer**: Static helper that renders visible tilemap tiles with hash-based brightness variation and an optional per-tile detail callback.
- **ProjectileSystem**: Plain class that moves projectiles, checks lifetime expiry, and detects collisions between projectiles and Health entities. Faction logic prevents friendly fire (same-faction projectiles don't hit). Exposes `DestroyedThisFrame` and `DamageEventsThisFrame` lists for the state to process loot drops, explosions, and damage popups.
- **ShieldRegenSystem**: Source-generated system that regenerates shields after a configurable delay (`ShieldRegenDelay`) since last hit. Regen rate is per-second (`ShieldRegenRate`).
- **EnemyAISystem**: Plain class implementing a state machine for NPC ships. Pirates patrol, chase, and attack the player (flee when low health). Traders cruise and flee from nearby pirates. Patrols hunt pirates and defend traders. Fires projectiles via a deferred spawn list to avoid mutation during iteration. Takes `Func<Vector2>` for player position and `Func<bool>` for player alive state.

### Rendering
The `SpriteRenderer` class provides both SDL3 draw primitives (filled rects, scanline circles, lines) and texture-based rendering with rotation and alpha support.

**TextureManager** is a lightweight utility class that wraps the SDL renderer handle and provides two public methods used by all entity renderers:
- `CreateTextureFromPixels(byte[] pixels, int width, int height)` — creates an SDL texture from raw RGBA pixel data
- `SetPixelBlock(...)` — static helper to fill rectangular pixel regions

**Entity Renderers** follow a consistent pattern: each is an `IDisposable` class that receives a `TextureManager` in its constructor, generates its own textures procedurally, owns them for their lifetime, and provides `Render()`/rendering methods. They are all owned by `Game` and disposed on shutdown.

| Renderer | Texture Ownership | Key Methods |
|---|---|---|
| **AvatarRenderer** | Singleton texture (16×16 humanoid) | `Render(renderer, camera, position)` |
| **VehicleRenderer** | Singleton texture (20×20 rover) | `Render(renderer, camera, position, rotation, isMounted)` |
| **SpaceshipRenderer** | Per-type solar + landed textures, flame texture | `RenderFlying(...)`, `RenderLanded(...)` |
| **EnemyShipRenderer** | 3 faction textures (pirate/trader/patrol) + flame | `Render(renderer, camera, position, rotation, faction, isThrusting)`, `RenderHealthBar(...)` |
| **StationRenderer** | Singleton texture (32×32 station) | `RenderStations(renderer, camera, ecsWorld, entities, globalTime)` |
| **AsteroidRenderer** | Singleton texture (12×12 rock) | `RenderAsteroids(renderer, camera, asteroids, center, globalTime)` |
| **PlanetRenderer** | Factory — tracks all created textures | `CreateTexture(size, r, g, b, seed)`, `RenderPlanetsAndMoons(...)`, `DestroyTexture(tex)`, `DestroyAll()` |
| **StarRenderer** | Factory — tracks all created textures | `CreateTexture(size, r, g, b)`, `Render(...)`, `DestroyTexture(tex)`, `DestroyAll()` |

**Scene Renderers** are static helper classes that handle non-entity rendering (HUD, panels, background elements):
- **SolarSystemRenderer** — background stars (parallax), orbit lines, HUD, interaction panels (planet/moon/station)
- **ProjectileRenderer** — projectile trail rendering (colored elongated lines), floating damage numbers (blue=shield, yellow=hull), expanding explosion circles with particle sparks
- **PlanetSurfaceRenderer** — terrain details, settlement markers, surface HUD
- **InteriorRenderer** — tiles, room labels, NPCs, interactable markers, minimap
- **SettlementRenderer** — settlement-specific rendering

**Procedural texture descriptions:**
- **Ship (solar)**: 4 variants (Scout/Fighter/Freighter/Explorer) — triangular/angular pixel-art with cockpit highlights and engine pods
- **Ship (landed)**: Oval hull with cockpit and landing struts, color-matched per type
- **Engine flame**: Orange-yellow gradient cone rendered behind the ship when thrusting
- **Planets/moons**: Sphere-shaded textures with diffuse + specular lighting, surface noise, and edge darkening — unique per body
- **Stars**: Radial gradient glow from white core to star color
- **Stations**: Cross-shaped design with central hub, outer ring, solar panels, and docking indicators; slowly rotates
- **Asteroids**: Irregular rocky blobs with angular distortion
- **Avatar**: Tiny humanoid in green suit with blue visor
- **Vehicle**: Top-down 4-wheel rover with roll cage, cockpit windshield, headlights, and tail lights
- **Pirate ship**: 28px red/dark angular hull with spiky aggressive silhouette
- **Trader ship**: 32px gold/warm-toned bulky freighter silhouette
- **Patrol ship**: 30px blue/cyan sleek military hull

### Ship Types
The game has multiple ship types defined in `ShipTypeCatalog`. Each `ShipType` record specifies the hull's available equipment slots, sprite size, weight multiplier, base hull/fuel values, and buy/sell pricing.

| Ship | Slots | Size | Weight | Base Hull | Base Fuel | Base Cargo | Cost | Sell |
|---|---|---|---|---|---|---|---|---|
| Scout (starter) | 4 (Engine, Shield, FTL, Utility) | 32px | 1.0x | 80 | 80 | 40 | Free | 200 |
| Fighter | 5 (Engine, Armor, Shield, Weapon×2) | 32px | 1.1x | 120 | 60 | 30 | 1500 | 750 |
| Freighter | 6 (Engine, Armor, FTL, Utility×2, Weapon) | 48px | 1.4x | 200 | 160 | 120 | 3000 | 1500 |
| Explorer | 7 (All slots) | 40px | 1.2x | 150 | 140 | 80 | 5000 | 2500 |

**Weight system**: `PlayerData.GetCombinedStats()` divides acceleration and maxSpeed by the ship type's weight factor. Heavier ships are slower.

**Base hull/fuel/cargo**: Ship type provides base hull, fuel, and cargo values. Part bonuses add on top: `MaxHull = BaseHull + PartBonuses`, `MaxFuel = BaseFuel + PartBonuses`, `MaxCargo = BaseCargo + PartBonuses`.

**Dynamic sprite size**: `SolarSystemState` and `PlanetSurfaceState` read `CurrentShipType.SpriteSize` for rendering. Flame offset and size scale proportionally.

**Switching ships**: `PlayerData.SwitchShipType()` moves incompatible parts to inventory, fills empty slots with tier-1 defaults, and preserves health/fuel as proportional percentages.

### Ship Dealer
The **ShipDealerOverlay** provides a UI for buying and trading in ship hulls. Available at station/settlement terminals and the station menu.

- Left column: all ship types with quick stats and net pricing
- Right column: detailed stats for selected ship with comparison to current ship (green = better, red = worse)
- Slot breakdown shows gained (+) and lost (-) slots
- Trade-in pricing: net cost = buy price − current ship's sell value
- Buying triggers `SwitchShipType()` which handles part migration automatically

### Ship Customization
Players can equip and swap ship parts at space stations via the **ShipCustomizationOverlay**. The system is defined in `ShipParts.cs` and integrated into `PlayerData`.

**Equipment Slots**: Variable per ship type (4–7). Defined by `ShipType.AvailableSlots`. The `ShipCustomizationOverlay` dynamically adjusts its slot list and panel height when opened based on `CurrentShipType`.

**Slot Types** (8 possible): Engine, Armor, Shield, FTL Drive, Weapon 1, Weapon 2, Utility, Utility 2.

**Part Tiers**: Each slot type has 3 tiers of parts (Tier 1 = starter, Tier 3 = best). Parts are defined in `ShipPartCatalog` with buy cost, sell/trade-in value, name, description, and stat bonuses.

**Stats affected by parts** (`ShipPartStats`):
| Stat | Affected By | Gameplay Effect |
|---|---|---|
| Acceleration | Engine | Ship thrust in SolarSystemState |
| MaxSpeed | Engine | Ship speed cap in SolarSystemState |
| RotationSpeed | Engine | Ship turning rate in SolarSystemState |
| MaxHull | Armor | Hull capacity (health pool in combat) |
| MaxFuel | Utility | Fuel tank size, extends range |
| FtlRange | FTL Drive | Maximum FTL jump distance in GalaxyMapOverlay |
| ShieldStrength | Shield | Shield HP pool — absorbs damage before hull, regenerates after delay |
| WeaponDamage | Weapon 1/2 | Projectile damage (also used as mining DPS) |
| FuelEfficiency | Utility | Reduces fuel consumption per jump |
| CargoCapacity | Utility | Bonus cargo capacity for mined resources |

**Ownership model**: Once a part is purchased, the player owns it permanently. Owned parts are stored in `PlayerData.OwnedParts` (inventory). Swapping between owned parts is free — the old part returns to inventory. Players can sell owned (unequipped) parts manually for their sell value.

**How combined stats work**: `PlayerData.GetCombinedStats()` sums the stats of all equipped parts, then divides acceleration/maxSpeed by the ship type's weight factor. SolarSystemState reads acceleration/maxSpeed/rotationSpeed each frame. GalaxyMapOverlay reads FTL range for jump distance and range circle. `TrySpendFuel()` applies fuel efficiency.

**UI**: Two-column overlay — left column lists equipped slots, right column shows available parts for the selected slot. Parts show status tags: [EQUIPPED], [OWNED] (free to equip), or a credit cost (must buy). Stat comparison shown for selected parts (green = better, red = worse). Press Enter to equip/buy, X to sell owned parts.

### Avatar Customization
Players can equip and swap avatar gear at **Avatar Customization** terminals in station interiors via the **AvatarCustomizationOverlay**. The system is defined in `AvatarParts.cs` and integrated into `PlayerData`.

**Equipment Slots** (3 total): Suit, Helmet, Boots.

**Part Tiers**: Each slot has 3 tiers (Tier 1 = starter, Tier 3 = best). Parts are defined in `AvatarPartCatalog`.

**Stats affected by parts** (`AvatarPartStats`):
| Stat | Affected By | Gameplay Effect |
|---|---|---|
| WalkSpeed | Suit | Bonus to base avatar movement speed (200 + WalkSpeed) |
| OxygenCapacity | Helmet | Oxygen tank capacity (future hazardous environments) |
| TerrainPenalty | Boots | Terrain movement penalty reduction (future terrain effects) |

**Ownership model**: Same as ship parts — buy once, own permanently, swap free, sell manually. Stored in `PlayerData.OwnedAvatarParts`. Combined stats via `PlayerData.GetCombinedAvatarStats()`.

**Dynamic stat application**: Both `PlanetSurfaceState` and `InteriorState` compute avatar speed as `BaseAvatarSpeed (200) + CombinedAvatarStats.WalkSpeed` when entering the state.

### Vehicle Customization
Players can equip and swap vehicle parts at **Vehicle Customization** terminals in station interiors via the **VehicleCustomizationOverlay**. The system is defined in `VehicleParts.cs` and integrated into `PlayerData`.

**Equipment Slots** (3 total): Engine, Chassis, Lights.

**Part Tiers**: Each slot has 3 tiers (Tier 1 = starter, Tier 3 = best). Parts are defined in `VehiclePartCatalog`.

**Stats affected by parts** (`VehiclePartStats`):
| Stat | Affected By | Gameplay Effect |
|---|---|---|
| Acceleration | Engine | Vehicle thrust acceleration on planet surface |
| MaxSpeed | Engine | Vehicle top speed on planet surface |
| RotationSpeed | Chassis | Vehicle turning rate |
| Friction | Chassis | Added to base friction (affects handling/grip) |
| Visibility | Lights | Light range on planet surface (future visibility system) |

**Ownership model**: Same as ship/avatar parts. Stored in `PlayerData.OwnedVehicleParts`. Combined stats via `PlayerData.GetCombinedVehicleStats()`.

**Dynamic stat application**: When mounting the vehicle in `PlanetSurfaceState`, the `VehicleMovementSystem` is created with stats from `GetCombinedVehicleStats()` (acceleration, maxSpeed, rotationSpeed, friction). Falls back to `GameConfig` defaults if a stat is zero.

### Asteroid Mining
Players can mine asteroids in the solar system view by holding **Space** near an asteroid belt. The mining laser beam originates from the ship and targets the nearest asteroid within range (120 world pixels). Mining DPS equals the ship's combined `WeaponDamage` stat — weapons are dual-use for both combat and mining.

**Resource Types** (defined in `MiningResources.cs`):
| Resource | Value/Unit | Rarity | Color |
|---|---|---|---|
| Iron | 5 | Common (30%) | Brown |
| Nickel | 8 | Common (25%) | Gray |
| Ice | 3 | Common (15%) | Light Blue |
| Gold | 20 | Uncommon (15%) | Gold |
| Platinum | 35 | Rare (10%) | Silver-White |
| Crystal | 50 | Rare (5%) | Cyan |

**Asteroid Properties**: Each asteroid has HP (proportional to visual size), a resource type, and a resource amount. When HP reaches zero the asteroid is destroyed and resources are added to the player's cargo hold.

**Cargo System**: The player has a cargo hold with limited capacity. Capacity = `ShipType.BaseCargo` + part bonuses (`CargoCapacity` stat from Utility parts like Cargo Pod/Bay). Cargo is stored in `PlayerData.Cargo` as a dictionary of `ResourceType → int`. The HUD shows current/max cargo at all times in the solar system.

**Mining Beam Visual**: A flickering red laser beam (3 parallel lines) rendered from ship to asteroid with a glow effect at the impact point. Asteroids visually shrink as they take damage.

**Selling Cargo**: Resources can be sold for credits at:
- **Space Station overlay** — "SELL CARGO" menu option (via `SellCargoOverlay`)
- **Interior Cargo Terminals** — `InteractableType.CargoTerminal` placed in station trading rooms and settlement markets

**Sell Cargo UI**: Lists all held resources with amounts and credit values. Navigate with Up/Down, sell individual resources with Enter, or sell all at once.

### Customization UI Pattern
All three customization overlays (Ship, Avatar, Vehicle) inherit from `CustomizationOverlayBase`, which provides the full two-column UI layout, input handling, equip/buy/sell logic, and rendering. Each part record (`ShipPart`, `AvatarPart`, `VehiclePart`) implements the `ICustomizablePart` interface. Subclasses only supply:
- Title, title color, panel height, slot definitions
- Data access (equipped parts dictionary, inventory list, catalog lookup)
- Equip/sell operations on the correct PlayerData collections
- Stat comparison rendering (type-specific stat diffs)

Shared UI features:
- Two-column layout: equipped slots (left) → available parts for selected slot (right)
- Status tags: **[EQUIPPED]** (cyan), **[OWNED]** (green, free to equip), **price** (yellow, must buy)
- Stat comparison panel with color-coded deltas (green = improvement, red = worse)
- Controls: Enter = equip/buy, X = sell owned unequipped part, Escape = close, Arrow keys = navigate
- Each overlay is opened by pressing E near the corresponding terminal in an interior, or from the station menu

### Customization Terminals in Interiors
Both station docking bays and settlement landing pads contain customization terminals:

**Station docking bays** — four terminals near the landing pad:
| Terminal | Color (world) | Color (minimap) | InteractableType |
|---|---|---|---|
| Exit Door | Green | Green | ExitDoor |
| Ship Customization | Cyan (100,220,255) | Cyan | ShipCustomization |
| Ship Dealer | Gold (255,200,80) | Gold | ShipDealer |
| Avatar Customization | Cyan (0,200,200) | Cyan | AvatarCustomization |
| Vehicle Customization | Orange (200,120,0) | Orange | VehicleCustomization |

**Settlement landing pads** — same terminal types plus Ship Dealer: Ship Customization and Ship Dealer near the exit, Avatar and Vehicle Customization at the top of the pad.

All customization types and the ship dealer are also accessible from the **SpaceStationOverlay** menu (without walking to a terminal).

A built-in `MiniBitmapFont` renders text without requiring TTF files. All HUD panels use semi-transparent dark backgrounds for readability.

### Combat System
Real-time projectile combat in the solar system. Players fire weapons with Space (when not mining), and NPC ships behave according to their faction AI.

**Factions**:
| Faction | Behavior | Color |
|---|---|---|
| **Player** | Controlled by input, fires green projectiles | Green |
| **Pirate** | Patrol → detect player/trader → chase → attack → flee when low HP | Red |
| **Trader** | Cruise through system, flee from nearby pirates | Gold |
| **Patrol** | Hunt pirates, defend traders, strong shields | Blue |

**Danger Level**: Each star system has a seeded danger level (1–5) stored in `StarSystemData.DangerLevel`. Displayed on the galaxy map with color-coded stars (green 1–2, yellow 3, red 4–5). Higher danger = more pirates, stronger enemies, better loot.

**NPC Spawning**: Pirates, traders, and patrols are spawned when entering a solar system. Counts scale with danger level. Pirates get hull and damage bonuses per danger level. Patrols have strong shields. Traders are unarmed and flee from threats.

**Shield Mechanics**: Shields absorb damage before hull HP. Shields regenerate after a configurable delay since last hit (`ShieldRegenDelay = 3s`). Regen rate is constant (`ShieldRegenRate = 5 HP/s`). Shield HP pool comes from equipped Shield parts (`ShieldStrength` stat).

**Loot Drops**: Destroyed enemies drop credits (scaled by danger level), with chances for resource drops and equipment part drops. Part tier scales with danger level. Loot is displayed as a combat message.

**Player Death**: When hull reaches zero:
- 3-second respawn timer with death screen overlay
- Lose 10% of credits and 25% of cargo
- Respawn at nearest station with 50% hull and full shields
- NPC AI continues running during death (pirates keep fighting traders/patrols)

**Combat HUD**: Hull bar (red→green gradient) and shield bar (blue) displayed below the cargo HUD. Danger level shown as colored text. Floating damage numbers appear at hit locations (blue = shield, yellow = hull). Expanding explosion circles on entity destruction.

**Friendly Fire Rules**: Same-faction projectiles never hit each other. Patrol/trader projectiles don't hit the player. Only pirate projectiles can hit the player, traders, and patrols.

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
- Space (hold): Fire weapons / mine nearest asteroid (mining takes priority when near an asteroid)
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
- WASD/Arrows: Move avatar / drive vehicle
- Mouse Scroll: Zoom
- V: Mount/dismount vehicle (when near)
- E: Board ship (when near, on foot) / Enter settlement (when near, on foot)
- Escape: Leave planet (quick exit)

### Interior (Station / Settlement)
- WASD/Arrows: Move avatar
- Mouse Scroll: Zoom
- E: Interact with NPCs / terminals
- Enter: Advance dialogue / Confirm purchase
- Escape: Close overlay / Exit interior

## Build & Run
```bash
dotnet build
dotnet run
dotnet run -- 12345  # with specific galaxy seed
```

## Current Status (v0.3 - Combat)
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
- [x] Walkable interiors for stations and settlements (rooms, NPCs, trade, repair, missions)
- [x] Player vehicle for planet surface exploration (mount/dismount, 3x speed)
- [x] Ship customization system (dynamic equipment slots per ship type, 3 tiers, dynamic ship stats)
- [x] Multiple ship types (Scout, Fighter, Freighter, Explorer) with ship dealer UI
- [x] Avatar customization system (3 equipment slots, 3 tiers, dynamic walk speed)
- [x] Vehicle customization system (3 equipment slots, 3 tiers, dynamic vehicle physics)
- [x] Customization terminals in station interiors (ship, avatar, vehicle)
- [x] Asteroid mining (mining laser beam, named resources, cargo system, sell at stations/terminals)
- [x] Space combat system (projectile weapons, shield/hull mechanics, enemy AI)
- [x] NPC factions (pirates, traders, patrols) with faction-specific AI behaviors
- [x] Per-system danger levels with galaxy map display
- [x] Loot drops (credits, resources, equipment parts scaled by danger)
- [x] Player death and respawn with penalties

- [x] Entity renderer architecture (Avatar, Vehicle, Spaceship, EnemyShip, Station, Asteroid, Planet, Star renderers own their textures)
- [x] Scene renderer extraction (SolarSystemRenderer, ProjectileRenderer, PlanetSurfaceRenderer, InteriorRenderer, SettlementRenderer)

## TODO / Next Steps
- [ ] Planet surface combat (hostile fauna + hostile NPCs)
- [ ] Mission system (acceptance, tracking, completion)
- [ ] Fuel consumption during local flight
- [ ] Sound effects and music (SDL_Mixer)
- [ ] Save/load game
- [ ] FTL travel animation
