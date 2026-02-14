I want to create a 2D procedural space exploration game.

This is the tech stack:
- Code written in C# using .net 10
- SDL for rendering (using https://github.com/edwardgushchin/SDL3-CS )
- It will be a 100% 2D game, so use tilemaps / tiles for everything.
- Use an entity / component system to build everything.

Guidance:
- Ask me any question that you have before starting to work.
- Pause if you have questions or need further guidance.
- Build the project often to make sure that everything works.
- Pause when you think that you have something that works and is worth testing, so I can make partial commits and provide feedback.
- Write any guidance documentation in files that you need for yourself, so if I have to start a new conversation you will know the state of things.

These are the basic components / loops:

[PROCEDURAL SYSTEM]
Use a user configurable numeric seed (random by default) to build the galaxy and all procedural things. Use deterministic algorithms so the same seed will always produce the same result.
Assign a new seed to each random element (the galaxy should have a seed, each solar system another seed, each planet another seed, and so on). The player will only provide the galaxy seed.

[PLAYER]
1. Player spaceship
- The player will have a spaceship to travel in space
- The spaceship will be composed of a main body and upgradeable slots (weapons, energy source, armor, shields, thrusters, FTL engine, etc..)
- The player will be able to purchase a new ship or ship parts in SPACE STATIONS.

2. Player vehicle
- The player will have a vehicle to travel in planets
- The vehicle will be stored inside the spaceship, and will be upgradeable like the spaceship (weapons, armor, shields, engine, etc..)

3. Player avatar
- The player will have an avatar to walk inside space stations or settlements in planets
- The avatar will also be upgradeable (suit, weapons, etc..)

[SPACE]
Space will be composed of 2 layers:

1. Galaxy: This will be mostly a 2d map with all the available solar systems. The player will use an ingame map to travel between solar systems.

2. Solar system: This will be where there player travels in his spaceship. 
A solar system will be composed of:
- A sun at the center (of all known star types)
- Planets of different kind (use our own solar system for reference of types of planets)
- Moons orbiting those planets
- Asteroid belts / asteroids located at different places (orbiting the sun / planets / moons)
- Space stations oribint different places (sun / planets / moons)

[SPACE STATIONS]
When the player spaceship is located over a spacestation, he can request to land on the space station, at which point the spaceship will land inside the space station and the player will be shown a GUI with all the available options:
- Ship customization: Another menu will be shown with options for buying a new ship, ship parts, selling ship parts, installing parts, etc..)
- Missions: List of available missions (TBD in the future)
- Exit ship: Walk inside the space station (based 2d map for now with some other avatars and classic space station locations)
- Exit space station: Exit the space station in his ship.

[PLANETS]
When the player spaceship is located over a planet that has a solid surface, he can land on it, at which point the player will be shown a list of available landing locations in a 2D map and he can choose where to land.
- If he choose to land on a settlement, he will be shown an UI similar to the one used for space stations
- If he choose to land outside a settlement, the spaceship will landed on the terrain and he can choose to exit the spaceship using either a vehicle o walking.
- The player will be able to board the spaceship again once he is closer to it.

