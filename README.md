# Space Exploration Game

[![Build](https://github.com/federicodangelo/SpaceExplorationGame/actions/workflows/build.yml/badge.svg)](https://github.com/federicodangelo/SpaceExplorationGame/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/federicodangelo/SpaceExplorationGame)](https://github.com/federicodangelo/SpaceExplorationGame/releases/latest)

A 2D procedural space exploration game built entirely using AI coding agents — an experiment to test the limits of AI-assisted game development.

## About This Project

This project was created as an experiment to push the boundaries of what's possible when using **AI coding agents** for game development. The entire codebase — rendering, procedural generation, ECS architecture, audio synthesis, UI, and gameplay — was written by [Claude Opus 4.6](https://www.anthropic.com/claude) through iterative prompting.

Initial version (v1.0.0 — information below doest not include post-v1.0.0 changes):
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

## Screenshots

<p align="center">
  <img src="media/Screenshot%202026-02-17%20215012.png" width="400" alt="Screenshot 1" />
  <img src="media/Screenshot%202026-02-17%20215031.png" width="400" alt="Screenshot 2" />
</p>
<p align="center">
  <img src="media/Screenshot%202026-02-17%20215045.png" width="400" alt="Screenshot 3" />
  <img src="media/Screenshot%202026-02-17%20215054.png" width="400" alt="Screenshot 4" />
</p>
<p align="center">
  <img src="media/Screenshot%202026-02-17%20215109.png" width="400" alt="Screenshot 5" />
  <img src="media/Screenshot%202026-02-17%20215123.png" width="400" alt="Screenshot 6" />
</p>
<p align="center">
  <img src="media/Screenshot%202026-02-17%20215133.png" width="400" alt="Screenshot 7" />
  <img src="media/Screenshot%202026-02-17%20215148.png" width="400" alt="Screenshot 8" />
</p>
<p align="center">
  <img src="media/Screenshot%202026-02-17%20215158.png" width="400" alt="Screenshot 9" />
  <img src="media/Screenshot%202026-02-17%20215206.png" width="400" alt="Screenshot 10" />
</p>
<p align="center">
  <img src="media/Screenshot%202026-02-17%20215217.png" width="400" alt="Screenshot 11" />
  <img src="media/Screenshot%202026-02-17%20215236.png" width="400" alt="Screenshot 12" />
</p>

## Video Demo

https://github.com/federicodangelo/SpaceExplorationGame/raw/master/media/Recording%202026-02-17%20214814.mp4

> If the video doesn't play inline, [download it here](media/Recording%202026-02-17%20214814.mp4).

## Building & Running

```bash
# Requires .NET 10 SDK
dotnet run --project SpaceExplorationGame
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
