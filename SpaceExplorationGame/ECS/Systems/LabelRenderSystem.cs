using System.Numerics;
using Arch.Core;
using Arch.System;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.ECS.Systems;

/// <summary>
/// Renders text labels for all entities with Transform + Label components.
/// Positions the label below the entity at the configured offset, centered horizontally.
/// </summary>
public partial class LabelRenderSystem : BaseSystem<World, float>
{
    private readonly SpriteRenderer _renderer;
    private readonly Camera _camera;

    public LabelRenderSystem(World world, SpriteRenderer renderer, Camera camera)
        : base(world)
    {
        _renderer = renderer;
        _camera = camera;
    }

    [Query]
    [All(typeof(Transform), typeof(Label))]
    public void RenderLabel(ref Transform transform, ref Label label)
    {
        var textPos = transform.Position + new Vector2(0, label.OffsetY);
        float textScale = Math.Max(0.8f, _camera.Zoom * 0.8f);
        float textWidth = _renderer.MeasureText(label.Text, textScale);
        _renderer.DrawText(_camera, textPos - new Vector2(textWidth / (2 * _camera.Zoom), 0),
            label.Text, 180, 180, 180, textScale);
    }
}
