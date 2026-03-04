using Arch.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Rendering;

/// <summary>Renders ECS particles as soft glowing circles.</summary>
public static class ParticleRenderer
{
    public static void RenderParticles(ISpriteRenderer renderer, Camera camera, World world)
    {
        var query = new QueryDescription().WithAll<Transform, Particle>();
        world.Query(in query, (ref Transform transform, ref Particle particle) =>
        {
            if (particle.Lifetime <= 0f) return;

            float t = Math.Clamp(particle.Age / particle.Lifetime, 0f, 1f);
            float life = 1f - t;
            if (life <= 0f) return;

            float radius = float.Lerp(particle.StartSize, particle.EndSize, t);
            byte alpha = (byte)Math.Clamp((int)(180f * life), 0, 255);

            renderer.DrawFilledCircle(camera, transform.Position, radius, particle.Color.WithAlpha(alpha));
            renderer.DrawFilledCircle(camera, transform.Position, radius * 0.45f,
                new Color4(255, 255, 255, (byte)(alpha * 0.45f)));
        });
    }
}
