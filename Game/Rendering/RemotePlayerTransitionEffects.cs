using System.Numerics;
using Arch.Core;
using Engine.Network;
using Engine.Network.Client;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.Simulation.Base;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Manages and renders visual transition effects for remote players entering/leaving the simulation
/// (FTL warp, planet landing/takeoff, station docking/undocking).
/// </summary>
public sealed class RemotePlayerTransitionEffects
{
    private const float FTLJumpDepartureDuration = FTLTransitionState.TotalDuration;
    private const float FTLJumpArrivalDuration = FTLTransitionState.TotalDuration;

    private const float SolarSystemToPlanetDuration = OrbitalSurfaceTransitionState.TotalDuration;
    private const float PlanetToSolarSystemDuration = OrbitalSurfaceTransitionState.TotalDuration;

    private const float SolarSystemToSpaceStationDuration = StationDockingTransitionState.TotalDuration;
    private const float SpaceStationToSolarSystemDuration = StationDockingTransitionState.TotalDuration;

    private readonly List<TransitionEffect> _effects = [];

    public bool IsRemotePlayerTransitionActive(byte playerId)
    {
        return _effects.Any(e => e.PlayerId == playerId);
    }

    private enum TransitionType
    {
        None,
        FTLJumpArrival,
        FTLJumpDeparture,
        SolarSystemToPlanet,
        PlanetToSolarSystem,
        SolarSystemToSpaceStation,
        SpaceStationToSolarSystem,
    }

    private sealed class TransitionEffect
    {
        public byte PlayerId;
        public Vector2 Position;
        public float Rotation;
        public string ShipTypeId = "";
        public TransitionType TransitionType;
        public float Elapsed;
        public float Duration;
    }

    public void Clear()
    {
        _effects.Clear();
    }

    /// <summary>
    /// Collect new departure/arrival effects and tick active ones.
    /// Call once per frame from the game state's Update.
    /// </summary>
    public void Update(float dt, SolarSystemSimulation sim, ClientNetworkManager? net)
    {
        if (net == null)
        {
            TickEffects(dt);
            return;
        }

        var currentLocation = sim.GetNetPlayerLocation();

        foreach (var remote in net.RemotePlayers.Values)
        {
            if (remote.PendingTransition.To.IsUnknown) continue;

            // Find the player's entity in the simulation to read their current position
            var from = remote.PendingTransition.From;
            var to = remote.PendingTransition.To;

            remote.PendingTransition = new NetPlayerTransition
            {
                From = NetPlayerLocation.ForUnknown(),
                To = NetPlayerLocation.ForUnknown()
            };

            // Different solar system => FTL jump 
            // Same system but planet change => landing/takeoff
            // Same system but station change => docking/undocking
            TransitionType transType = from.SolarSystemIndex != to.SolarSystemIndex
                ? currentLocation.SolarSystemIndex == to.SolarSystemIndex
                    ? TransitionType.FTLJumpArrival
                    : TransitionType.FTLJumpDeparture
                : !from.IsOnPlanet && to.IsOnPlanet
                    ? TransitionType.SolarSystemToPlanet
                    : from.IsOnPlanet && !to.IsOnPlanet
                        ? TransitionType.PlanetToSolarSystem
                        : !from.IsOnSpaceStation && to.IsOnSpaceStation
                            ? TransitionType.SolarSystemToSpaceStation
                            : from.IsOnSpaceStation && !to.IsOnSpaceStation
                                ? TransitionType.SpaceStationToSolarSystem
                                : TransitionType.None;

            var (position, rotation) = FindRemotePlayerPositionAndRotation(sim, remote.PlayerId, transType, from);
            var shipTypeId = remote.Info.ShipTypeId;

            float duration = transType switch
            {
                TransitionType.FTLJumpDeparture => FTLJumpDepartureDuration,
                TransitionType.FTLJumpArrival => FTLJumpArrivalDuration,
                TransitionType.SolarSystemToPlanet => SolarSystemToPlanetDuration,
                TransitionType.PlanetToSolarSystem => PlanetToSolarSystemDuration,
                TransitionType.SolarSystemToSpaceStation => SolarSystemToSpaceStationDuration,
                TransitionType.SpaceStationToSolarSystem => SpaceStationToSolarSystemDuration,
                _ => 1.0f,
            };

            _effects.Add(new TransitionEffect
            {
                PlayerId = remote.PlayerId,
                Position = position,
                Rotation = rotation,
                ShipTypeId = shipTypeId,
                TransitionType = transType,
                Elapsed = 0f,
                Duration = duration,
            });
        }

        TickEffects(dt);
    }

