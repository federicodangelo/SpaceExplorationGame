using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Effects;

/// <summary>
/// ECS particle simulation and emission system.
/// Emits particles from <see cref="ParticleEmitter"/> entities according to
/// their <see cref="EmitCondition"/> and current source motion.
/// </summary>
public class ParticleSystem(World world) : BaseSystem<World, float>(world)
{
    private readonly List<Entity> _expiredEntities = [];
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
        _expiredEntities.Clear();
        _spawnQueue.Clear();

        int particleCount = SimulateParticles(dt);
        EmitParticles(dt, particleCount);
        DestroyExpiredEntities();
        SpawnQueuedParticles();
    }

    private int SimulateParticles(float dt)
    {
        int particleCount = 0;

        World.Query(in _particleQuery, (Entity entity, ref Transform transform, ref Particle particle) =>
        {
            particleCount++;

            particle.Age += dt;
            if (particle.Age >= particle.Lifetime)
            {
                _expiredEntities.Add(entity);
                return;
            }

            float dragFactor = Math.Clamp(1f - particle.Drag * dt, 0f, 1f);
            particle.Velocity *= dragFactor;
            transform.Position += particle.Velocity * dt;
        });

        return particleCount;
    }

    private void EmitParticles(float dt, int particleCount)
    {
        World.Query(in _emitterQuery, (Entity entity, ref Transform transform, ref ParticleEmitter emitter) =>
        {
            EmitFromEmitter(entity, ref transform, ref emitter, dt, particleCount);
        });
    }

    private void EmitFromEmitter(Entity entity, ref Transform transform, ref ParticleEmitter emitter, float dt, int particleCount)
    {
        if (emitter.EmitCondition == EmitCondition.Never)
        {
            return;
        }

        if (!TryResolveSource(entity, ref transform, ref emitter, out var sourceEntity, out var sourceTransform, out bool hasCarrier))
        {
            return;
        }

        Vector2 carrierVelocity = Vector2.Zero;
        Vector2 acceleration = Vector2.Zero;
        float rotationVelocity = 0f;
        if (World.Has<Velocity>(sourceEntity))
        {
            var velocity = World.Get<Velocity>(sourceEntity);
            carrierVelocity = velocity.Linear;
            acceleration = velocity.Acceleration;
            rotationVelocity = velocity.RotationVelocity;
        }

        if (!TryBuildEmissionData(ref transform, ref emitter, sourceTransform, hasCarrier,
            acceleration, rotationVelocity, out var baseSpawnPos, out var ejectDir))
        {
            return;
        }

        QueueParticleSpawns(ref emitter, dt, particleCount, baseSpawnPos, ejectDir, carrierVelocity);
    }

    private bool TryResolveSource(Entity entity, ref Transform transform, ref ParticleEmitter emitter,
        out Entity sourceEntity, out Transform sourceTransform, out bool hasCarrier)
    {
        hasCarrier = emitter.CarrierEntity != default
            && World.IsAlive(emitter.CarrierEntity)
            && World.Has<Transform>(emitter.CarrierEntity);

        if (emitter.CarrierEntity != default && !hasCarrier)
        {
            sourceEntity = default;
            sourceTransform = default;
            return false;
        }

        sourceEntity = hasCarrier ? emitter.CarrierEntity : entity;
        sourceTransform = hasCarrier ? World.Get<Transform>(emitter.CarrierEntity) : transform;
        return true;
    }

    private bool TryBuildEmissionData(ref Transform emitterTransform, ref ParticleEmitter emitter,
        Transform sourceTransform, bool hasCarrier, Vector2 acceleration, float rotationVelocity,
        out Vector2 baseSpawnPos, out Vector2 ejectDir)
    {
        Vector2 forward = GetForward(sourceTransform.Rotation);
        Vector2 right = new(-forward.Y, forward.X);

        bool useFixedEmitter = hasCarrier && emitter.LocalEjectDirection.LengthSquared() > 0.0001f;

        baseSpawnPos = sourceTransform.Position;
        ejectDir = Vector2.Zero;

        if (useFixedEmitter)
        {
            baseSpawnPos += LocalToWorld(sourceTransform.Rotation, emitter.LocalOffset);
            emitterTransform.Position = baseSpawnPos;

            if (_validateEmitterBounds && !IsWithinEmitterValidationBounds(baseSpawnPos))
            {
                return false;
            }

            if (emitter.ActivationMask != ThrusterActivation.None)
            {
                var activeMask = BuildActivationMask(acceleration, rotationVelocity, forward, right);
                if ((activeMask & emitter.ActivationMask) == 0)
                {
                    return false;
                }
            }

            ejectDir = Vector2.Normalize(LocalToWorld(sourceTransform.Rotation, emitter.LocalEjectDirection));
            return true;
        }

        if (_validateEmitterBounds && !IsWithinEmitterValidationBounds(sourceTransform.Position))
        {
            return false;
        }

        Vector2 accelDir = acceleration;
        bool accelerating = accelDir.LengthSquared() >= 0.0001f;
        if (emitter.EmitCondition == EmitCondition.WhenAccelerating && !accelerating)
        {
            return false;
        }

        if (!accelerating)
        {
            accelDir = forward;
        }

        accelDir = Vector2.Normalize(accelDir);
        ejectDir = -accelDir;
        baseSpawnPos = sourceTransform.Position + ejectDir * emitter.SternOffset;
        return true;
    }

    private void QueueParticleSpawns(ref ParticleEmitter emitter, float dt, int particleCount,
        Vector2 baseSpawnPos, Vector2 ejectDir, Vector2 carrierVelocity)
    {
        var perp = new Vector2(-ejectDir.Y, ejectDir.X);

        emitter.SpawnAccumulator += dt;
        while (emitter.SpawnAccumulator >= emitter.SpawnInterval && particleCount + _spawnQueue.Count < MaxParticleEntities)
        {
            emitter.SpawnAccumulator -= emitter.SpawnInterval;

            float lateral = NextFloat(-4f, 4f);
            var spawnPos = baseSpawnPos + perp * lateral;

            float ejectSpeed = NextFloat(emitter.EjectSpeedMin, emitter.EjectSpeedMax);
            float sideDrift = NextFloat(-emitter.LateralDrift, emitter.LateralDrift);
            var velocity = carrierVelocity + ejectDir * ejectSpeed + perp * sideDrift;

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
    }

    private void DestroyExpiredEntities()
    {
        foreach (var entity in _expiredEntities)
        {
            if (World.IsAlive(entity))
            {
                World.Destroy(entity);
            }
        }
    }

    private void SpawnQueuedParticles()
    {
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

    private static Vector2 GetForward(float rotationDegrees)
    {
        float rad = rotationDegrees * MathF.PI / 180f;
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }

    private static Vector2 LocalToWorld(float rotationDegrees, Vector2 localVector)
    {
        var forward = GetForward(rotationDegrees);
        var right = new Vector2(-forward.Y, forward.X);
        return forward * localVector.X + right * localVector.Y;
    }

    private static ThrusterActivation BuildActivationMask(Vector2 acceleration, float rotationVelocity,
        Vector2 forward, Vector2 right)
    {
        const float linearThreshold = 0.1f;
        const float rotationThreshold = 2f;

        var mask = ThrusterActivation.None;
        float forwardAccel = Vector2.Dot(acceleration, forward);
        float sideAccel = Vector2.Dot(acceleration, right);

        if (forwardAccel > linearThreshold) mask |= ThrusterActivation.Forward;
        if (forwardAccel < -linearThreshold) mask |= ThrusterActivation.Backward;
        if (sideAccel > linearThreshold) mask |= ThrusterActivation.StrafeRight;
        if (sideAccel < -linearThreshold) mask |= ThrusterActivation.StrafeLeft;

        if (rotationVelocity > rotationThreshold) mask |= ThrusterActivation.RotateRight;
        if (rotationVelocity < -rotationThreshold) mask |= ThrusterActivation.RotateLeft;

        return mask;
    }

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
