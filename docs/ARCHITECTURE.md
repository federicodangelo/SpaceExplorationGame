# Space Exploration Game - Architecture Documentation

## Overview
A 2D procedural space exploration game built with C# (.NET 10), SDL3 (via SDL3-CS), and Arch ECS.

## Tech Stack
- **Runtime**: .NET 10
- **Rendering**: SDL3 via [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS) NuGet package
- **ECS**: [Arch ECS](https://github.com/genaray/Arch) v2.1.0 with [Arch.System](https://github.com/genaray/Arch.Extended) v1.1.0 and Arch.System.SourceGenerator v2.1.0
- **Graphics**: Procedural pixel art textures generated at runtime (sphere-shaded planets, glow-gradient stars, pixel-art ship/avatar/station sprites) plus SDL3 draw primitives and a minimal bitmap font.
- **Audio**: SDL3 built-in audio API (push-based streaming) — fully procedural synthesis, no external audio files or SDL_mixer

## Project Structure

```
SpaceExplorationGame/
├── SpaceExplorationGame.csproj    # .NET 10 project file
├── Program.cs                     # Entry point
├── Core/
│   ├── Game.cs                    # Main game class (platform, ECS world, game loop)
│   ├── GameConfig.cs              # All tunable constants
│   ├── GameState.cs               # Abstract base class for game states
│   ├── Camera.cs                  # 2D camera with zoom and viewport
│   ├── CommonTypes.cs             # Shared lightweight value types (colors, geometry, spawn data, etc.)
│   ├── GalaxyLocation.cs          # Galaxy location value type (system/planet/settlement) for missions
│   ├── PlayerData.cs              # Persistent player data across state changes (includes mission tracking)
│   ├── CombatHelper.cs            # Shared combat utilities (loot drops, damage popups, effects)
│   ├── FactionRules.cs            # Centralized faction interaction/friendly-fire rules
│   ├── ICustomizablePart.cs       # Common interface for all equipment parts
│   ├── IDebugInfoProvider.cs      # DebugTimer / DebugTimingEntry — lightweight timing infrastructure
│   ├── MenuOptionsPersistence.cs  # Persists main-menu selections to disk (JSON)
│   ├── NpcShipLoadoutHelper.cs    # NPC ship type/loadout/weapon selection and stat derivation by danger tier
│   ├── Missions.cs                # Mission data model (MissionType, MissionStatus, Mission class)
│   ├── MissionTracker.cs          # Active/completed mission state + Notify* callbacks
│   ├── NavigationTarget.cs        # Player navigation target (Set* / Clear helpers)
│   ├── ShipParts.cs               # Ship types, equipment data model, stats, and part catalog
│   ├── ShipStatsHelper.cs         # Shared ship stat aggregation helper (type + parts -> final stats)
│   ├── AvatarParts.cs             # Avatar customization data model, stats, and part catalog
│   ├── VehicleParts.cs            # Vehicle customization data model, stats, and part catalog
│   └── MiningResources.cs         # Resource types, cargo model, mineable asteroid data
├── Platform/
│   ├── IPlatform.cs               # Top-level platform interface (SpriteRenderer, Textures, InputManager, AudioManager)
│   ├── ISpriteRenderer.cs         # Rendering abstraction (primitives, textures, text — world & screen space)
│   ├── ITextureManager.cs         # Texture creation utilities (CreateFromPixels, SetPixelBlock)
│   ├── IFontRenderer.cs           # Font/text rendering abstraction
│   ├── ITileMapRenderer.cs        # Tilemap rendering abstraction (hash brightness, detail callback)
│   ├── IInputManager.cs           # Input abstraction (actions, axes, mouse, text input)
│   ├── IAudioManager.cs           # Audio abstraction (music themes, SFX playback, volume)
│   ├── InputTypes.cs              # InputAction, InputActionAxis, MouseButton, InputMethod, MovementInputMode enums
│   └── Sdl/
│       ├── SdlPlatform.cs         # SDL3 IPlatform implementation (window, renderer lifecycle)
│       ├── SdlSpriteRenderer.cs   # SDL3 ISpriteRenderer implementation
│       ├── SdlTextureManager.cs   # SDL3 ITextureManager implementation
│       ├── SdlFontRenderer.cs     # SDL3 IFontRenderer implementation (wraps MiniBitmapFont)
│       ├── SdlTileMapRenderer.cs  # SDL3 ITileMapRenderer implementation
│       ├── SdlInputManager.cs     # SDL3 IInputManager implementation (keyboard, mouse, gamepad)
│       └── SdlAudioManager.cs     # SDL3 IAudioManager implementation (push-streaming audio)
├── ECS/
│   ├── EntityFactory.cs           # Centralized entity creation (all component compositions)
│   ├── Components/
│   │   └── Components.cs          # All ECS component structs (incl. combat: Health, Projectile, EnemyAI, SurfaceAI, Faction, LootDrop)
│   └── Systems/
│       ├── OrbitSystem.cs          # Deterministic orbital position updates
│       ├── VelocitySystem.cs       # Acceleration/velocity integration + centralized damping + position/rotation updates
│       ├── CameraFollowSystem.cs   # Smooth camera follow + mouse wheel zoom
│       ├── InteractionProximitySystem.cs  # Nearest interactable entity detection
│       ├── DependentEntityCleanupSystem.cs # Removes entities owned by destroyed parent entities
│       ├── Movement/
│       │   ├── AvatarMovementSystem.cs  # WASD/arrow movement intent (critically damped acceleration target)
│       │   ├── ShipMovementSystem.cs    # Ship input intent (thrust/rotation/brake), applied by VelocitySystem
│       │   └── VehicleMovementSystem.cs # Vehicle input intent (thrust/rotation/brake), applied by VelocitySystem
│       ├── Combat/
│       │   ├── ProjectileSystem.cs      # Projectile movement, collision detection, damage application
│       │   └── ShieldRegenSystem.cs     # Shield regeneration after damage delay
│       ├── Effects/
│       │   └── ParticleSystem.cs        # ECS particle simulation/emission with emitter bounds validation
│       └── AI/
│           ├── ShipEnemyAISystem.cs     # AI state machine for NPC ships (pirate/trader/patrol)
│           └── AvatarEnemyAISystem.cs   # AI state machine for surface enemies (fauna/bandits)
├── Audio/
│   ├── MusicGenerator.cs            # Real-time procedural ambient music (6 layers, 7 themes)
│   └── SfxGenerator.cs              # Pre-generated sound effects (15 types, additive synthesis)
├── Generation/
│   ├── IUniverseGenerator.cs      # Interface: GenerateGalaxy, GenerateSolarSystem, GeneratePlanetSurface, etc.
│   ├── SeededRandom.cs            # Deterministic xorshift64 PRNG
│   ├── SeedManager.cs             # Hierarchical seed derivation
│   ├── SurfaceTerrainRules.cs     # Shared walkability/landing/spawn validation rules for terrain types
│   ├── Procedural/
│   │   ├── ProceduralUniverseGenerator.cs  # Default IUniverseGenerator — delegates to individual generators
│   │   ├── GalaxyGenerator.cs              # Galaxy star system placement & properties
│   │   ├── SolarSystemGenerator.cs         # Planets, moons, asteroids, stations
│   │   ├── PlanetSurfaceGenerator.cs       # Terrain tilemap generation
│   │   ├── InteriorGenerator.cs            # Station/settlement interior layouts + InteractableType enum
│   │   └── MissionGenerator.cs             # Deterministic mission generation per station/settlement board
│   └── Showcase/
│       ├── ShowcaseUniverseGeneratorHelpers.cs      # Shared helpers for showcase generators
│       ├── StarTypeShowcaseUniverseGenerator.cs     # IUniverseGenerator for star-type debug showcase
│       ├── PlanetTypeShowcaseUniverseGenerator.cs   # IUniverseGenerator for planet-type debug showcase
│       ├── AsteroidMiningShowcaseUniverseGenerator.cs  # IUniverseGenerator for asteroid mining showcase
│       └── SurfaceMiningShowcaseUniverseGenerator.cs   # IUniverseGenerator for surface mining showcase
├── Simulation/
│   ├── ISimulation.cs             # Interface + UpdateContext/AddContext value types
│   ├── SimulationPlayer.cs        # Player presence within a simulation (PlayerData + Entity)
│   ├── SimulationCoordinator.cs   # Manages all active simulations (lifecycle, 90s empty timeout, parent chain keep-alive)
│   ├── SolarSystemSimulation.cs   # Solar system simulation (orbits, combat, NPC AI, mining, loot)
│   ├── PlanetSurfaceSimulation.cs # Planet surface simulation (terrain, surface combat, avatar/vehicle, respawn)
│   ├── InteriorSimulation.cs      # Interior simulation (walkable rooms, NPC interaction, no combat)
│   └── Base/
│       ├── SimulationBase.cs          # Abstract base class (ECS world, player management, template methods)
│       └── CombatSimulationBase.cs    # Intermediate base for combat simulations (death/respawn, combat messages, music timer)
├── Rendering/
│   ├── Base/
│   │   ├── MiniBitmapFont.cs      # Built-in 5x8 pixel font data (used by SdlFontRenderer)
│   │   └── RenderColors.cs        # Shared color/style constants used across multiple renderers
│   ├── LabelRenderer.cs           # Centered text labels below entities (queries Transform + Label)
│   ├── AvatarRenderer.cs          # Player avatar texture & rendering (IDisposable, owns texture)
│   ├── VehicleRenderer.cs         # Player vehicle texture & rendering (IDisposable, owns texture)
│   ├── SpaceshipRenderer.cs       # Player ship textures & rendering (IDisposable, owns textures per type)
│   ├── EnemyShipRenderer.cs       # NPC faction ship textures & rendering (IDisposable, pirate/trader/patrol)
│   ├── StationRenderer.cs         # Space station texture & rendering (IDisposable, owns texture)
│   ├── AsteroidRenderer.cs        # Asteroid texture & rendering (IDisposable, owns texture)
│   ├── PlanetRenderer.cs          # Planet/moon texture factory & rendering (IDisposable, tracks textures)
│   ├── StarRenderer.cs            # Star texture factory & rendering (IDisposable, tracks textures)
│   ├── SolarSystemRenderer.cs     # Solar system static helpers (background stars, orbits, panels)
│   ├── PlanetSurfaceRenderer.cs   # Planet surface static helpers (terrain, settlements)
│   ├── ProjectileRenderer.cs      # Projectile trail rendering, damage popups, explosion effects (static)
│   ├── SurfaceEnemyRenderer.cs    # Surface enemy rendering (fauna/bandit sprites, health bars)
│   ├── InteriorRenderer.cs        # Interior static helpers (tiles, NPCs, labels)
│   ├── SettlementRenderer.cs      # Settlement rendering helper
│   └── SurfaceRockRenderer.cs     # Mineable rock rendering on planet surfaces (health bars, resource veins)
├── States/
│   ├── MainMenuState.cs           # Starting configuration menu (danger/location filters, seed controls, launch)
│   ├── SolarSystemState.cs        # Space flight within a solar system + overlays
│   ├── FTLTransitionState.cs      # Hyperspace jump animation between star systems
│   ├── OrbitalSurfaceTransitionState.cs # Cinematic landing/takeoff transition between orbit and planet surface
│   ├── PlanetSurfaceState.cs      # Planet surface exploration (tilemap)
│   └── InteriorState.cs           # Walkable station/settlement interiors
└── UI/
    ├── MenuWidget.cs              # Reusable scrollable menu widget (generic over enum)
    ├── Hud/
    │   ├── HudRenderer.cs             # Unified HUD: location info, stats, health bars, prompts, offscreen indicators
    │   └── HudMinimapRenderer.cs      # Unified minimap: data-driven renderer with markers, areas, player dot
    └── Overlays/
        ├── Base/
        │   └── OverlayBase.cs             # Abstract base class for all overlays
        ├── Customization/
        │   ├── AvatarCustomizationOverlay.cs # Avatar customization management UI
        │   ├── ShipCustomizationOverlay.cs   # Ship equipment management UI (dynamic slots per ship type)
        │   ├── VehicleCustomizationOverlay.cs # Vehicle customization management UI
        │   └── Base/
        │       └── CustomizationOverlayBase.cs  # Abstract base for all customization overlays
        ├── Map/
        │   ├── GalaxyMapOverlay.cs         # Dual-tab map overlay container (Solar System + Galaxy tabs)
        │   ├── GalaxyMapPanel.cs           # Galaxy star chart panel (hover, click-to-select, FTL travel)
        │   ├── PlanetLandingOverlay.cs     # Orbital landing site selection overlay container
        │   ├── PlanetLandingPanel.cs       # Planet terrain panel for landing site selection
        │   ├── PlanetSurfaceMapOverlay.cs  # Planet surface map overlay container (opened with M on surface)
        │   ├── PlanetSurfaceMapPanel.cs    # Planet surface terrain panel with settlement/ship markers
        │   ├── SolarSystemMapPanel.cs      # Solar system panel (planets, moons, stations, orbits)
        │   └── Base/
        │       ├── MapOverlayBase.cs       # Abstract base for map overlays (frame, layout, info panel)
        │       ├── MapPanelBase.cs         # Abstract base for map panels (camera, pan, zoom, WASD)
        │       └── PlanetMapPanelBase.cs   # Abstract base for planet map panels (terrain texture, settlements)
        └── Menu/
            ├── CargoListOverlay.cs         # Cargo hold overlay (view/discard cargo from in-game menu)
            ├── ControlsOverlay.cs          # Context-aware key bindings display overlay
            ├── DebugMenuOverlay.cs         # Main-menu debug utilities and showcase launchers
            ├── HealthStationOverlay.cs     # Avatar healing overlay (credits for HP)
            ├── InGameMenuOverlay.cs        # Pause menu overlay (Resume / Map / Missions / Cargo / Controls / Main Menu)
            ├── ListPanelOverlay.cs         # Abstract base for navigable list panel overlays
            ├── MainMenuOverlay.cs          # Main menu start-option selection overlay
            ├── MissionOverlay.cs           # Mission board overlay (Available / Active tabs)
            ├── MissionsListOverlay.cs      # Active missions list overlay (track/abandon)
            ├── RepairOverlay.cs            # Ship repair overlay
            ├── SellCargoOverlay.cs         # Sell resources for credits overlay
            ├── ShipDealerOverlay.cs        # Ship hull purchase/trade-in overlay
            ├── SpaceStationOverlay.cs      # Station docking overlay (rendered atop SolarSystem)
            ├── StarshipMenuOverlay.cs      # Starship menu on planet surface (Fly/Disembark)
            ├── TextInputOverlay.cs         # Generic text/numeric input panel (used for seed editing)
            └── Base/
                ├── MenuPanelOverlayBase.cs   # Abstract base for MenuWidget-driven panel overlays
                └── PanelOverlayBase.cs       # Root base for all centered-panel overlays (dimming, border, title)
```

## Command Line Options

```
dotnet run -- [--seed|-s <seed>] [--location|-l <location> [--sublocation|-sl <sublocation>]] [--showcase|-sc <showcase> [--star-type <type>]]
```

| Argument                                           | Description                                                                                   |
| -------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `--help`, `-h`, `/?`                               | Show CLI usage help and exit.                                                                 |
| `--seed <seed>`, `-s <seed>`                       | Optional explicit seed for deterministic world generation. If omitted, a random seed is used. |
| `--location <location>`, `-l <location>`           | Target top-level start location (`system`, `station`, `planet`, `settlement`).                |
| `--sublocation <sublocation>`, `-sl <sublocation>` | Target sub-location for the selected location.                                                |
| `--showcase <showcase>`, `-sc <showcase>`          | Launch a debug showcase directly (`star-type`, `planet-type`, `asteroid`, `surface-mining`).  |
| `--star-type <type>`                               | Optional star class override for `--showcase star-type` (default: `G`).                       |

**Location / sub-location matrix:**

| `--location` | `--sublocation` values                     |
| ------------ | ------------------------------------------ |
| `system`     | *(omit or use `none`)*                     |
| `station`    | `orbit`, `docked`, `inside`                |
| `planet`     | `orbit`, `landed`, `on-foot`, `on-vehicle` |
| `settlement` | `above`, `inside`, `on-foot`, `on-vehicle` |

> Note: Galaxy Map is opened from `SolarSystemState` with `M`.

**Examples:**
```
dotnet run                              # Random seed, main menu
dotnet run -- --help                    # Show CLI help
dotnet run -- --seed 12345              # Seed 12345, main menu
dotnet run -- -s 12345                  # Same as --seed
dotnet run -- --location system         # Random seed, jump to star system
dotnet run -- -l station -sl docked     # Alias form
dotnet run -- --location station --sublocation docked
dotnet run -- --seed 42 --location planet --sublocation on-foot
dotnet run -- --location settlement --sublocation on-vehicle
dotnet run -- --showcase planet-type
dotnet run -- --showcase star-type --star-type K
```

## Architecture Decisions

### Game States
The game uses a state machine pattern. Each state (`GameState` subclass) handles **rendering and input** while delegating simulation logic to a separate `ISimulation` instance. When switching states, the player is removed from the current simulation and added to the next — but the simulation itself stays alive in the `SimulationCoordinator` for a configurable timeout (90s), allowing seamless return without data loss.

`GameStateType` enum: `MainMenu`, `SolarSystem`, `PlanetSurface`, `Interior` (`FTLTransitionState` reports `SolarSystem` while playing the transition animation)

States:
- **MainMenuState**: Starting point selection with live preview. Animated starfield background with pulsing title glow. Uses **MainMenuOverlay** (extends `MenuPanelOverlayBase<MenuAction>`) to configure danger filter, location type, reroll location, edit seed, randomize seed, open debug tools, and start. Regenerates the entire galaxy via `Game.RegenerateGalaxy()` when seed changes, and updates preview text for the currently selected start context. Supports auto-launch via constructor parameter (for CLI location/sublocation flags). Displays the active galaxy seed and preview details.
- **SolarSystemState**: Rendering and input for space flight. Delegates all simulation logic to `SolarSystemSimulation` (obtained via `SimulationCoordinator.FindOrCreate`). Player controls ship with WASD. Orbiting planets/moons/stations rendered with sphere-shaded textures. Press E near planets/stations to interact. Press M to open the **GalaxyMapOverlay** (dual-tab map with Solar System and Galaxy views). Press Space to fire ship weapons (one or two equipped weapon slots with independent cooldowns and inherited ship velocity). Press Escape to open the **InGameMenuOverlay**. NPC ships (pirates, traders, patrols) spawn by danger level using `NpcShipLoadoutHelper` (ship type, parts, weapon specs, and loot scaling). Enemy AI now uses cruise targets, directional braking, and faction-specific combat behaviors. Thruster particles are simulated via ECS (`ParticleEmitter` + `ParticleSystem`) and rendered with `ParticleRenderer`. Destroyed enemies drop credits, resources, and equipment parts. Player death respawns at the nearest station with cargo/credit penalties (`DeathHullPercent` is currently 100%, so no hull loss). When docking at a station, a **SpaceStationOverlay** opens on top. When approaching a planet/moon, a **PlanetLandingOverlay** opens on top. Uses **anchor system** to keep the player ship tracking an orbiting body while overlays are active. The state creates input-only ECS systems (`ShipMovementSystem`, `CameraFollowSystem`) that operate on the simulation's ECS world. Supports auto-open parameters for seamless transitions from MainMenu or returning from other states.
- **PlanetSurfaceState**: Rendering and input for planet exploration. Delegates all simulation logic to `PlanetSurfaceSimulation` (whose `Parent` is the `SolarSystemSimulation`). Player avatar walks on generated terrain with per-tile brightness variation and terrain detail sprites. Lands at the site chosen in PlanetLandingOverlay (or map center by default), currently through `OrbitalSurfaceTransitionState` cinematic descent. On landing, the **StarshipMenuOverlay** opens giving options to Fly to Space, Disembark on Foot, or Disembark on Vehicle. The vehicle starts stored inside the starship and is only deployed when the player chooses to disembark on vehicle. Press E near ship to board (reopens StarshipMenuOverlay), E near settlement to enter interior, E near deployed vehicle to mount, E while in vehicle to dismount (or board ship if near it). Ship and vehicle positions are preserved when entering/exiting settlements. When leaving the planet, return to orbit uses `OrbitalSurfaceTransitionState` takeoff animation and the vehicle always returns with the starship regardless of deployment state. Avatar walk speed and vehicle physics are dynamically computed from equipped avatar/vehicle parts. Press M to open the **PlanetSurfaceMapOverlay** (terrain overview with ship/player/vehicle markers and selectable settlements). Surface combat: hostile fauna and bandits spawn on walkable terrain away from the landing zone and settlements. Player fires projectiles with Space (movement direction) or left mouse button (aim at cursor). Avatar has persistent HP with an equipped weapon slot affecting damage. Damage popups, explosions, loot drops (credits + resources) on enemy kills. Death triggers an in-place respawn after a 2.5-second timer — the avatar is recreated near the ship at full HP with a 10% credit penalty (no return to orbit). While dead, all input except menu-back is blocked. Avatar health bar displayed in HUD; enemy health bars shown above enemies; enemy dots on minimap. Press Escape to open the **InGameMenuOverlay**. The state creates input-only ECS systems (`AvatarMovementSystem`, `CameraFollowSystem`) on the simulation's ECS world.
- **FTLTransitionState**: Intermediate animation state played during FTL jumps between star systems. 2D side-view style: the player's ship is rendered center-screen facing right with engine exhaust and FTL trail effects. Four phases: charge-up (1.6s — ship shakes, stars begin moving, engine glow builds), jump flash (0.15s — bright white-blue flash), hyperspace travel (2.5s — fast horizontal star streaks scrolling left, vertical energy waves sweeping across, long blue FTL trail behind ship), and exit flash (1.6s — arrival flash fades to reveal the new system). No player input is accepted. Automatically transitions to the target `SolarSystemState` when the animation completes. Triggered from `GalaxyMapPanel.TravelToSelected()` instead of a direct state change.
- **OrbitalSurfaceTransitionState**: Bidirectional cinematic transition between orbit and surface. Landing mode animates ship alignment, descent, and touchdown while blending from orbital body rendering into terrain; takeoff mode plays the reverse flow. Input is disabled during transition. On completion, it changes state to `PlanetSurfaceState` (landing) or `SolarSystemState` (takeoff) and updates return-context metadata in `PlayerData`.
- **InteriorState**: Rendering and input for walkable interiors. Delegates simulation logic to `InteriorSimulation` (whose `Parent` is the `PlanetSurfaceSimulation`). Procedurally generated rooms connected by corridors (stations) or streets (settlements). Features NPCs with dialogue, repair stations, mission boards, cargo terminals, and customization terminals (ship, ship dealer, avatar, vehicle). Station docking bays have five terminals: exit door, ship customization, ship dealer, avatar customization, and vehicle customization. Avatar walk speed is dynamically computed from equipped avatar parts. Minimap shows room layout, NPCs, and interactable objects with color-coded dots. No combat in interiors. No InGameMenuOverlay — Escape closes dialogues/overlays. The state creates input-only ECS systems (`AvatarMovementSystem`, `CameraFollowSystem`) on the simulation's ECS world.

### Simulation Architecture
Simulation logic (ECS entity management, physics, combat, AI) is fully separated from rendering and input. Each game state delegates its domain logic to an `ISimulation` instance while retaining only rendering, overlay management, and input-to-ECS bridging.

**Core types** (`Simulation/`):
- `ISimulation` — interface: `EcsWorld`, `HasPlayers`, `Parent`, `Create()`, `Destroy()`, `Update(UpdateContext)`, `AddPlayer()`, `RemovePlayer()`
- `UpdateContext` — readonly record struct passed each tick: `Dt`, `GlobalTime`
- `AddContext` — readonly record struct for per-join data: `LandingTileX`, `LandingTileY`
- `SimulationPlayer` — encapsulates a player's presence: `PlayerData` + `Entity` in the simulation's ECS world

**Class hierarchy** (`Simulation/Base/` contains the abstract base classes):
```
ISimulation
└── SimulationBase (abstract)                  Simulation/Base/SimulationBase.cs
    ├── CombatSimulationBase (abstract)        Simulation/Base/CombatSimulationBase.cs
    │   ├── SolarSystemSimulation
    │   └── PlanetSurfaceSimulation
    └── InteriorSimulation
```

**SimulationBase** — abstract base providing:
- ECS `World` lifecycle (create on construction, dispose on destroy)
- Player management: `Players`, `LocalPlayer`, `HasPlayers`
- Template method `AddPlayer()`: calls `CreatePlayerEntity()` (abstract) → registers player → stores `LocalPlayer`
- Template method `RemovePlayer()`: calls `DestroyPlayerEntity()` (virtual hook for cleanup) → destroys entity → updates list
- Lookup helpers: `FindPlayerByEntity(Entity)`, `IsLocalPlayerEntity(Entity)`, `FindLocalPlayerByEntity(Entity)`
- `Parent` link to the parent simulation (e.g., `PlanetSurfaceSimulation.Parent` → `SolarSystemSimulation`)

**CombatSimulationBase** — intermediate base for combat simulations:
- `PlayerDead`, `RespawnTimer` — death/respawn tracking
- `CombatMessage`, `CombatMessageTimer` — floating loot/kill messages
- `CombatMusicTimer` — combat music auto-disengage timer
- `UpdateCombatTimers(dt)` — shared timer tick logic

**SimulationCoordinator** — owned by `Game`, manages all active simulations:
- `Register(ISimulation)` / `Unregister(ISimulation)` — manual lifecycle control
- `FindOrCreate<T>(predicate, builder)` — reuse existing simulation or create a new one
- `Update(UpdateContext)` — ticks all simulations every frame (never paused by overlays)
- **Empty timeout**: simulations with no players are destroyed after 90 seconds
- **Parent chain keep-alive**: when a simulation has players, its entire ancestor chain is kept alive

**State ↔ Simulation interaction pattern**:
1. State `Enter()` calls `game.Coordinator.FindOrCreate<T>(...)` to get/create its simulation
2. State calls `simulation.AddPlayer(game.Player)` to join
3. State creates input-only ECS systems (movement, camera) on `simulation.EcsWorld`
4. State reads simulation public properties for rendering (entities, combat state, messages)
5. State `Exit()` calls `simulation.RemovePlayer(player)` — simulation stays alive in coordinator

### Platform Abstraction Layer
The `Platform/` folder defines a clean interface boundary between game logic and the concrete SDL3 implementation. Every platform-capability is exposed via a C# interface; the rest of the codebase only references these interfaces. Swapping the renderer or input backend requires no changes outside `Platform/Sdl/`.

**Top-level interface: `IPlatform`** — aggregates all platform capabilities:
```
IPlatform
├── ISpriteRenderer  — 2D primitives, texture draw, text (world & screen space), clip regions
├── ITextureManager  — CreateFromPixels / SetPixelBlock texture creation helpers
├── IInputManager    — keyboard, mouse, gamepad; action/axis abstraction; text input
└── IAudioManager    — music theme switching, SFX playback, volume controls
```

**`IInputManager`** replaces the former `Core/InputManager.cs`. It has:
- Action-based API: `IsActionDown/Pressed/Released(InputAction)`, `GetActionAxisDirection(InputActionAxis)`
- Mouse helpers: `IsMouseDown/Pressed/Released(MouseButton)`, `MouseX/Y/MouseWheelY`
- Text input support: `TextInput`, `TextInputBackspacesCount`, `TextInputReturnsCount`
- `InputMethod` enum (`MouseKeyboard` / `Gamepad`) and `MovementInputMode`
- `InputTypes.cs` defines all enums: `InputAction`, `InputActionAxis`, `MouseButton`, `InputMethod`, `MovementInputMode`

**`IAudioManager`** replaces `Audio/AudioManager.cs`. It wraps `MusicGenerator` and `SfxGenerator` behind a platform interface. All game states and simulations call `game.Platform.AudioManager` (or the injected reference) — they never import SDL directly.

**SDL3 Implementations** (`Platform/Sdl/`):
| Interface          | SDL3 Implementation  | Notes                                                       |
| ------------------ | -------------------- | ----------------------------------------------------------- |
| `IPlatform`        | `SdlPlatform`        | Window creation, renderer lifecycle, frame timing           |
| `ISpriteRenderer`  | `SdlSpriteRenderer`  | SDL3 draw calls, texture rendering, rotation, alpha         |
| `ITextureManager`  | `SdlTextureManager`  | `SDL_CreateTexture`, pixel upload                           |
| `IFontRenderer`    | `SdlFontRenderer`    | Wraps `MiniBitmapFont` for text rasterization               |
| `ITileMapRenderer` | `SdlTileMapRenderer` | Visible-tile culling, hash-based brightness, detail sprites |
| `IInputManager`    | `SdlInputManager`    | SDL3 event polling, keyboard + mouse + gamepad mapping      |
| `IAudioManager`    | `SdlAudioManager`    | SDL3 audio device stream, push-based mixing, crossfade      |

**Generation abstraction: `IUniverseGenerator`** (`Generation/IUniverseGenerator.cs`) — a parallel platform abstraction for world content. Defines `GenerateGalaxy()`, `GenerateSolarSystem()`, `GeneratePlanetSurface()`, `GenerateStationInterior()`, `GenerateSettlementInterior()`, and `GenerateBoardMissions()`. The default implementation is `ProceduralUniverseGenerator` (`Generation/Procedural/`), which delegates to the individual static generator classes. Showcase modes use dedicated `IUniverseGenerator` subclasses (`Generation/Showcase/`) that override specific methods to return curated content.

### Overlays
Overlays are semi-transparent UI layers rendered on top of a game state. All overlays inherit from `OverlayBase`, which provides:
- `IsOpen` property (protected set)
- `UpdateInput(Game)` — returns `true` if the overlay consumed input (blocks parent state)
- `Update(Game, float dt)` — fixed-timestep simulation
- `Render(Game)` — abstract rendering
- `Close()` — sets `IsOpen = false`

**Overlay class hierarchy**:
```
OverlayBase                             (Overlays/Base/OverlayBase.cs)
├── MapOverlayBase                      (Overlays/Map/Base/MapOverlayBase.cs)
│   ├── GalaxyMapOverlay                (Overlays/Map/GalaxyMapOverlay.cs)
│   ├── PlanetLandingOverlay            (Overlays/Map/PlanetLandingOverlay.cs)
│   └── PlanetSurfaceMapOverlay         (Overlays/Map/PlanetSurfaceMapOverlay.cs)
├── PanelOverlayBase                    (Overlays/Menu/Base/PanelOverlayBase.cs)
│   ├── MenuPanelOverlayBase<T>         (Overlays/Menu/Base/MenuPanelOverlayBase.cs)
│   │   ├── InGameMenuOverlay           (Overlays/Menu/InGameMenuOverlay.cs)
│   │   ├── MainMenuOverlay             (Overlays/Menu/MainMenuOverlay.cs)
│   │   ├── DebugMenuOverlay            (Overlays/Menu/DebugMenuOverlay.cs)
│   │   ├── SpaceStationOverlay         (Overlays/Menu/SpaceStationOverlay.cs)
│   │   └── StarshipMenuOverlay         (Overlays/Menu/StarshipMenuOverlay.cs)
│   ├── ListPanelOverlay                (Overlays/Menu/ListPanelOverlay.cs)
│   │   ├── CargoListOverlay            (Overlays/Menu/CargoListOverlay.cs)
│   │   ├── MissionOverlay              (Overlays/Menu/MissionOverlay.cs)
│   │   ├── MissionsListOverlay         (Overlays/Menu/MissionsListOverlay.cs)
│   │   ├── SellCargoOverlay            (Overlays/Menu/SellCargoOverlay.cs)
│   │   └── ShipDealerOverlay           (Overlays/Menu/ShipDealerOverlay.cs)
│   ├── RepairOverlay                   (Overlays/Menu/RepairOverlay.cs)
│   ├── HealthStationOverlay            (Overlays/Menu/HealthStationOverlay.cs)
│   ├── ControlsOverlay                 (Overlays/Menu/ControlsOverlay.cs)
│   └── TextInputOverlay                (Overlays/Menu/TextInputOverlay.cs)
└── CustomizationOverlayBase            (Overlays/Customization/Base/CustomizationOverlayBase.cs)
    ├── ShipCustomizationOverlay        (Overlays/Customization/ShipCustomizationOverlay.cs)
    ├── AvatarCustomizationOverlay      (Overlays/Customization/AvatarCustomizationOverlay.cs)
    └── VehicleCustomizationOverlay     (Overlays/Customization/VehicleCustomizationOverlay.cs)

MapPanelBase                            (Overlays/Map/Base/MapPanelBase.cs)
├── SolarSystemMapPanel                 (Overlays/Map/SolarSystemMapPanel.cs)
├── GalaxyMapPanel                      (Overlays/Map/GalaxyMapPanel.cs)
└── PlanetMapPanelBase                  (Overlays/Map/Base/PlanetMapPanelBase.cs)
    ├── PlanetLandingPanel              (Overlays/Map/PlanetLandingPanel.cs)
    └── PlanetSurfaceMapPanel           (Overlays/Map/PlanetSurfaceMapPanel.cs)
```

**`PanelOverlayBase`** provides a complete centered-panel framework: background dimming, bordered panel with title/separator, credits display, controls hint, timed status messages, Escape-to-close, and click-outside-to-close. All menu/list overlays in `Overlays/Menu/` inherit from it.

**`MenuPanelOverlayBase<T>`** extends `PanelOverlayBase` for overlays driven by a `MenuWidget<T>` enum-based menu. Handles menu input delegation, sub-overlay lifecycle, and `OnOptionSelected` callbacks.

**`ListPanelOverlay`** extends `PanelOverlayBase` for overlays with a navigable list of items. Provides keyboard/mouse navigation, confirm/secondary-action callbacks, and tab switching.

**`MapOverlayBase`** provides a full-screen map overlay framework: dark background, bordered frame with header strip, clipped map content area, and an info panel beside the map. Delegates all content to a `MapPanelBase` panel returned by `GetActivePanel()`. Subclasses supply the panel, header rendering, and optional HUD elements.

**`MapPanelBase`** is the abstract base for map panels displayed inside a `MapOverlayBase`. Provides a dedicated `Camera`, mouse-wheel zoom-to-cursor, drag panning, WASD/arrow key movement, and shared rendering helpers (target brackets, mission diamonds, info panel headers). Each panel implements `Open`, `Close`, `SetupCamera`, `UpdateInput`, `RenderContent`, and `RenderInfoPanel`.

**`PlanetMapPanelBase`** extends `MapPanelBase` for planet/moon terrain maps. Provides terrain texture creation/rendering, settlement marker rendering, camera clamping within terrain bounds, and shared lifecycle stubs. Used by both `PlanetLandingPanel` and `PlanetSurfaceMapPanel`.

Key overlays:
- **GalaxyMapOverlay** (drawn over SolarSystemState): Full-screen dual-tab map overlay container. Switches between two panels via M/Tab key or clickable header tabs:
  - **Solar System tab** (`SolarSystemMapPanel`): Interactive map of the current solar system showing the star, orbiting planets, moons, and stations at their orbital positions. Click to select objects, double-click to set navigation target. Info panel shows object details (type, radius, orbit, moons, terrain, settlements, danger). Renders orbit lines, mission target markers, and animated selection brackets.
  - **Galaxy tab** (`GalaxyMapPanel`): Bird's-eye view of the galaxy. Click to select star systems, double-click or Enter to travel. Mouse drag to pan. Nebula clouds and glow-textured stars. Shows FTL range and fuel range circles. Traveling to a different system spends fuel and transitions to a new SolarSystemState.
  
  Both panels share a common layout via `MapOverlayBase` with an 800×700 map area and 280px info panel. Opened with M key from SolarSystemState, defaults to Solar System tab. Closed with Escape. Each panel manages its own camera state independently.
- **SpaceStationOverlay** (drawn over SolarSystemState): Semi-transparent menu drawn when docked. Refuels ship on docking. 9 menu options: Repair, Missions, Sell Cargo, Ship Customization, Ship Dealer, Avatar Customization, Vehicle Customization, Walk Station, Exit. Walk Station transitions to InteriorState; Exit closes the overlay and returns to free flight. Hosts 7 sub-overlays.
- **PlanetLandingOverlay** (drawn over SolarSystemState): Orbital landing site selection overlay. Uses `MapOverlayBase` framework (700×700 map, 260px info panel) and delegates terrain rendering to `PlanetLandingPanel` (extends `PlanetMapPanelBase`). Shows full terrain map as a texture (1px = 1 tile) with settlement markers. The player clicks to choose a landing site; reticle with terrain info panel shows selected terrain type and position. Supports zoom, pan via mouse drag and WASD. Cannot land on water/lava/void. Confirms with Enter/E, cancels with Escape. Supports moon landing (tracks moon context for correct return). Ship is anchored to the orbiting body via the anchor system while the overlay is active.
- **MainMenuOverlay** (drawn over MainMenuState): Main configuration overlay. Extends `MenuPanelOverlayBase<MenuAction>` with centered alignment, large text, and descriptions. 7 options: danger level filter, location type filter, randomize location, edit seed, random seed, debug menu, and start game. No dimming (MainMenuState draws its own background). Escape does nothing. Uses `TextInputOverlay` as a sub-overlay for numeric seed input.
- **InGameMenuOverlay** (drawn over SolarSystemState and PlanetSurfaceState): Pause/escape menu toggled with Escape key. Options include Resume, Map, Missions List, Cargo, Controls, and Main Menu. Uses `MenuPanelOverlayBase<InGameMenuOption>`. Not used in InteriorState.
- **CargoListOverlay** (drawn as sub-overlay of InGameMenuOverlay): Displays current cargo and allows discarding one unit of a selected resource or discarding all cargo with confirmation. Extends `ListPanelOverlay`.
- **DebugMenuOverlay** (drawn as sub-overlay of MainMenuState): Developer/debug utilities panel for launching showcase generators (e.g., star type, planet type, asteroid mining, surface mining). Extends `MenuPanelOverlayBase<DebugMenuAction>`.
- **ControlsOverlay** (drawn as sub-overlay of InGameMenuOverlay): Displays context-appropriate key bindings based on the current `GameStateType`. Extends `PanelOverlayBase`.
- **MissionsListOverlay** (drawn as sub-overlay of InGameMenuOverlay): Lists all active missions with status, progress, and rewards. Allows tracking (Enter) or abandoning (X) missions. Extends `ListPanelOverlay`.
- **HealthStationOverlay** (drawn over InteriorState): Available at health station NPCs. Shows avatar HP bar and offers full healing for credits (1 credit per HP). Extends `PanelOverlayBase`.
- **PlanetSurfaceMapOverlay** (drawn over PlanetSurfaceState): Planet surface terrain overview overlay. Uses `MapOverlayBase` framework (700×700 map, 260px info panel) and delegates rendering to `PlanetSurfaceMapPanel` (extends `PlanetMapPanelBase`). Shows terrain map with ship, player, and vehicle markers. Settlements are clickable — click to select, double-click to set as navigation target. Info panel shows selected object details (settlement name, terrain type, distance). Opened with M key from PlanetSurfaceState, closed with M or Escape.
- **StarshipMenuOverlay** (drawn over PlanetSurfaceState): Shown when landing on a planet or boarding the starship on the surface. Three options: Fly to Space (return to orbit), Disembark on Foot (exit ship walking), Disembark on Vehicle (deploy vehicle and drive). Vehicle option is disabled if the player has no vehicle. Uses `MenuWidget<StarshipMenuOption>`.
- **RepairOverlay**: Ship hull repair interface. Cost: 2 credits per damage point (full repair only). Available from SpaceStationOverlay and interior RepairStation terminals.
- **MissionOverlay**: Mission board interface with two tabs (Available / Active). Available tab shows missions generated for the current station/settlement board; Active tab shows the player's accepted missions. Accept missions with Enter/E (max 3 active), turn in completed missions with Enter, abandon missions with X, switch tabs with A/D. Missions are generated deterministically per board using seeded RNG; accepted/completed missions are filtered out so they won't re-appear.
- **SellCargoOverlay**: Sell mined resources for credits. Lists cargo with amounts and values. Navigate with Up/Down, sell individual resources or "SELL ALL" with Enter. Available from SpaceStationOverlay and interior CargoTerminal.
- **ShipDealerOverlay**: Buy/trade-in ship hulls. Two-column layout with ship list and detailed stat comparison. Trade-in pricing: net cost = buy price − current ship sell value. Slot comparison shows gained/lost slots. Triggers `SwitchShipType()`.
- **CustomizationOverlayBase**: Abstract base for Ship/Avatar/Vehicle customization overlays. Provides two-column UI layout, input handling, equip/buy/sell logic, and rendering.
- **ShipCustomizationOverlay**: Ship equipment management with dynamic slot list per ship type.
- **AvatarCustomizationOverlay**: Avatar gear management (Suit, Helmet, Boots, Weapon).
- **VehicleCustomizationOverlay**: Vehicle part management (Engine, Chassis, Lights).

### Anchor System
When an overlay opens in SolarSystemState (e.g., SpaceStationOverlay, PlanetLandingOverlay), the player ship is "anchored" to the target celestial body. This means the ship's position tracks the orbiting body's position while the overlay is displayed, so orbits keep animating and the ship stays visually attached to the station or planet. The anchor is cleared when the overlay closes.

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

**Generation folder structure**:
- `IUniverseGenerator` defines the full content-generation contract; `Game` always works through this interface.
- `Generation/Procedural/ProceduralUniverseGenerator` is the default implementation — a thin orchestrator that calls the individual static generator classes (`GalaxyGenerator`, `SolarSystemGenerator`, `PlanetSurfaceGenerator`, `InteriorGenerator`, `MissionGenerator`).
- `Generation/Showcase/` contains alternative `IUniverseGenerator` implementations for debug showcases (`StarTypeShowcase`, `PlanetTypeShowcase`, `AsteroidMiningShowcase`, `SurfaceMiningShowcase`). They subclass `ProceduralUniverseGenerator` and override only the methods needed for the showcase.
- `SurfaceTerrainRules` centralizes walkability/landing/spawn validation so that `PlanetSurfaceGenerator`, `PlanetSurfaceSimulation`, and the landing overlay all use identical rules.

### ECS Usage (Arch)
Components are plain structs defined in `Components.cs`. The game uses Arch's `World.Query()` with lambda syntax for ad-hoc iteration, plus dedicated **systems** for recurring logic. Key component types:
- `Transform` — position + rotation
- `Velocity` — linear velocity + acceleration + damping + rotation velocity (`RotationVelocity`, `MaxRotationSpeed`) + optional movement delegate (`CanMoveTo`)
- `Sprite` — rendering info (texture or colored rect via `ColoredRect()` factory)
- `CelestialBody` — star/planet/moon/station properties (type, name, radius, data index, has solid surface)
- `Orbit` — orbital mechanics (parent entity, radius, speed, base angle, current angle)
- `PlayerControlled` — tag for player entity
- `Label` — text displayed near entity (with Y offset)
- `Interactable` — landing/docking capability (`InteractionType`: LandOnPlanet / DockAtStation)
- `StarSystemMarker` — galaxy map marker (system index, name, star class)
- `Health` — hull + shield HP, shield regen rate/delay, damage tracking; `TakeDamage()` absorbs shields first
- `Projectile` — damage, speed, lifetime, collision radius, owner faction, RGB color
- `EnemyAI` — mutable state (`State`, `StateTimer`, per-weapon cooldown array) + immutable `EnemyAIConfig` record (faction, detection/combat ranges, weapon specs, acceleration, MaxRotationSpeed)
- `SurfaceAI` — mutable state (State, StateTimer, FireCooldown, WanderTimer, WanderAngle) + immutable `SurfaceAIConfig` record (faction, detect/attack range, walk speed, fire rate)
- `LootDrop` — credit ranges, resource/part drop chances, danger level scaling
- `AsteroidField` — mineable asteroid tag (resource type, amount, size)
- `ParticleEmitter` — configurable emitter attached to an entity (`EmitCondition`, spawn interval, speed/lifetime/size/drag/color)
- `Particle` — per-particle simulation state (velocity, age/lifetime, size curve, drag, color)
- `OwnedBy` — ownership link for dependent entities that should be destroyed with their owner
- `Faction` — enum: Player, Pirate, Trader, Patrol, Fauna, Bandit
- `AIState` — enum: Idle, Patrol, Chase, Attack, Flee, Defend

### Entity Factory
`EntityFactory` (static class in `ECS/EntityFactory.cs`) centralizes all entity creation to ensure consistent component compositions:

| Factory Method       | Components Created                                                         |
| -------------------- | -------------------------------------------------------------------------- |
| `CreateStar`         | Transform, Sprite, CelestialBody, Label                                    |
| `CreatePlanet`       | Transform, Sprite, CelestialBody, Orbit, Label, +Interactable if solid     |
| `CreateMoon`         | Transform, Sprite, CelestialBody, Orbit, Label, Interactable               |
| `CreateStation`      | Transform, Sprite, CelestialBody, Orbit, Label, Interactable               |
| `CreateAsteroid`     | Transform, Sprite, Orbit, Health, AsteroidField                            |
| `CreatePlayerShip`   | Transform, Sprite, Velocity, PlayerControlled, Health                      |
| `CreatePlayerAvatar` | Transform, Sprite, Velocity, PlayerControlled, +Health if on surface       |
| `CreateLandedShip`   | Transform, Sprite, Label                                                   |
| `CreateVehicle`      | Transform, Sprite, Label                                                   |
| `CreatePirateShip`   | Transform, Sprite, Velocity, Health, EnemyAI, LootDrop (scaled by danger)  |
| `CreateTraderShip`   | Transform, Sprite, Velocity, Health, EnemyAI (unarmed, flees)              |
| `CreatePatrolShip`   | Transform, Sprite, Velocity, Health (shielded), EnemyAI (no flee, no loot) |
| `CreateProjectile`   | Transform, Velocity, Projectile                                            |
| `CreateFauna`        | Transform, Sprite, Velocity, Health, SurfaceAI, LootDrop                   |
| `CreateBandit`       | Transform, Sprite, Velocity, Health, SurfaceAI, LootDrop                   |

### ECS Systems
Systems live in `ECS/Systems/` (organized into `Movement/`, `Combat/`, `AI/`, and `Effects/` subdirectories) and encapsulate reusable game logic.

Most systems extend `BaseSystem<World, float>` and use Arch's source generator via `[Query]` and `[All(typeof(T))]` attributes on partial methods. The source generator auto-implements `Update()` to iterate matching entities.

> **Important**: The source generator overrides `Update()` without calling `BeforeUpdate()` / `AfterUpdate()`. Do not rely on those lifecycle hooks for per-frame state reset. Systems that need lifecycle control should be plain classes with manual `World.Query()` calls instead.

| System                           | Location            | Base Class                  | Queries                                                   | Used By                                                     |
| -------------------------------- | ------------------- | --------------------------- | --------------------------------------------------------- | ----------------------------------------------------------- |
| **OrbitSystem**                  | `Systems/`          | `BaseSystem` (source gen)   | `Transform + Orbit`                                       | SolarSystemSimulation                                       |
| **VelocitySystem**               | `Systems/`          | `BaseSystem` (source gen)   | `Transform + Velocity`                                    | SolarSystemSimulation, PlanetSurfaceSimulation              |
| **CameraFollowSystem**           | `Systems/`          | `BaseSystem` (source gen)   | `PlayerControlled + Transform`                            | SolarSystemState, PlanetSurfaceState, InteriorState (input) |
| **InteractionProximitySystem**   | `Systems/`          | `BaseSystem` (manual query) | `Transform + CelestialBody + Interactable`                | SolarSystemSimulation                                       |
| **DependentEntityCleanupSystem** | `Systems/`          | `BaseSystem` (manual query) | `OwnedBy`                                                 | SolarSystemSimulation, PlanetSurfaceSimulation              |
| **AvatarMovementSystem**         | `Systems/Movement/` | `BaseSystem` (source gen)   | `PlayerControlled + Transform + Velocity`                 | PlanetSurfaceState, InteriorState (input)                   |
| **ShipMovementSystem**           | `Systems/Movement/` | `BaseSystem` (manual)       | Single entity                                             | SolarSystemState (input)                                    |
| **VehicleMovementSystem**        | `Systems/Movement/` | `BaseSystem` (manual)       | Single entity                                             | PlanetSurfaceState (input)                                  |
| **ProjectileSystem**             | `Systems/Combat/`   | `BaseSystem` (source gen)   | `Transform + Velocity + Projectile`, `Transform + Health` | SolarSystemSimulation, PlanetSurfaceSimulation              |
| **ShieldRegenSystem**            | `Systems/Combat/`   | `BaseSystem` (source gen)   | `Health`                                                  | SolarSystemSimulation                                       |
| **ShipEnemyAISystem**            | `Systems/AI/`       | `BaseSystem` (source gen)   | `Transform + Velocity + EnemyAI + Health`                 | SolarSystemSimulation                                       |
| **AvatarEnemyAISystem**          | `Systems/AI/`       | `BaseSystem` (source gen)   | `Transform + Velocity + SurfaceAI + Health`               | PlanetSurfaceSimulation                                     |
| **ParticleSystem**               | `Systems/Effects/`  | `BaseSystem` (manual query) | `Transform + Particle`, `Transform + ParticleEmitter`     | SolarSystemSimulation                                       |

Rendering helpers (not ECS systems but query ECS data):
| Helper              | Location          | Description                                                                                 |
| ------------------- | ----------------- | ------------------------------------------------------------------------------------------- |
| **LabelRenderer**   | `Rendering/`      | Queries `Transform + Label` and draws centered text below entities                          |
| **TileMapRenderer** | `Rendering/Base/` | Static utility for visible tilemap rendering with hash-based brightness and detail callback |

System details:
- **OrbitSystem**: Computes deterministic orbital positions from global time. Accepts `Func<float>` for time and `Func<Vector2>` for fallback center.
- **VelocitySystem**: Integrates acceleration into velocity each frame, applies `MaxSpeed` clamping, applies centralized damping (`Velocity.Damping`), updates position via `CanMoveTo` collision checks, and applies `RotationVelocity` to `Transform.Rotation` (clamped by `MaxRotationSpeed`).
- **AvatarMovementSystem**: Handles WASD/arrow input with configurable speed. Exposes a `Func<Vector2, bool>? CanMoveTo` delegate for collision checking (terrain, walls).
- **CameraFollowSystem**: Lerps camera toward the player entity and handles mouse-wheel zoom.
- **ShipMovementSystem**: Handles ship input intent — A/D rotation, W thrust, S braking. Sets `Velocity.Acceleration`, `Velocity.RotationVelocity`, and `Velocity.Damping`; actual movement integration is handled by `VelocitySystem`. Reads equipped ship stats (acceleration, maxSpeed, rotationSpeed) from `PlayerData`.
- **InteractionProximitySystem**: Finds the nearest interactable entity to a given position. Extends `BaseSystem<World, float>` with a static cached `QueryDescription` and manual iteration via `FindNearest(Vector2)`.
- **DependentEntityCleanupSystem**: Removes entities with `OwnedBy` when their owner entity is no longer alive, preventing orphaned dependent entities across combat and transition flows.
- **VehicleMovementSystem**: Handles vehicle input intent — thrust along facing direction, A/D rotation, braking, friction. Extends `BaseSystem<World, float>` with manual `Update()` and configurable physics params; actual movement integration is handled by `VelocitySystem`.
- **ProjectileSystem**: Extends `BaseSystem<World, float>` with source-generated iteration over `Transform + Velocity + Projectile`. Uses per-frame snapshot lists plus `HashSet<Entity>` tracking for expired/processed projectiles and manual target queries over `Transform + Health`. Faction logic prevents friendly fire. Exposes `DestroyedLastUpdate` and `DamageEventsLastUpdate` for states to process loot drops, explosions, and damage popups.
- **ShieldRegenSystem**: Source-generated system that regenerates shields after a configurable delay (`ShieldRegenDelay`) since last hit. Regen rate is per-second (`ShieldRegenRate`).
- **ShipEnemyAISystem**: Extends `BaseSystem<World, float>` with source-generated iteration. Uses flyweight `EnemyAIConfig` records and per-entity mutable state. Implements smooth turning plus acceleration-based steering, directional braking, and per-entity cruise targets to avoid edge drift. Pirates engage player/traders and flee when low health; traders cruise/flee; patrols hunt pirates. Fires projectiles via a deferred spawn list, carrying inherited ship velocity for more consistent ballistic behavior.
- **AvatarEnemyAISystem**: Extends `BaseSystem<World, float>` with source-generated iteration. Uses flyweight `SurfaceAIConfig` records. Sets acceleration intent toward desired velocity (`SetAccelerationTowardVelocity`) so movement is integrated by `VelocitySystem`; fauna chase/wander/short-range attack, while bandits patrol/chase/strafe/fire/flee.
- **ParticleSystem**: Manual ECS effects system over `ParticleEmitter` and `Particle` components. Simulates particle drag/lifetime, queues spawns from active emitters (`Always`, `Never`, `WhenAccelerating`), caps live particle count, and supports viewport-based emitter validation bounds for performance.
- **CombatHelper**: Static utility class in `Core/` providing shared combat logic: `ProcessLootDrop` (unified loot with configurable resource amounts and part drops), `CreateDamagePopups`, `UpdateCombatMessageTimer`, `UpdateVisualEffects`. Used by both SolarSystemSimulation and PlanetSurfaceSimulation. Part drops are gated by `enablePartDrops` flag (space combat only) and tier is capped by danger level. Won't drop parts already owned or equipped.

### Rendering
The `ISpriteRenderer` interface (implemented by `SdlSpriteRenderer`) provides both SDL3 draw primitives (filled rects, circles, lines) and texture-based rendering with rotation and alpha support, in both world-space and screen-space variants. The `ITextureManager` interface (implemented by `SdlTextureManager`) exposes:
- `CreateTextureFromPixels(byte[] pixels, int width, int height)` — creates an SDL texture from raw RGBA pixel data
- `SetPixelBlock(...)` — static helper to fill rectangular pixel regions

**`Rendering/Base/`** now contains only two files:
- `MiniBitmapFont.cs` — built-in 5×8 pixel font data (used by `SdlFontRenderer`)
- `RenderColors.cs` — shared color/style constants (`HealthBarBackground`, `ShieldBarFill`, `StarCoreHighlight`, etc.) used across multiple renderers

The former `FontRenderer.cs`, `SpriteRenderer.cs`, `TextureManager.cs`, and `TileMapRenderer.cs` that used to live in `Rendering/Base/` have been moved into the Platform abstraction layer (`ISpriteRenderer`, `ITextureManager`, `IFontRenderer`, `ITileMapRenderer` interfaces + SDL3 implementations in `Platform/Sdl/`).

**Entity Renderers** follow a consistent pattern: each is an `IDisposable` class that receives an `ITextureManager` in its constructor, generates its own textures procedurally, owns them for their lifetime, and provides `Render()`/rendering methods. They are all owned by `Game` and disposed on shutdown.

| Renderer              | Texture Ownership                                 | Key Methods                                                                                               |
| --------------------- | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| **AvatarRenderer**    | Singleton texture (16×16 humanoid)                | `Render(renderer, camera, position)`                                                                      |
| **VehicleRenderer**   | Singleton texture (20×20 rover)                   | `Render(renderer, camera, position, rotation, isMounted)`                                                 |
| **SpaceshipRenderer** | Per-type solar + landed textures, flame texture   | `RenderFlying(...)`, `RenderLanded(...)`                                                                  |
| **EnemyShipRenderer** | 3 faction textures (pirate/trader/patrol) + flame | `Render(renderer, camera, position, rotation, faction, isThrusting)`, `RenderHealthBar(...)`              |
| **StationRenderer**   | Singleton texture (32×32 station)                 | `RenderStations(renderer, camera, ecsWorld, entities, globalTime)`                                        |
| **AsteroidRenderer**  | Singleton texture (12×12 rock)                    | `RenderAsteroids(renderer, camera, asteroids, center, globalTime)`                                        |
| **PlanetRenderer**    | Factory — tracks all created textures             | `CreateTexture(size, r, g, b, seed)`, `RenderPlanetsAndMoons(...)`, `DestroyTexture(tex)`, `DestroyAll()` |
| **StarRenderer**      | Factory — tracks all created textures             | `CreateTexture(size, r, g, b)`, `Render(...)`, `DestroyTexture(tex)`, `DestroyAll()`                      |

**HUD Renderers** are static helper classes providing a unified HUD shared across all game states:
- **HudRenderer** — unified top-left HUD (location info, credits/cargo, health/shield bars, danger level), bottom-center interaction prompts (planet/station panels, board ship, enter settlement, NPC dialogue), and offscreen edge indicators (NPC ships, star, settlements). Used by SolarSystemState, PlanetSurfaceState, and InteriorState.
- **HudMinimapRenderer** — unified data-driven minimap renderer (top-right). Accepts `MinimapMarker[]` (point entities) and `MinimapArea[]` (rectangular regions) in world coordinates. Supports player-centered scrolling view (solar system, planet surface) and full-map view (interiors). Types: `MinimapMarkerShape` (Rect/Circle), `MinimapMarker` record struct (WorldPos, RGBA, Size, Shape), `MinimapArea` record struct (WorldX/Y/W/H, RGBA). Three public entry points: `RenderSolarSystemMinimap`, `RenderPlanetSurfaceMinimap`, `RenderInteriorMinimap`.

**Scene Renderers** are static helper classes that handle non-entity rendering (panels, background elements):
- **SolarSystemRenderer** — background stars (parallax), orbit lines, interaction panels (planet/moon/station)
- **ProjectileRenderer** — projectile trail rendering (colored elongated lines), floating damage numbers (blue=shield, yellow=hull), expanding explosion circles with particle sparks
- **SurfaceEnemyRenderer** — procedural fauna (4-legged creature) and bandit (humanoid) sprites with health bars overhead
- **SurfaceRockRenderer** — mineable rock rendering on planet surfaces (body, highlight, resource vein, health bar)
- **PlanetSurfaceRenderer** — terrain details, settlement markers
- **InteriorRenderer** — tiles, room labels, NPCs, interactable markers
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

| Ship            | Slots                                     | Size | Weight | Base Hull | Base Fuel | Base Cargo | Cost | Sell |
| --------------- | ----------------------------------------- | ---- | ------ | --------- | --------- | ---------- | ---- | ---- |
| Scout (starter) | 4 (Engine, Shield, FTL, Utility)          | 32px | 1.0x   | 80        | 80        | 40         | Free | 200  |
| Fighter         | 5 (Engine, Armor, Shield, Weapon×2)       | 32px | 1.1x   | 120       | 60        | 30         | 1500 | 750  |
| Freighter       | 6 (Engine, Armor, FTL, Utility×2, Weapon) | 48px | 1.4x   | 200       | 160       | 120        | 3000 | 1500 |
| Explorer        | 7 (All slots)                             | 40px | 1.2x   | 150       | 140       | 80         | 5000 | 2500 |

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
| Stat           | Affected By | Gameplay Effect                                                      |
| -------------- | ----------- | -------------------------------------------------------------------- |
| Acceleration   | Engine      | Ship thrust in SolarSystemState                                      |
| MaxSpeed       | Engine      | Ship speed cap in SolarSystemState                                   |
| RotationSpeed  | Engine      | Ship turning rate in SolarSystemState                                |
| MaxHull        | Armor       | Hull capacity (health pool in combat)                                |
| MaxFuel        | Utility     | Fuel tank size, extends range                                        |
| FtlRange       | FTL Drive   | Maximum FTL jump distance in GalaxyMapOverlay                        |
| ShieldStrength | Shield      | Shield HP pool — absorbs damage before hull, regenerates after delay |
| WeaponDamage   | Weapon 1/2  | Projectile damage (also used as mining DPS)                          |
| FuelEfficiency | Utility     | Reduces fuel consumption per jump                                    |
| CargoCapacity  | Utility     | Bonus cargo capacity for mined resources                             |

**Ownership model**: Once a part is purchased, the player owns it permanently. Owned parts are stored in `PlayerData.OwnedParts` (inventory). Swapping between owned parts is free — the old part returns to inventory. Players can sell owned (unequipped) parts manually for their sell value.

**How combined stats work**: `PlayerData.GetCombinedStats()` sums the stats of all equipped parts, then divides acceleration/maxSpeed by the ship type's weight factor. SolarSystemState reads acceleration/maxSpeed/rotationSpeed each frame. GalaxyMapOverlay reads FTL range for jump distance and range circle. `TrySpendFuel()` applies fuel efficiency.

**UI**: Two-column overlay — left column lists equipped slots, right column shows available parts for the selected slot. Parts show status tags: [EQUIPPED], [OWNED] (free to equip), or a credit cost (must buy). Stat comparison shown for selected parts (green = better, red = worse). Press Enter to equip/buy, X to sell owned parts.

### Avatar Customization
Players can equip and swap avatar gear at **Avatar Customization** terminals in station interiors via the **AvatarCustomizationOverlay**. The system is defined in `AvatarParts.cs` and integrated into `PlayerData`.

**Equipment Slots** (4 total): Suit, Helmet, Boots, Weapon.

**Part Tiers**: Each slot has 3 tiers (Tier 1 = starter, Tier 3 = best). Parts are defined in `AvatarPartCatalog`.

**Stats affected by parts** (`AvatarPartStats`):
| Stat           | Affected By | Gameplay Effect                                             |
| -------------- | ----------- | ----------------------------------------------------------- |
| WalkSpeed      | Suit        | Bonus to base avatar movement speed (200 + WalkSpeed)       |
| OxygenCapacity | Helmet      | Oxygen tank capacity (future hazardous environments)        |
| TerrainPenalty | Boots       | Terrain movement penalty reduction (future terrain effects) |
| WeaponDamage   | Weapon      | Bonus projectile damage on planet surface (base 10 + bonus) |
| Armor          | Suit        | Bonus to avatar max health (base 100 + armor)               |

**Ownership model**: Same as ship parts — buy once, own permanently, swap free, sell manually. Stored in `PlayerData.OwnedAvatarParts`. Combined stats via `PlayerData.GetCombinedAvatarStats()`.

**Dynamic stat application**: Both `PlanetSurfaceSimulation` and `InteriorSimulation` compute avatar speed as `BaseAvatarSpeed (200) + CombinedAvatarStats.WalkSpeed` when creating the player avatar entity.

### Vehicle Customization
Players can equip and swap vehicle parts at **Vehicle Customization** terminals in station interiors via the **VehicleCustomizationOverlay**. The system is defined in `VehicleParts.cs` and integrated into `PlayerData`.

**Equipment Slots** (3 total): Engine, Chassis, Lights.

**Part Tiers**: Each slot has 3 tiers (Tier 1 = starter, Tier 3 = best). Parts are defined in `VehiclePartCatalog`.

**Stats affected by parts** (`VehiclePartStats`):
| Stat          | Affected By | Gameplay Effect                                          |
| ------------- | ----------- | -------------------------------------------------------- |
| Acceleration  | Engine      | Vehicle thrust acceleration on planet surface            |
| MaxSpeed      | Engine      | Vehicle top speed on planet surface                      |
| RotationSpeed | Chassis     | Vehicle turning rate                                     |
| Friction      | Chassis     | Added to base friction (affects handling/grip)           |
| Visibility    | Lights      | Light range on planet surface (future visibility system) |

**Ownership model**: Same as ship/avatar parts. Stored in `PlayerData.OwnedVehicleParts`. Combined stats via `PlayerData.GetCombinedVehicleStats()`.

**Dynamic stat application**: When mounting the vehicle in `PlanetSurfaceSimulation`, the `VehicleMovementSystem` is created with stats from `GetCombinedVehicleStats()` (acceleration, maxSpeed, rotationSpeed, friction). Falls back to `GameConfig` defaults if a stat is zero.

### Asteroid Mining
Players can mine asteroids in the solar system view by holding **Space** near an asteroid belt. The mining laser beam originates from the ship and targets the nearest asteroid within range (120 world pixels). Mining DPS equals the ship's combined `WeaponDamage` stat — weapons are dual-use for both combat and mining.

**Resource Types** (defined in `MiningResources.cs`):
| Resource | Value/Unit | Rarity         | Color        |
| -------- | ---------- | -------------- | ------------ |
| Iron     | 5          | Common (30%)   | Brown        |
| Nickel   | 8          | Common (25%)   | Gray         |
| Ice      | 3          | Common (15%)   | Light Blue   |
| Gold     | 20         | Uncommon (15%) | Gold         |
| Platinum | 35         | Rare (10%)     | Silver-White |
| Crystal  | 50         | Rare (5%)      | Cyan         |

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

### Mission System
Players can accept, track, and complete missions from mission boards at space stations and settlements. The system supports 5 mission types with deterministic generation, automatic progress tracking, and credit rewards.

**Mission Types**:
| Type        | Objective                                   | Completion Trigger                               | Reward Range     |
| ----------- | ------------------------------------------- | ------------------------------------------------ | ---------------- |
| Delivery    | Dock at a target station in another system  | `Missions.NotifyStationDocked(targetSystem)`     | 300–1500 credits |
| Mining      | Mine X units of a specific resource         | `Missions.NotifyResourceMined(resource, amount)` | 200–800 credits  |
| Bounty Hunt | Destroy X pirate ships                      | `Missions.NotifyPirateKilled()`                  | 500–2000 credits |
| Exploration | Land on a specific planet in another system | `Missions.NotifyPlanetLanded(system, planet)`    | 300–1000 credits |
| Patrol      | Travel to a specific star system            | `Missions.NotifySystemEntered(system)`           | 200–600 credits  |

**Mission Generation** (`MissionGenerator`):
- Each mission board (station or settlement) generates 5 candidate missions using a deterministic seed derived from the board's location
- Board seed for stations: `SeedManager.GetStarSystemRandom(systemIndex)` combined with station index
- Board seed for settlements: derived from planet surface seed + settlement position hash
- Missions reference real systems/planets from the galaxy data
- Weighted random type selection: Delivery 25%, Mining 20%, Bounty 20%, Exploration 20%, Patrol 15%
- Rewards scale with distance to the target system (minimum 1 system away)

**Mission Lifecycle**:
1. Visit a mission board → see available missions (filtered by already-claimed IDs)
2. Accept a mission (up to 3 active at a time)
3. Progress is tracked automatically via `MissionTracker.Notify*` methods called from game states
4. When all objectives are met, mission status changes to `Completed`
5. Visit any mission board → turn in completed missions for credit rewards
6. Missions can be abandoned at any time (no penalty)

**Integration Points**:
- `SolarSystemSimulation.Create()` → `Missions.NotifySystemEntered()` (Patrol missions)
- `SpaceStationOverlay.Open()` → `Missions.NotifyStationDocked()` (Delivery missions)
- `PlanetSurfaceSimulation.Create()` → `Missions.NotifyPlanetLanded()` (Exploration missions)
- `SolarSystemSimulation` combat handlers → `Missions.NotifyPirateKilled()` (Bounty missions)
- `SolarSystemSimulation` + `PlanetSurfaceSimulation` → `Missions.NotifyResourceMined()` (Mining missions)

**HUD Mission Tracker**: The most urgent active mission is displayed in the top-left HUD area (below health bars) with a colored type badge, title, progress text, and completion indicator. If the player has multiple active missions, a "+N MORE" count is shown.

**Mission Target Markers**:
- **Galaxy Map**: Star systems that are targets of active missions display a pulsing colored ring and diamond icon using the mission's type color. When a target system is selected, the info panel shows the mission type and title. Markers are drawn before the player location marker so they don't obscure it.
- **Solar System**: When the player is in a system that contains mission targets, pulsing rings and type labels appear around the relevant entities — stations for Delivery missions and specific planets for Exploration missions. The markers use the mission's type color with a smooth pulsing alpha animation.

**Data Model** (`Missions.cs`):
- `MissionType` enum: Delivery, Mining, BountyHunt, Exploration, Patrol
- `MissionStatus` enum: Available, Active, Completed
- `Mission` class: Id, Title, Description, Type, Status, target info (system/planet/resource), progress (CurrentAmount/RequiredAmount), CreditReward, origin info

**PlayerData Mission & Navigation Sub-objects**:
- `PlayerData.Missions` (`MissionTracker`) — manages active missions, tracking, and notification callbacks
  - `Active` — list of accepted missions (max 3)
  - `ClaimedIds` — HashSet of all accepted/completed mission IDs (prevents re-offering)
  - `Completed` — lifetime counter
- `PlayerData.Navigation` (`NavigationTarget`) — manages the player's current navigation target
  - `Type`, `PlanetIndex`, `MoonIndex`, `SpaceStationIndex`, `Name`, `Color`, `WorldX`, `WorldY`
  - Methods: `SetStar()`, `SetPlanet()`, `SetMoon()`, `SetStation()`, `SetSurface()`, `Clear()`

### Customization Terminals in Interiors
Both station docking bays and settlement landing pads contain customization terminals:

**Station docking bays** — five terminals near the landing pad:
| Terminal              | Color (world)      | Color (minimap) | InteractableType     |
| --------------------- | ------------------ | --------------- | -------------------- |
| Exit Door             | Green              | Green           | ExitDoor             |
| Ship Customization    | Cyan (100,220,255) | Cyan            | ShipCustomization    |
| Ship Dealer           | Gold (255,200,80)  | Gold            | ShipDealer           |
| Avatar Customization  | Cyan (0,200,200)   | Cyan            | AvatarCustomization  |
| Vehicle Customization | Orange (200,120,0) | Orange          | VehicleCustomization |

**Settlement landing pads** — same terminal types plus Ship Dealer: Ship Customization and Ship Dealer near the exit, Avatar and Vehicle Customization at the top of the pad.

All customization types and the ship dealer are also accessible from the **SpaceStationOverlay** menu (without walking to a terminal).

A built-in `MiniBitmapFont` renders text without requiring TTF files. All HUD panels use semi-transparent dark backgrounds for readability.

### Combat System
Real-time projectile combat in the solar system. Players fire weapons with Space (when not mining), and NPC ships behave according to their faction AI.

**Factions**:
| Faction    | Behavior                                                          | Color |
| ---------- | ----------------------------------------------------------------- | ----- |
| **Player** | Controlled by input, fires green projectiles                      | Green |
| **Pirate** | Patrol → detect player/trader → chase → attack → flee when low HP | Red   |
| **Trader** | Cruise through system, flee from nearby pirates                   | Gold  |
| **Patrol** | Hunt pirates, defend traders, strong shields                      | Blue  |

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

**Friendly Fire Rules**: Same-faction projectiles never hit each other. Patrol/trader projectiles don't hit the player. Only pirate projectiles can hit the player, traders, and patrols. On planet surfaces, fauna and bandit projectiles don't hit each other but do hit the player. All friendly-fire logic is centralized in `Core/FactionRules.cs` (`FactionRules.CanHit(attacker, target)`) and shared between `ProjectileSystem` and `AvatarEnemyAISystem`.

### Surface Combat
Real-time projectile combat on planet surfaces. Players shoot with Space (fires in last movement direction) or left mouse button (fires toward cursor). Hostile fauna and bandits spawn on walkable terrain during planet surface generation.

**Surface Factions**:
| Faction    | Behavior                                                          | Color               |
| ---------- | ----------------------------------------------------------------- | ------------------- |
| **Fauna**  | Wander → detect player → chase → melee-range bite attack          | Red (180,60,60)     |
| **Bandit** | Patrol → detect player → chase → ranged fire → flee when critical | Orange (200,100,60) |

**Spawning**: Fauna (3–10 per planet) and bandits (0–4, only on planets with settlements) are placed on walkable terrain at least 8 tiles from the landing zone and 4 tiles from settlements. Counts and positions are seeded deterministically. Ocean planets get fewer fauna.

**Avatar Weapon Tiers**:
| Weapon        | Tier | Cost | Bonus Damage |
| ------------- | ---- | ---- | ------------ |
| Sidearm       | T1   | Free | +0           |
| Pulse Rifle   | T2   | 300  | +8           |
| Plasma Cannon | T3   | 700  | +20          |

Base avatar weapon damage is 10. Total damage = base + equipped weapon's `WeaponDamage` bonus.

**Avatar Health**: Persistent across planet visits. Base 100 HP + `Armor` stat from equipped avatar suit. Stored in `PlayerData.AvatarHealth` / `AvatarMaxHealth`. Health is synced from the ECS `Health` component back to `PlayerData` each frame and persisted when the player is removed from the simulation (via `DestroyPlayerEntity` hook).

**Surface Loot Drops**: Destroyed enemies drop credits (fauna: 10–40, bandits: 20–80) with chances for resource drops (30–40%). Bandits have a small chance (5%) to drop equipment parts.

**Avatar Death**: When HP reaches zero:
- 2.5-second death screen with "YOU DIED" and "RESPAWNING..."
- Lose 10% of credits
- Respawn near the landed ship at full avatar health (vehicle auto-stowed)
- All input except menu-back is blocked while dead

**Surface Combat HUD**: Avatar HP bar at bottom-left, floating damage numbers, explosion effects, combat loot messages. Enemy health bars above each enemy. Enemy dots on the minimap (red = fauna, orange = bandits).

### Audio System
Fully procedural audio engine using SDL3's built-in audio API (push-based streaming at 44100 Hz, stereo float32). No external audio files — all music and sound effects are synthesized at runtime, matching the game's procedural generation philosophy.

**Architecture**: `Game` accesses audio via `IPlatform.AudioManager` (`IAudioManager`). The concrete implementation is `SdlAudioManager` (`Platform/Sdl/SdlAudioManager.cs`). States call `SetMusicTheme()` and `PlaySfx()` through this interface.

**`IAudioManager` / `SdlAudioManager`** (`Platform/Sdl/SdlAudioManager.cs`):
- Opens an SDL3 audio device stream via `SDL.OpenAudioDeviceStream` (44100 Hz, float32 LE, stereo)
- `Update(float dt)` generates and pushes mixed audio chunks (~2048 frames / ~46ms) to the device, keeping ~0.2s buffered via `SDL.GetAudioStreamAvailable`
- `SetMusicTheme(theme, instant)` with smooth crossfade (fade out → switch → fade in, 2 vol units/s)
- `PlaySfx(type, volume, pan)` with constant-power stereo panning, up to 16 simultaneous voices
- Master / Music / SFX volume controls from `GameConfig`

**MusicGenerator** (`Audio/MusicGenerator.cs`):
- Real-time additive synthesis with 6 concurrent layers:
  - **Drone**: 2 detuned sine oscillators + sub-octave
  - **Pad**: 3-note chord with stereo spread
  - **Arpeggio**: Triangle wave oscillator with pattern sequencing
  - **Bass**: Sine wave at chord root / 2
  - **Atmosphere**: Stereo filtered noise
  - **Reverb**: Ping-pong delay line
- Pentatonic minor scale `[0, 3, 5, 7, 10]` with 4 chord voicings and smooth portamento (~2s glide)
- Deterministic noise via xorshift PRNG (thread-safe, no locking)

**Music Themes** (`MusicTheme` enum):
| Theme         | Root   | BPM | Character                                |
| ------------- | ------ | --- | ---------------------------------------- |
| MainMenu      | 110 Hz | 60  | Warm drone, gentle pad, slow arp         |
| SolarSystem   | 82 Hz  | 70  | Deep space ambience, moderate arp        |
| PlanetSurface | 130 Hz | 75  | Higher drone, livelier arp               |
| Interior      | 164 Hz | 55  | Quiet, minimal — mostly pad + atmosphere |
| FTL           | 73 Hz  | 140 | Intense driving arp, ascending pattern   |
| Combat        | 98 Hz  | 120 | Heavy bass, aggressive arp, high reverb  |

**SfxGenerator** (`Audio/SfxGenerator.cs`):
- Pre-generates all SFX as mono float arrays at startup
- Synthesis techniques: frequency sweeps, filtered noise bursts, sine thumps, ADSR envelopes, single-pole low-pass filter

**SFX Types** (`SfxType` enum — 15 types):
| SFX            | Technique                  | Duration | Triggered By                        |
| -------------- | -------------------------- | -------- | ----------------------------------- |
| LaserFire      | Descending sine sweep      | ~0.15s   | Player fires weapon (space/surface) |
| EnemyLaser     | Higher ascending sweep     | ~0.12s   | (reserved for enemy fire)           |
| Explosion      | Low thump + filtered noise | ~0.6s    | Enemy ship / enemy destroyed        |
| SmallExplosion | Shorter thump + noise      | ~0.3s    | Asteroid / rock destroyed           |
| ShieldHit      | Brief high-freq ping       | ~0.1s    | Player shields absorb damage        |
| HullDamage     | Mid thump                  | ~0.15s   | Player hull takes damage            |
| MenuSelect     | Quick ascending blip       | ~0.08s   | Menu option confirmed               |
| MenuNavigate   | Soft tick                  | ~0.04s   | (reserved for menu navigation)      |
| FtlCharge      | Rising sine sweep          | ~0.8s    | FTL charge phase begins             |
| FtlJump        | Deep descending sweep      | ~0.5s    | FTL jump fires                      |
| PickupCredits  | Ascending multi-blip       | ~0.2s    | Credits awarded from loot           |
| PickupItem     | Lower ascending blip       | ~0.15s   | Resource/item picked up             |
| MiningHit      | Noise burst                | ~0.1s    | (reserved for mining impact)        |
| Landing        | Descending rumble          | ~0.5s    | Ship lands on planet                |
| Takeoff        | Ascending rumble           | ~0.6s    | Ship takes off from planet          |

**Combat Music Tracking**: `SolarSystemSimulation` and `PlanetSurfaceSimulation` (via `CombatSimulationBase`) maintain a `CombatMusicTimer` that resets on each damage event. The corresponding states read this timer to switch music themes — when it exceeds `GameConfig.CombatMusicDelay` (5s), the music fades back from Combat to the state's default theme.

**Audio Config** (`GameConfig`):
- `AudioMasterVolume = 0.5f` — overall output level
- `AudioMusicVolume = 0.4f` — music layer level
- `AudioSfxVolume = 0.7f` — SFX layer level
- `CombatMusicDelay = 5f` — seconds before combat music disengages

### Camera
The `Camera` class handles world-to-screen coordinate conversion with zoom support. Scrollable tilemaps render only visible tiles using `GetVisibleBounds()`.

## Controls

### Main Menu
- Up/Down or W/S: Navigate options
- Enter/E: Select option
- Mouse: Hover to highlight, click to select

### Map Overlay (M key — SolarSystemState)
- M/Tab: Toggle between Solar System and Galaxy tabs
- WASD/Arrows/Mouse Drag: Pan camera
- Mouse Scroll: Zoom
- Click: Select object (planet/station/star system)
- Double-Click/Enter: Set navigation target / Travel to system (Galaxy tab)
- Escape: Close overlay

### Solar System
- W/Up: Thrust forward
- A/D or Left/Right: Rotate ship
- S/Down: Brake
- Mouse Scroll: Zoom
- Space (hold): Fire weapons / mine nearest asteroid (mining takes priority when near an asteroid)
- E: Interact (open landing overlay for planets / dock at station)
- M: Open galaxy map overlay
- Escape: Open in-game menu (Resume / Map / Missions / Cargo / Controls / Main Menu)

### Planet Landing (Overlay)
- Click: Select landing site
- WASD/Arrows: Nudge cursor
- Mouse Scroll: Zoom map
- Left-Drag: Pan map
- Enter/E: Confirm landing
- Escape: Cancel (return to solar system)

### Space Station (Overlay)
- Up/Down or W/S: Navigate menu
- Enter: Confirm selection
- Escape: Exit station / close sub-overlay

### Planet Surface
- WASD/Arrows: Move avatar / drive vehicle
- Mouse Scroll: Zoom
- Space (hold): Fire weapon (in movement direction)
- Left Mouse (hold): Fire weapon (toward cursor)
- E: Board ship (when near, on foot) / Enter settlement (when near, on foot) / Mount/dismount vehicle
- M: Open surface map overlay (terrain overview with settlements, ship, player markers)
- Escape: Open in-game menu (Resume / Map / Missions / Cargo / Controls / Main Menu)

### Surface Map (M key — PlanetSurfaceState)
- WASD/Arrows/Mouse Drag: Pan camera
- Mouse Scroll: Zoom
- Click: Select settlement or location
- Double-Click: Set navigation target
- M/Escape: Close overlay

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

## Current Status
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
- [x] Orbital landing site selection overlay (planet overview map with terrain, settlements, cursor)
- [x] Procedural pixel art textures (ships, planets, stars, stations, avatar, asteroids)
- [x] HUD backgrounds for readability
- [x] Terrain tile variation with detail sprites
- [x] Walkable interiors for stations and settlements (rooms, NPCs, trade, repair, missions)
- [x] Player vehicle for planet surface exploration (mount/dismount, 3x speed)
- [x] Ship customization system (dynamic equipment slots per ship type, 3 tiers, dynamic ship stats)
- [x] Multiple ship types (Scout, Fighter, Freighter, Explorer) with ship dealer UI
- [x] Avatar customization system (4 equipment slots, 3 tiers, dynamic walk speed)
- [x] Vehicle customization system (3 equipment slots, 3 tiers, dynamic vehicle physics)
- [x] Customization terminals in station interiors (ship, ship dealer, avatar, vehicle, cargo)
- [x] Asteroid mining (mining laser beam, named resources, cargo system, sell at stations/terminals)
- [x] Space combat system (projectile weapons, shield/hull mechanics, enemy AI)
- [x] NPC factions (pirates, traders, patrols) with faction-specific AI behaviors
- [x] Per-system danger levels with galaxy map display
- [x] Loot drops (credits, resources, equipment parts scaled by danger)
- [x] Player death and respawn with penalties
- [x] Entity renderer architecture (Avatar, Vehicle, Spaceship, EnemyShip, Station, Asteroid, Planet, Star renderers own their textures)
- [x] Simulation architecture (SimulationBase/CombatSimulationBase hierarchy, SimulationCoordinator with 90s timeout, state↔simulation separation)
- [x] Scene renderer extraction (SolarSystemRenderer, ProjectileRenderer, PlanetSurfaceRenderer, InteriorRenderer, SettlementRenderer)
- [x] Unified HUD renderer (HudRenderer: location info, stats, health bars, prompts, offscreen indicators across all states)
- [x] Unified minimap renderer (HudMinimapRenderer: data-driven markers/areas, player-centered scrolling, settlement/room areas)
- [x] Offscreen edge indicators (NPC ships, star, settlements with distance labels)
- [x] Planet surface combat (hostile fauna, hostile bandits, avatar weapons, persistent health, in-place respawn near ship)
- [x] In-game menu overlay (Resume / Map / Missions / Cargo / Controls / Main Menu) for SolarSystem and PlanetSurface states
- [x] Main menu with 7 start options (including Inside Station and Inside Settlement)
- [x] Main menu overlay (MenuPanelOverlayBase extraction)
- [x] Anchor system for ship tracking during overlays
- [x] Centralized entity creation via EntityFactory
- [x] Map overlay framework (MapOverlayBase + MapPanelBase + PlanetMapPanelBase base classes)
- [x] Dual-tab map overlay (Solar System + Galaxy tabs with SolarSystemMapPanel and GalaxyMapPanel)
- [x] Planet surface map overlay (PlanetSurfaceMapOverlay with settlement/ship/vehicle markers)
- [x] Mission system (5 types: Delivery, Mining, BountyHunt, Exploration, Patrol; deterministic generation, accept/track/complete/abandon, HUD tracker)
- [x] FTL travel animation (hyperspace tunnel with star streaks, charge-up → tunnel → exit flash)
- [x] Orbital/surface cinematic transition state (landing and takeoff)
- [x] Procedural audio engine (SDL3 built-in audio, 44100 Hz stereo float32 push-streaming)
- [x] Procedural ambient music (6 layers: drone, pad, arp, bass, atmosphere, reverb; 7 themes with crossfade)
- [x] Procedural sound effects (15 SFX types: weapons, explosions, shields, FTL, pickups, landing/takeoff, menus)
- [x] Combat music tracking (auto-switch to combat theme on damage, fade back after 5s)
- [x] Sound effects and music (procedural synthesis via SDL3 built-in audio)
- [x] Main menu debug overlay and showcase launchers
- [x] Platform abstraction layer (IPlatform / ISpriteRenderer / ITextureManager / IInputManager / IAudioManager + SDL3 implementations)
- [x] Universe generation interface (IUniverseGenerator / ProceduralUniverseGenerator) with showcase subclass overrides
- [x] Centralized faction rules (FactionRules.CanHit)
- [x] Centralized surface terrain rules (SurfaceTerrainRules)
- [x] Menu option persistence (MenuOptionsPersistence — saves danger/location/sublocation selections to disk)
- [x] Debug timing infrastructure (DebugTimer / DebugTimingEntry / IDebugInfoProvider)

## TODO / Next Steps
- [ ] Save/load game