    private void TickEffects(float dt)
    {
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            _effects[i].Elapsed += dt;
            if (_effects[i].Elapsed >= _effects[i].Duration)
                _effects.RemoveAt(i);
        }
    }

    /// <summary>
    /// Render all active transition effects.
    /// </summary>
    public void Render(ISpriteRenderer renderer, Camera camera, SpaceshipRenderer spaceshipRenderer)
    {
        foreach (var fx in _effects)
        {
            float t = Math.Clamp(fx.Elapsed / fx.Duration, 0f, 1f);

            switch (fx.TransitionType)
            {
                case TransitionType.FTLJumpArrival:
                    RenderFTLJumpArrival(renderer, camera, spaceshipRenderer, fx, t);
                    break;
                case TransitionType.PlanetToSolarSystem:
                    RenderPlanetToSolarSystem(renderer, camera, spaceshipRenderer, fx, t);
                    break;
                case TransitionType.SpaceStationToSolarSystem:
                    RenderSpaceStationToSolarSystem(renderer, camera, spaceshipRenderer, fx, t);
                    break;
                case TransitionType.FTLJumpDeparture:
                    RenderFTLJumpDeparture(renderer, camera, spaceshipRenderer, fx, t);
                    break;
                case TransitionType.SolarSystemToPlanet:
                    RenderSolarSystemToPlanet(renderer, camera, spaceshipRenderer, fx, t);
                    break;
                case TransitionType.SolarSystemToSpaceStation:
                    RenderSolarSystemToSpaceStation(renderer, camera, spaceshipRenderer, fx, t);
                    break;
            }
        }
    }

    // ── Departure effects ───────────────────────────────────────────

    private static void RenderFTLJumpDeparture(ISpriteRenderer renderer, Camera camera,
        SpaceshipRenderer spaceshipRenderer, TransitionEffect fx, float t)
    {
        var pos = fx.Position;
        float rad = fx.Rotation * MathF.PI / 180f;
        var forward = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        int shipSize = ShipTypeCatalog.GetById(fx.ShipTypeId)?.SpriteSize ?? 32;
        var warpColor = new Color4(120, 180, 255, 255);

        if (t < 0.3f)
        {
            // Phase 1: Ship visible but starting to stretch forward
            float p = t / 0.3f;
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, shipSize);

            // Growing forward streak
            float streakLen = shipSize * (1f + p * 3f);
            var streakEnd = pos + forward * streakLen;
            int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                float st = i / (float)steps;
                var sp = Vector2.Lerp(pos, streakEnd, st);
                float radius = (1f - st) * shipSize * 0.25f * p;
                byte a = (byte)(255 * (1f - p * 0.5f) * (1f - st));
                renderer.DrawFilledCircle(camera, sp, Math.Max(radius, 1f), warpColor.WithAlpha(a));
            }
        }
        else if (t < 0.6f)
        {
            // Phase 2: Bright flash, ship disappearing into streak
            float p = (t - 0.3f) / 0.3f;

            // Flash
            float flashIntensity = MathF.Max(0, 1f - p * 1.5f);
            if (flashIntensity > 0)
            {
                renderer.DrawFilledCircle(camera, pos, shipSize * (0.8f + flashIntensity),
                    new Color4(255, 255, 255, (byte)(200 * flashIntensity)));
                renderer.DrawFilledCircle(camera, pos, shipSize * (0.4f + flashIntensity * 0.3f),
                    warpColor.WithAlpha((byte)(150 * flashIntensity)));
            }

            // Streak shooting forward and fading
            float streakLen = shipSize * (4f + p * 6f);
            var streakEnd = pos + forward * streakLen;
            int steps = 8;
            byte streakAlpha = (byte)(180 * (1f - p));
            for (int i = 0; i <= steps; i++)
            {
                float st = i / (float)steps;
                var sp = Vector2.Lerp(pos, streakEnd, st);
                float radius = (1f - st) * shipSize * 0.2f * (1f - p);
                byte a = (byte)(streakAlpha * (1f - st));
                renderer.DrawFilledCircle(camera, sp, Math.Max(radius, 1f), warpColor.WithAlpha(a));
            }
        }
        else
        {
            // Phase 3: Residual glow fading out
            float p = (t - 0.6f) / 0.4f;
            byte glowAlpha = (byte)(60 * (1f - p));
            if (glowAlpha > 3)
            {
                renderer.DrawFilledCircle(camera, pos, shipSize * 0.3f * (1f - p * 0.5f),
                    warpColor.WithAlpha(glowAlpha));
            }
        }
    }

    private static void RenderSolarSystemToPlanet(ISpriteRenderer renderer, Camera camera,
        SpaceshipRenderer spaceshipRenderer, TransitionEffect fx, float t)
    {
        var pos = fx.Position;
        int shipSize = ShipTypeCatalog.GetById(fx.ShipTypeId)?.SpriteSize ?? 32;
        var landingColor = new Color4(200, 180, 100, 255);

        if (t < 0.6f)
        {
            // Phase 1: Ship shrinking with fading
            float p = t / 0.6f;
            float scale = 1f - p * 0.8f;
            int scaledSize = Math.Max((int)(shipSize * scale), 4);
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, scaledSize);

            // Atmospheric glow around the landing ship
            float glowRadius = shipSize * 0.5f * (1f - p * 0.5f);
            renderer.DrawFilledCircle(camera, pos, glowRadius,
                landingColor.WithAlpha((byte)(80 * (1f - p))));
        }
        else
        {
            // Phase 2: Landing ring pulse at surface position
            float p = (t - 0.6f) / 0.4f;
            float ringRadius = shipSize * (0.3f + p * 1.2f);
            byte ringAlpha = (byte)(100 * (1f - p));
            if (ringAlpha > 3)
                renderer.DrawCircle(camera, pos, ringRadius, landingColor.WithAlpha(ringAlpha));

            // Small glow at center
            byte centerAlpha = (byte)(40 * (1f - p));
            if (centerAlpha > 3)
                renderer.DrawFilledCircle(camera, pos, shipSize * 0.15f * (1f - p),
                    landingColor.WithAlpha(centerAlpha));
        }
    }

    private static void RenderSolarSystemToSpaceStation(ISpriteRenderer renderer, Camera camera,
        SpaceshipRenderer spaceshipRenderer, TransitionEffect fx, float t)
    {
        var pos = fx.Position;
        int shipSize = ShipTypeCatalog.GetById(fx.ShipTypeId)?.SpriteSize ?? 32;
        var dockingColor = new Color4(100, 200, 255, 255);

        if (t < 0.5f)
        {
            // Phase 1: Ship fading with a shrinking glow
            float p = t / 0.5f;
            int scaledSize = Math.Max((int)(shipSize * (1f - p * 0.6f)), 4);
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, scaledSize);

            // Docking energy effect
            renderer.DrawFilledCircle(camera, pos, shipSize * 0.4f * (1f - p * 0.3f),
                dockingColor.WithAlpha((byte)(60 * (1f - p))));
        }
        else
        {
            // Phase 2: Residual station docking glow
            float p = (t - 0.5f) / 0.5f;
            byte glowAlpha = (byte)(50 * (1f - p));
            if (glowAlpha > 3)
                renderer.DrawFilledCircle(camera, pos, shipSize * 0.25f * (1f - p * 0.5f),
                    dockingColor.WithAlpha(glowAlpha));
        }
    }

    // ── Arrival effects ─────────────────────────────────────────────

    private static void RenderFTLJumpArrival(ISpriteRenderer renderer, Camera camera,
        SpaceshipRenderer spaceshipRenderer, TransitionEffect fx, float t)
    {
        var pos = fx.Position;
        float rad = fx.Rotation * MathF.PI / 180f;
        var forward = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
        int shipSize = ShipTypeCatalog.GetById(fx.ShipTypeId)?.SpriteSize ?? 32;
        var warpColor = new Color4(120, 180, 255, 255);

        if (t < 0.3f)
        {
            // Phase 1: Streak converging to arrival point
            float p = t / 0.3f;
            float streakLen = shipSize * (8f * (1f - p));
            var streakStart = pos - forward * streakLen;
            int steps = 8;
            byte streakAlpha = (byte)(180 * p);
            for (int i = 0; i <= steps; i++)
            {
                float st = i / (float)steps;
                var sp = Vector2.Lerp(streakStart, pos, st);
                float radius = st * shipSize * 0.2f * p;
                byte a = (byte)(streakAlpha * st);
                renderer.DrawFilledCircle(camera, sp, Math.Max(radius, 1f), warpColor.WithAlpha(a));
            }
        }
        else if (t < 0.6f)
        {
            // Phase 2: Bright flash as ship materializes
            float p = (t - 0.3f) / 0.3f;

            float flashIntensity = MathF.Max(0, 1f - p * 1.5f);
            if (flashIntensity > 0)
            {
                renderer.DrawFilledCircle(camera, pos, shipSize * (0.8f + flashIntensity),
                    new Color4(255, 255, 255, (byte)(200 * flashIntensity)));
                renderer.DrawFilledCircle(camera, pos, shipSize * (0.4f + flashIntensity * 0.3f),
                    warpColor.WithAlpha((byte)(150 * flashIntensity)));
            }

            // Ship fading in
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, shipSize);
        }
        else
        {
            // Phase 3: Ship fully visible, residual glow fading
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, shipSize);

            float p = (t - 0.6f) / 0.4f;
            byte glowAlpha = (byte)(60 * (1f - p));
            if (glowAlpha > 3)
                renderer.DrawFilledCircle(camera, pos, shipSize * 0.3f * (1f - p * 0.5f),
                    warpColor.WithAlpha(glowAlpha));
        }
    }

    private static void RenderPlanetToSolarSystem(ISpriteRenderer renderer, Camera camera,
        SpaceshipRenderer spaceshipRenderer, TransitionEffect fx, float t)
    {
        var pos = fx.Position;
        int shipSize = ShipTypeCatalog.GetById(fx.ShipTypeId)?.SpriteSize ?? 32;
        var liftoffColor = new Color4(200, 180, 100, 255);

        if (t < 0.4f)
        {
            // Phase 1: Ring pulse expanding from surface + ship growing
            float p = t / 0.4f;
            float ringRadius = shipSize * (0.2f + p * 1.0f);
            byte ringAlpha = (byte)(100 * (1f - p));
            if (ringAlpha > 3)
                renderer.DrawCircle(camera, pos, ringRadius, liftoffColor.WithAlpha(ringAlpha));

            // Ship growing from small
            float scale = 0.2f + p * 0.8f;
            int scaledSize = Math.Max((int)(shipSize * scale), 4);
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, scaledSize);

            // Atmospheric glow
            renderer.DrawFilledCircle(camera, pos, shipSize * 0.4f * p,
                liftoffColor.WithAlpha((byte)(60 * p)));
        }
        else
        {
            // Phase 2: Ship at full size, glow fading
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, shipSize);

            float p = (t - 0.4f) / 0.6f;
            byte glowAlpha = (byte)(40 * (1f - p));
            if (glowAlpha > 3)
                renderer.DrawFilledCircle(camera, pos, shipSize * 0.3f * (1f - p * 0.5f),
                    liftoffColor.WithAlpha(glowAlpha));
        }
    }

    private static void RenderSpaceStationToSolarSystem(ISpriteRenderer renderer, Camera camera,
        SpaceshipRenderer spaceshipRenderer, TransitionEffect fx, float t)
    {
        var pos = fx.Position;
        int shipSize = ShipTypeCatalog.GetById(fx.ShipTypeId)?.SpriteSize ?? 32;
        var undockColor = new Color4(100, 200, 255, 255);

        if (t < 0.4f)
        {
            // Phase 1: Ship fading in with energy glow
            float p = t / 0.4f;
            int scaledSize = Math.Max((int)(shipSize * (0.4f + p * 0.6f)), 4);
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, scaledSize);

            renderer.DrawFilledCircle(camera, pos, shipSize * 0.4f * p,
                undockColor.WithAlpha((byte)(60 * p)));
        }
        else
        {
            // Phase 2: Ship fully visible, residual glow
            spaceshipRenderer.Render(renderer, camera, pos, fx.Rotation, fx.ShipTypeId, shipSize);

            float p = (t - 0.4f) / 0.6f;
            byte glowAlpha = (byte)(40 * (1f - p));
            if (glowAlpha > 3)
                renderer.DrawFilledCircle(camera, pos, shipSize * 0.25f * (1f - p * 0.5f),
                    undockColor.WithAlpha(glowAlpha));
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static (Vector2 position, float rotation) FindRemotePlayerPositionAndRotation(SolarSystemSimulation sim, byte remotePlayerId, TransitionType transitionType, NetPlayerLocation from)
    {
        foreach (var p in sim.Players)
        {
            if (p.Type == PlayerType.Remote && p.RemotePlayerId == remotePlayerId)
            {
                if (sim.EcsWorld.IsAlive(p.Entity) && sim.EcsWorld.Has<Transform>(p.Entity))
                {
                    ref var tf = ref sim.EcsWorld.Get<Transform>(p.Entity);
                    return (tf.Position, tf.Rotation);
                }
            }
        }

        // Player not found in the simulation, we can only get here is the player is not in the simulation yet, which means it's always an "arrival"
        if (from.IsUnknown)
        {
            // This should not happen, but just in case, spawn at center
            return (Vector2.Zero, 0f);
        }

        if (transitionType == TransitionType.SpaceStationToSolarSystem)
        {
            if (from.IsOnSpaceStation)
            {
                var spaceStation = sim.SpaceStationEntities.ElementAtOrDefault(from.SpaceStationIndex);
                if (spaceStation != Entity.Null && sim.EcsWorld.IsAlive(spaceStation) && sim.EcsWorld.Has<Transform>(spaceStation))
                {
                    ref var tf = ref sim.EcsWorld.Get<Transform>(spaceStation);
                    return (tf.Position, tf.Rotation);
                }
            }
        }
        else if (transitionType == TransitionType.PlanetToSolarSystem)
        {
            if (from.IsOnPlanet)
            {
                var planet = sim.PlanetEntities.ElementAtOrDefault(from.PlanetIndex);
                if (planet != Entity.Null && sim.EcsWorld.IsAlive(planet) && sim.EcsWorld.Has<Transform>(planet))
                {
                    if (from.IsOnMoon)
                    {
                        var moon = sim.MoonEntities[from.PlanetIndex].ElementAtOrDefault(from.MoonIndex);
                        if (moon != Entity.Null && sim.EcsWorld.IsAlive(moon) && sim.EcsWorld.Has<Transform>(moon))
                        {
                            ref var tf = ref sim.EcsWorld.Get<Transform>(moon);
                            return (tf.Position, tf.Rotation);
                        }
                    }
                    else
                    {
                        ref var tf = ref sim.EcsWorld.Get<Transform>(planet);
                        return (tf.Position, tf.Rotation);
                    }
                }
            }
        }

        // Fallback to default spawn point in solar system
        return (sim.GetDefaultSpawnCoordinates(), 0f);
    }
}
