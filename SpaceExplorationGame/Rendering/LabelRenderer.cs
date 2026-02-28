using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders text labels for all entities with Transform + Label components.
/// Positions the label below the entity at the configured offset, centered horizontally.
/// </summary>
public class LabelRenderer
{
    private static readonly QueryDescription _labelQuery =
        new QueryDescription().WithAll<Transform, Label>();

    private readonly World _world;
    private readonly ISpriteRenderer _renderer;
    private readonly Camera _camera;

    public LabelRenderer(World world, ISpriteRenderer renderer, Camera camera)
    {
        _world = world;
        _renderer = renderer;
        _camera = camera;
    }

    /// <summary>Render all entity labels.</summary>
    public void Render()
    {
        _world.Query(in _labelQuery, (ref Transform transform, ref Label label) =>
        {
            var textPos = transform.Position + new Vector2(0, label.OffsetY);
            float textScale = Math.Max(1, _camera.Zoom);
            float textWidth = _renderer.MeasureText(label.Text, textScale);
            _renderer.DrawText(_camera, textPos - new Vector2(textWidth / (2 * _camera.Zoom), 0),
                label.Text, new Color3(180, 180, 180), textScale);
        });
    }
}
