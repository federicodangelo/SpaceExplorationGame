using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems.Effects;

/// <summary>
/// Ticks the <see cref="WarpEffect"/> animation on NPC ships.
/// When a warp-in completes the component is removed so normal gameplay resumes.
/// When a warp-out completes the entity is marked for destruction by setting hull to 0
/// (the existing combat pipeline handles cleanup).
/// </summary>
public class WarpEffectSystem(World world) : BaseSystem<World, float>(world)
{
    private static readonly QueryDescription _warpQuery =
        new QueryDescription().WithAll<WarpEffect>();

    private readonly List<Entity> _warpInComplete = [];
    private readonly List<Entity> _warpOutComplete = [];

    /// <summary>Entities whose warp-out animation completed this frame (ready for removal).</summary>
    public IReadOnlyList<Entity> WarpOutCompleted => _warpOutComplete;

    public override void Update(in float dt)
    {
        _warpInComplete.Clear();
        _warpOutComplete.Clear();

        float deltaTime = dt;
        World.Query(in _warpQuery, (Entity entity, ref WarpEffect warp) =>
        {
            warp.Progress += deltaTime / warp.Duration;
            if (warp.Progress >= 1f)
            {
                warp.Progress = 1f;
                if (warp.IsWarpingIn)
                    _warpInComplete.Add(entity);
                else
                    _warpOutComplete.Add(entity);
            }
        });

        // Remove WarpEffect from ships that finished warping in — they become normal combatants
        foreach (var entity in _warpInComplete)
        {
            if (World.IsAlive(entity) && World.Has<WarpEffect>(entity))
                World.Remove<WarpEffect>(entity);
        }
    }
}
