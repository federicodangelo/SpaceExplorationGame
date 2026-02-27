using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders projectiles as colored elongated dots with a trail effect.
/// Stateless — uses draw primitives, no owned textures.
/// </summary>
public static class ProjectileRenderer
{
    /// <summary>Render all projectile entities in the world.</summary>
    public static void RenderProjectiles(SpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, Velocity, Projectile>();
        world.Query(in query, (ref Transform transform, ref Velocity velocity, ref Projectile proj) =>
        {
            var pos = transform.Position;
            float speed = velocity.Linear.Length();
            float rad = transform.Rotation * MathF.PI / 180f;
            var facingDir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));

            // Draw projectile as a small elongated shape
            if (speed > 10f)
            {
                // Trail: draw a line behind the projectile
                var trailEnd = pos - facingDir * 8f;

                // Main beam
                renderer.DrawLine(camera, trailEnd, pos, proj.Color);

                // Glow core (brighter, slightly offset lines)
                var perp = new Vector2(-facingDir.Y, facingDir.X) * 0.8f;
                renderer.DrawLine(camera, trailEnd + perp, pos + perp, proj.Color.WithAlpha(140));
                renderer.DrawLine(camera, trailEnd - perp, pos - perp, proj.Color.WithAlpha(140));
            }
            else
            {
                // Slow/stationary projectile: just a dot
                renderer.DrawFilledCircle(camera, pos, 2f, proj.Color);
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
    public static void RenderDamageEffects(SpriteRenderer renderer, Camera camera,
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

    /// <summary>Render explosion effects (no mutation).</summary>
    public static void RenderExplosions(SpriteRenderer renderer, Camera camera,
        List<Explosion> explosions)
    {
        foreach (var explosion in explosions)
        {
            float progress = 1f - (explosion.Timer / explosion.MaxTime);
            float radius = explosion.Radius * (0.3f + progress * 0.7f);
            byte alpha = (byte)(255 * (1f - progress));

            // Outer glow
            renderer.DrawFilledCircle(camera, explosion.Position, radius,
                explosion.Color.WithAlpha((byte)(alpha / 2)));

            // Inner core
            renderer.DrawFilledCircle(camera, explosion.Position, radius * 0.5f,
                new Color4(255, 255, 200, alpha));

            // Sparks
            if (progress < 0.5f)
            {
                float sparkRadius = radius * 1.5f;
                int sparkCount = 6;
                for (int s = 0; s < sparkCount; s++)
                {
                    float angle = s * MathF.PI * 2f / sparkCount + progress * 3f;
                    var sparkPos = explosion.Position + new Vector2(
                        MathF.Cos(angle) * sparkRadius * progress,
                        MathF.Sin(angle) * sparkRadius * progress);
                    renderer.DrawFilledCircle(camera, sparkPos, 2f,
                        new Color4(255, (byte)(200 * (1 - progress)), 50, alpha));
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
