using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Destroys entities whose owner entity is no longer alive.
/// </summary>
public class DependentEntityCleanupSystem(World world) : BaseSystem<World, float>(world)
{
    private readonly List<Entity> _toDestroy = [];

    private static readonly QueryDescription _ownedQuery =
        new QueryDescription().WithAll<OwnedBy>();

    public override void Update(in float dt)
    {
        _toDestroy.Clear();

        World.Query(in _ownedQuery, (Entity entity, ref OwnedBy ownedBy) =>
        {
            if (!World.IsAlive(ownedBy.Owner))
            {
                _toDestroy.Add(entity);
            }
        });

        foreach (var entity in _toDestroy)
        {
            if (World.IsAlive(entity))
            {
                World.Destroy(entity);
            }
        }
    }
}
