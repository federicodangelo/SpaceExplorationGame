# Space Exploration Game

[![Build](https://github.com/federicodangelo/SpaceExplorationGame/actions/workflows/build.yml/badge.svg)](https://github.com/federicodangelo/SpaceExplorationGame/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/federicodangelo/SpaceExplorationGame)](https://github.com/federicodangelo/SpaceExplorationGame/releases/latest)

<p align="center">
  <a href="https://federicodangelo.github.io/SpaceExplorationGame/">
    <img src="https://img.shields.io/badge/%E2%96%B6%20PLAY%20IN%20BROWSER-federicodangelo.github.io%2FSpaceExplorationGame-brightgreen?style=for-the-badge" alt="Play in Browser" />
  </a>
</p>

A 2D procedural space exploration game built entirely using AI coding agents — an experiment to test the limits of AI-assisted game development.

## About This Project

This project was created as an experiment to push the boundaries of what's possible when using **AI coding agents** for game development. The entire codebase — rendering, procedural generation, ECS architecture, audio synthesis, UI, and gameplay — was written by [Claude](https://www.anthropic.com/claude) (Opus 4.6 and Sonnet 4.6) through iterative prompting.

### v1.1.0 (current)
- **AI Models:** Claude Opus 4.6 and Claude Sonnet 4.6
- **Total Development Time:** ~8 days of on-and-off work
- **Lines of Code:** ~28,000 (all AI-generated)
- *(Cost tracking discontinued)*

### v1.0.0
- **AI Model:** Claude Opus 4.6
- **Total Cost:** ~$50 USD in API/token usage
- **Development Time:** ~4 days of on-and-off work
- **Lines of Code:** ~18,000 (all AI-generated)

## Features

- **Procedural Galaxy** — Seed-based deterministic generation of star systems, planets, moons, asteroid belts, and space stations
- **Multiple Scales of Play** — Fly your ship through solar systems, land on planets, explore surfaces on foot or by vehicle, enter stations and settlements
- **Ship, Vehicle & Avatar Customization** — Upgradeable equipment slots for weapons, shields, engines, armor, and more
- **Combat** — Space combat against pirates, traders, and patrols; surface combat against fauna and bandits
- **Procedural Audio** — Fully synthesized music and sound effects at runtime (no audio files)
- **Procedural Pixel Art** — All textures generated at runtime (sphere-shaded planets, glow stars, pixel-art sprites)
- **Mission System** — Accept and complete missions from station/settlement boards
- **Mining** — Mine asteroids and surface rocks for resources
- **Galaxy & System Maps** — Navigate between star systems via FTL jumps

## Tech Stack

- **Language:** C# / .NET 10
- **Rendering:** [SDL3](https://www.libsdl.org/) via [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS)
- **ECS:** [Arch ECS](https://github.com/genaray/Arch) with Arch.System extensions
- **Audio:** SDL3 built-in audio API with fully procedural synthesis (no external audio files or SDL_mixer)

## Screenshots (v1.1.0)

<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030347.png" width="400" alt="Screenshot 1" />
  <img src="media/v1.1.0/screenshot_20260302_030411.png" width="400" alt="Screenshot 2" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030425.png" width="400" alt="Screenshot 3" />
  <img src="media/v1.1.0/screenshot_20260302_030437.png" width="400" alt="Screenshot 4" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030443.png" width="400" alt="Screenshot 5" />
  <img src="media/v1.1.0/screenshot_20260302_030455.png" width="400" alt="Screenshot 6" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030500.png" width="400" alt="Screenshot 7" />
  <img src="media/v1.1.0/screenshot_20260302_030546.png" width="400" alt="Screenshot 8" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030553.png" width="400" alt="Screenshot 9" />
  <img src="media/v1.1.0/screenshot_20260302_030558.png" width="400" alt="Screenshot 10" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030603.png" width="400" alt="Screenshot 11" />
  <img src="media/v1.1.0/screenshot_20260302_030614.png" width="400" alt="Screenshot 12" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030619.png" width="400" alt="Screenshot 13" />
  <img src="media/v1.1.0/screenshot_20260302_030632.png" width="400" alt="Screenshot 14" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030639.png" width="400" alt="Screenshot 15" />
  <img src="media/v1.1.0/screenshot_20260302_030647.png" width="400" alt="Screenshot 16" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030654.png" width="400" alt="Screenshot 17" />
  <img src="media/v1.1.0/screenshot_20260302_030729.png" width="400" alt="Screenshot 18" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030736.png" width="400" alt="Screenshot 19" />
  <img src="media/v1.1.0/screenshot_20260302_030740.png" width="400" alt="Screenshot 20" />
</p>
<p align="center">
  <img src="media/v1.1.0/screenshot_20260302_030806.png" width="400" alt="Screenshot 21" />
  <img src="media/v1.1.0/screenshot_20260302_030828.png" width="400" alt="Screenshot 22" />
</p>

## Screenshots (v1.0.0)

See [docs/SCREENSHOTS_V1.0.0.md](docs/SCREENSHOTS_V1.0.0.md) (includes video demo).

## Building & Running

```bash
# Requires .NET 10 SDK
dotnet run --project SpaceExplorationGame
```

### Command line options

```bash
dotnet run --project SpaceExplorationGame -- [--seed|-s <seed>] [--location|-l <location> [--sublocation|-sl <sublocation>]]
dotnet run --project SpaceExplorationGame -- [--seed|-s <seed>] [--location|-l <location> [--sublocation|-sl <sublocation>]] [--showcase|-sc <showcase> [--star-type <type>]]
```

| Argument                                           | Description                                                                                   |
| -------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| `--help`, `-h`, `/?`                               | Show CLI usage help and exit.                                                                 |
| `--seed <seed>`, `-s <seed>`                       | Optional explicit seed for deterministic world generation. If omitted, a random seed is used. |
| `--location <location>`, `-l <location>`           | Target top-level start location (`system`, `station`, `planet`, `settlement`).                |
| `--sublocation <sublocation>`, `-sl <sublocation>` | Target sub-location for the selected location (see matrix below).                             |
| `--showcase <showcase>`, `-sc <showcase>`          | Launch a debug showcase directly (`star-type`, `planet-type`, `asteroid`, `surface-mining`).  |
| `--star-type <type>`                               | Optional star class override for `--showcase star-type` (default: `G`).                       |

**Location / sub-location matrix**

| `--location` | `--sublocation` values                     |
| ------------ | ------------------------------------------ |
| `system`     | *(omit or use `none`)*                     |
| `station`    | `orbit`, `docked`, `inside`                |
| `planet`     | `orbit`, `landed`, `on-foot`, `on-vehicle` |
| `settlement` | `above`, `inside`, `on-foot`, `on-vehicle` |

**Examples**

```bash
dotnet run --project SpaceExplorationGame
dotnet run --project SpaceExplorationGame -- --help
dotnet run --project SpaceExplorationGame -- --seed 12345
dotnet run --project SpaceExplorationGame -- -s 12345
dotnet run --project SpaceExplorationGame -- --location system
dotnet run --project SpaceExplorationGame -- -l station -sl docked
dotnet run --project SpaceExplorationGame -- --location station --sublocation docked
dotnet run --project SpaceExplorationGame -- --seed 42 --location planet --sublocation on-foot
dotnet run --project SpaceExplorationGame -- --location settlement --sublocation on-vehicle
dotnet run --project SpaceExplorationGame -- --showcase planet-type
dotnet run --project SpaceExplorationGame -- --showcase star-type --star-type K
```

## Code Formatting

```bash
# Check and apply formatting locally
dotnet format SpaceExplorationGame/SpaceExplorationGame.csproj whitespace

# CI-style check (fails if formatting changes are needed)
dotnet format SpaceExplorationGame/SpaceExplorationGame.csproj whitespace --verify-no-changes
```

### Pre-commit hook

```bash
# Enable repository hooks (one-time per clone)
git config core.hooksPath .githooks
```

The `pre-commit` hook validates formatting before each commit using the same whitespace check as CI.

## Project Structure

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for a detailed breakdown of the codebase architecture, ECS components, rendering pipeline, and game state management.

## License

This project is provided as-is for educational and experimental purposes.
