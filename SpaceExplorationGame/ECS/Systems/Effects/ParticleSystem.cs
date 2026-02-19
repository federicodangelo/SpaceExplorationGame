using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Effects;

/// <summary>
/// ECS particle simulation and emission system.
/// Emitters can be toggled via <see cref="ParticleEmitter.IsEnabled"/>.
/// </summary>
public class ParticleSystem(World world) : BaseSystem<World, float>(world)
{
    private readonly List<Entity> _expiredParticles = [];
    private readonly List<ParticleSpawn> _spawnQueue = [];
    private readonly Random _random = new();

    private static readonly QueryDescription _particleQuery =
        new QueryDescription().WithAll<Transform, Particle>();

    private static readonly QueryDescription _emitterQuery =
        new QueryDescription().WithAll<Transform, ParticleEmitter>();

    private const int MaxParticleEntities = 1500;

    private bool _validateEmitterBounds;
    private VisibleBounds _emitterBounds;
    private float _outsideMarginPercent = 0.2f;

    /// <summary>
    /// Sets world-space bounds used to validate emitter updates.
    /// Emitters beyond the bounds expanded by <paramref name="outsideMarginPercent"/> are disabled.
    /// </summary>
    public void SetEmitterValidationBounds(VisibleBounds bounds, float outsideMarginPercent = 0.2f)
    {
        _emitterBounds = bounds;
        _outsideMarginPercent = Math.Max(0f, outsideMarginPercent);
        _validateEmitterBounds = true;
    }

    public override void Update(in float dt)
    {
        float deltaTime = dt;
        _expiredParticles.Clear();
        _spawnQueue.Clear();

        int particleCount = 0;

        // 1) Simulate particles and mark expired.
        World.Query(in _particleQuery, (Entity entity, ref Transform transform, ref Particle particle) =>
        {
            particleCount++;

            particle.Age += deltaTime;
            if (particle.Age >= particle.Lifetime)
            {
                _expiredParticles.Add(entity);
                return;
            }

            float dragFactor = Math.Clamp(1f - particle.Drag * deltaTime, 0f, 1f);
            particle.Velocity *= dragFactor;
            transform.Position += particle.Velocity * deltaTime;
        });

        // 2) Emit new particles from active emitters.
        World.Query(in _emitterQuery, (ref Transform transform, ref ParticleEmitter emitter) =>
        {
            if (!emitter.IsEnabled)
            {
                emitter.SpawnAccumulator = 0f;
                return;
            }

            if (_validateEmitterBounds && !IsWithinEmitterValidationBounds(transform.Position))
            {
                emitter.SpawnAccumulator = 0f;
                return;
            }

            var accelDir = emitter.AccelerationDirection;
            if (accelDir.LengthSquared() < 0.0001f)
            {
                float rad = transform.Rotation * MathF.PI / 180f;
                accelDir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
            }

            accelDir = Vector2.Normalize(accelDir);
            var ejectDir = -accelDir;
            var perp = new Vector2(-accelDir.Y, accelDir.X);

            emitter.SpawnAccumulator += deltaTime;
            while (emitter.SpawnAccumulator >= emitter.SpawnInterval && particleCount + _spawnQueue.Count < MaxParticleEntities)
            {
                emitter.SpawnAccumulator -= emitter.SpawnInterval;

                float lateral = NextFloat(-4f, 4f);
                var spawnPos = transform.Position + ejectDir * emitter.SternOffset + perp * lateral;

                float ejectSpeed = NextFloat(emitter.EjectSpeedMin, emitter.EjectSpeedMax);
                float sideDrift = NextFloat(-emitter.LateralDrift, emitter.LateralDrift);
                var velocity = emitter.CarrierVelocity + ejectDir * ejectSpeed + perp * sideDrift;

                float life = NextFloat(emitter.ParticleLifeMin, emitter.ParticleLifeMax);
                float size = NextFloat(emitter.ParticleSizeMin, emitter.ParticleSizeMax);

                _spawnQueue.Add(new ParticleSpawn(
                    spawnPos,
                    velocity,
                    life,
                    size,
                    size * 0.45f,
                    emitter.ParticleDrag,
                    emitter.ParticleColor));
            }
        });

        // 3) Destroy expired.
        foreach (var entity in _expiredParticles)
        {
            if (World.IsAlive(entity))
                World.Destroy(entity);
        }

        // 4) Spawn queued particles.
        foreach (var spawn in _spawnQueue)
        {
            World.Create(
                new Transform(spawn.Position),
                new Particle
                {
                    Velocity = spawn.Velocity,
                    Age = 0f,
                    Lifetime = spawn.Lifetime,
                    StartSize = spawn.StartSize,
                    EndSize = spawn.EndSize,
                    Drag = spawn.Drag,
                    Color = spawn.Color
                });
        }
    }

    private float NextFloat(float min, float max)
        => min + _random.NextSingle() * (max - min);

    private bool IsWithinEmitterValidationBounds(Vector2 worldPos)
    {
        float worldW = _emitterBounds.BottomRight.X - _emitterBounds.TopLeft.X;
        float worldH = _emitterBounds.BottomRight.Y - _emitterBounds.TopLeft.Y;
        float marginX = worldW * _outsideMarginPercent;
        float marginY = worldH * _outsideMarginPercent;

        float minX = _emitterBounds.TopLeft.X - marginX;
        float maxX = _emitterBounds.BottomRight.X + marginX;
        float minY = _emitterBounds.TopLeft.Y - marginY;
        float maxY = _emitterBounds.BottomRight.Y + marginY;

        return worldPos.X >= minX && worldPos.X <= maxX && worldPos.Y >= minY && worldPos.Y <= maxY;
    }

    private readonly record struct ParticleSpawn(
        Vector2 Position,
        Vector2 Velocity,
        float Lifetime,
        float StartSize,
        float EndSize,
        float Drag,
        Color3 Color);
}
