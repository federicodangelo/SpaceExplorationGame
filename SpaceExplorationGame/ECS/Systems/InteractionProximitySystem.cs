using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Finds the nearest interactable entity (with CelestialBody) to a given position.
/// Call FindNearest() each frame and check HasNearest / NearestEntity / NearestDistance.
/// </summary>
public partial class InteractionProximitySystem : BaseSystem<World, float>
{
    private static readonly QueryDescription _interactableQuery =
        new QueryDescription().WithAll<Transform, Interactable, CelestialBody>();

    private readonly float _interactionRadius;

    /// <summary>The nearest interactable entity found.</summary>
    public Entity NearestEntity { get; private set; }

    /// <summary>Whether a nearby interactable was found.</summary>
    public bool HasNearest { get; private set; }

    /// <summary>Distance to the nearest interactable.</summary>
    public float NearestDistance { get; private set; }

    public InteractionProximitySystem(World world, float interactionRadius) : base(world)
    {
        _interactionRadius = interactionRadius;
    }

    /// <summary>
    /// Scans all interactable entities and finds the nearest one to the given position.
    /// </summary>
    public void FindNearest(Vector2 playerPosition)
    {
        HasNearest = false;
        NearestDistance = float.MaxValue;

        World.Query(in _interactableQuery, (Entity entity, ref Transform transform, ref CelestialBody body) =>
        {
            float dist = Vector2.Distance(playerPosition, transform.Position);
            if (dist < body.Radius + _interactionRadius && dist < NearestDistance)
            {
                NearestDistance = dist;
                NearestEntity = entity;
                HasNearest = true;
            }
        });
    }
}
