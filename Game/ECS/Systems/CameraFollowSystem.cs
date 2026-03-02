using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Camera follow system: smoothly tracks the PlayerControlled entity's position
/// and handles mouse-wheel zoom.
/// </summary>
public partial class CameraFollowSystem : BaseSystem<World, float>
{
    private readonly Camera _camera;
    private readonly float _lerpSpeed;

    public CameraFollowSystem(World world, Camera camera, float lerpSpeed = 50f)
        : base(world)
    {
        _camera = camera;
        _lerpSpeed = lerpSpeed;
    }

    [Query]
    [All(typeof(PlayerControlled), typeof(Transform))]
    public void FollowPlayer(ref Transform transform, [Data] float dt)
    {
        _camera.LerpTo(transform.Position, _lerpSpeed * dt);
    }
}
