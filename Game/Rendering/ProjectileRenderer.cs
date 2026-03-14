using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders projectiles as colored elongated dots with a trail effect.
/// Stateless — uses draw primitives, no owned textures.
/// </summary>
public static class ProjectileRenderer
{
    /// <summary>Render all projectile entities in the world.</summary>
    public static void RenderProjectiles(ISpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, Velocity, Projectile>();
        world.Query(in query, (ref Transform transform, ref Velocity velocity, ref Projectile proj) =>
        {
            var pos = transform.Position;
            float rad = transform.Rotation * MathF.PI / 180f;
            var facingDir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));

            if (proj.Behavior == WeaponBehavior.Beam)
            {
                // Render beam as a long glowing line from owner to max range
                var beamEnd = pos + facingDir * CombatConfig.BeamMaxRange;
                float halfWidth = CombatConfig.BeamWidth * 0.5f;
                var perp = new Vector2(-facingDir.Y, facingDir.X);

                // Outer glow
                renderer.DrawLine(camera, pos + perp * (halfWidth + 2f), beamEnd + perp * (halfWidth + 2f),
                    proj.Color.WithAlpha(40));
                renderer.DrawLine(camera, pos - perp * (halfWidth + 2f), beamEnd - perp * (halfWidth + 2f),
                    proj.Color.WithAlpha(40));

                // Main beam body
                renderer.DrawLine(camera, pos + perp * halfWidth, beamEnd + perp * halfWidth, proj.Color);
                renderer.DrawLine(camera, pos, beamEnd, proj.Color);
                renderer.DrawLine(camera, pos - perp * halfWidth, beamEnd - perp * halfWidth, proj.Color);

                // Bright core
                renderer.DrawLine(camera, pos, beamEnd, new Color4(255, 255, 255, 140));
                return;
            }

            float speed = velocity.Linear.Length();

            if (speed > 10f)
            {
                // Glow behind projectile
                var trailEnd = pos - facingDir * 12f;
                var perp = new Vector2(-facingDir.Y, facingDir.X);

                // Outer glow (wider, translucent)
                renderer.DrawLine(camera, trailEnd + perp * 2f, pos + perp * 1.5f,
                    proj.Color.WithAlpha(50));
                renderer.DrawLine(camera, trailEnd - perp * 2f, pos - perp * 1.5f,
                    proj.Color.WithAlpha(50));

                // Main beam (bright)
                renderer.DrawLine(camera, trailEnd, pos, proj.Color);

                // Core parallel lines
                renderer.DrawLine(camera, trailEnd + perp * 0.8f, pos + perp * 0.8f,
                    proj.Color.WithAlpha(160));
                renderer.DrawLine(camera, trailEnd - perp * 0.8f, pos - perp * 0.8f,
                    proj.Color.WithAlpha(160));

                // Bright tip
                renderer.DrawFilledCircle(camera, pos, 2f,
                    new Color4(255, 255, 255, 180));
            }
            else
            {
                renderer.DrawFilledCircle(camera, pos, 2.5f, proj.Color);
                renderer.DrawFilledCircle(camera, pos, 1.5f,
                    new Color4(255, 255, 255, 140));
            }
        });
    }

    /// <summary>Update damage popups: advance timers, float upward, remove expired.</summary>
    public static void UpdateDamageEffects(List<DamagePopup> popups, float dt)
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var popup = popups[i];
            popup.Timer -= dt;
            popup.Position.Y -= 30f * dt; // float upward

            if (popup.Timer <= 0)
            {
                popups.RemoveAt(i);
            }
        }
    }

    /// <summary>Render damage number popups (no mutation).</summary>
    public static void RenderDamageEffects(ISpriteRenderer renderer, Camera camera,
        List<DamagePopup> popups)
    {
        foreach (var popup in popups)
        {
            string text = popup.Damage.ToString("F0");

            if (popup.ShieldHit)
            {
                renderer.DrawText(camera, popup.Position, text, new Color3(80, 160, 255), 1.5f);
            }
            else
            {
                renderer.DrawText(camera, popup.Position, text, new Color3(255, 200, 80), 1.5f);
            }
        }
    }

    /// <summary>Update explosions: advance timers, remove expired.</summary>
    public static void UpdateExplosions(List<Explosion> explosions, float dt)
    {
        for (int i = explosions.Count - 1; i >= 0; i--)
        {
            var explosion = explosions[i];
            explosion.Timer -= dt;

            if (explosion.Timer <= 0)
            {
                explosions.RemoveAt(i);
            }
        }
    }

    /// <summary>Render explosion effects with shockwave, debris, and smoke.</summary>
    public static void RenderExplosions(ISpriteRenderer renderer, Camera camera,
        List<Explosion> explosions)
    {
        foreach (var explosion in explosions)
        {
            float progress = 1f - (explosion.Timer / explosion.MaxTime);
            float radius = explosion.Radius * (0.3f + progress * 0.7f);
            byte alpha = (byte)(255 * (1f - progress));

            // Shockwave ring (expands fast, fades)
            if (progress < 0.6f)
            {
                float ringProgress = progress / 0.6f;
                float ringRadius = explosion.Radius * 1.4f * ringProgress;
                byte ringAlpha = (byte)(100 * (1f - ringProgress));
                renderer.DrawCircle(camera, explosion.Position, ringRadius,
                    new Color4(255, 255, 255, ringAlpha), 24);
                // Second thinner ring
                renderer.DrawCircle(camera, explosion.Position, ringRadius * 0.85f,
                    explosion.Color.WithAlpha((byte)(ringAlpha / 2)), 20);
            }

            // Outer glow
            renderer.DrawFilledCircle(camera, explosion.Position, radius,
                explosion.Color.WithAlpha((byte)(alpha / 2)));

            // Hot core (white-yellow)
            if (progress < 0.5f)
            {
                float coreFade = 1f - progress * 2f;
                renderer.DrawFilledCircle(camera, explosion.Position, radius * 0.5f,
                    new Color4(255, 255, 200, (byte)(alpha * coreFade)));
                // Bright center flash
                renderer.DrawFilledCircle(camera, explosion.Position, radius * 0.2f,
                    new Color4(255, 255, 255, (byte)(200 * coreFade)));
            }

            // Sparks that fly outward
            if (progress < 0.7f)
            {
                float sparkRadius = radius * 1.8f;
                int sparkCount = 8;
                for (int s = 0; s < sparkCount; s++)
                {
                    float angle = s * MathF.PI * 2f / sparkCount + progress * 4f;
                    float sparkDist = sparkRadius * progress * (0.8f + 0.4f * MathF.Sin(s * 3.7f));
                    var sparkPos = explosion.Position + new Vector2(
                        MathF.Cos(angle) * sparkDist,
                        MathF.Sin(angle) * sparkDist);
                    byte sparkAlpha = (byte)(alpha * (1f - progress / 0.7f));
                    renderer.DrawFilledCircle(camera, sparkPos, 2f,
                        new Color4(255, (byte)(220 - progress * 200), 50, sparkAlpha));
                }
            }

            // Smoke puffs (late phase)
            if (progress > 0.3f)
            {
                float smokeFade = (progress - 0.3f) / 0.7f;
                int smokeCount = 4;
                for (int s = 0; s < smokeCount; s++)
                {
                    float angle = s * MathF.PI * 2f / smokeCount + 0.5f;
                    float dist = radius * 0.6f * smokeFade;
                    var smokePos = explosion.Position + new Vector2(
                        MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                    byte smokeAlpha = (byte)(30 * (1f - smokeFade));
                    renderer.DrawFilledCircle(camera, smokePos, 4f + smokeFade * 4f,
                        new Color4(80, 70, 60, smokeAlpha));
                }
            }
        }
    }
}

/// <summary>Floating damage number popup.</summary>
public class DamagePopup
{
    public Vector2 Position;
    public float Damage;
    public float Timer;
    public float MaxTime;
    public bool ShieldHit;

    public DamagePopup(Vector2 position, float damage, bool shieldHit, float duration = 1.0f)
    {
        Position = position;
        Damage = damage;
        Timer = duration;
        MaxTime = duration;
        ShieldHit = shieldHit;
    }
}

/// <summary>Visual explosion effect.</summary>
public class Explosion
{
    public Vector2 Position;
    public float Radius;
    public float Timer;
    public float MaxTime;
    public Color3 Color;

    public Explosion(Vector2 position, float radius, Color3 color, float duration = 0.6f)
    {
        Position = position;
        Radius = radius;
        Timer = duration;
        MaxTime = duration;
        Color = color;
    }
}
