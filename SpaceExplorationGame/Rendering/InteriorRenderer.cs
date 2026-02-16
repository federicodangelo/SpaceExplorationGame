using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders interior visuals: exterior background, tiles, NPCs, interactables, and dialogue.
/// </summary>
public static class InteriorRenderer
{
    /// <summary>
    /// Renders the full interior world (background, tiles, NPCs, interactables, player avatar).
    /// </summary>
    public static void RenderWorld(SpriteRenderer renderer, Camera camera, InteriorData interior,
        Vector2 playerPos, AvatarRenderer avatarRenderer, double globalTime, PlanetData? planet)
    {
        RenderExteriorBackground(renderer, camera, interior, planet);
        RenderTiles(renderer, camera, interior, globalTime);
        RenderRoomLabels(renderer, camera, interior);
        RenderNpcs(renderer, camera, interior);
        RenderInteractableMarkers(renderer, camera, interior, globalTime);
        RenderPlayerAvatar(renderer, camera, playerPos, avatarRenderer);
    }

    /// <summary>
    /// Draws the exterior background visible through Void tiles.
    /// Stations show space with stars; settlements show planet terrain.
    /// </summary>
    private static void RenderExteriorBackground(SpriteRenderer renderer, Camera camera,
        InteriorData interior, PlanetData? planet)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();

        // Extend slightly beyond visible area
        int margin = GameConfig.TileSize * 2;
        float bgLeft = topLeft.X - margin;
        float bgTop = topLeft.Y - margin;
        float bgRight = bottomRight.X + margin;
        float bgBottom = bottomRight.Y + margin;
        float bgW = bgRight - bgLeft;
        float bgH = bgBottom - bgTop;
        var bgCenter = new Vector2(bgLeft + bgW / 2f, bgTop + bgH / 2f);

        if (interior.Type == InteriorType.Station)
        {
            RenderSpaceBackground(renderer, camera, bgCenter, bgW, bgH, bgLeft, bgTop, bgRight, bgBottom);
        }
        else
        {
            RenderTerrainBackground(renderer, camera, bgCenter, bgW, bgH, bgLeft, bgTop, bgRight, bgBottom, planet);
        }

        // Draw interior boundary outline to make the structure edges visible
        RenderBoundaryOutline(renderer, camera, interior);
    }

    private static void RenderSpaceBackground(SpriteRenderer renderer, Camera camera,
        Vector2 bgCenter, float bgW, float bgH, float bgLeft, float bgTop, float bgRight, float bgBottom)
    {
        // Space background: dark blue-black
        renderer.DrawRect(camera, bgCenter, (int)bgW, (int)bgH, new Color3(4, 4, 12));

        // Deterministic stars based on visible area
        int starGridSize = 60;
        int sx0 = (int)MathF.Floor(bgLeft / starGridSize) - 1;
        int sy0 = (int)MathF.Floor(bgTop / starGridSize) - 1;
        int sx1 = (int)MathF.Ceiling(bgRight / starGridSize) + 1;
        int sy1 = (int)MathF.Ceiling(bgBottom / starGridSize) + 1;

        for (int sx = sx0; sx <= sx1; sx++)
        {
            for (int sy = sy0; sy <= sy1; sy++)
            {
                int h = (sx * 374761393 + sy * 668265263) ^ (sx * 17 + sy * 31);
                if ((h & 3) != 0) continue; // ~25% density

                float px = sx * starGridSize + ((h >> 4) & 0x3F) - 32;
                float py = sy * starGridSize + ((h >> 10) & 0x3F) - 32;

                int bright = 80 + ((h >> 16) & 0x7F);
                byte sr = (byte)Math.Min(255, bright + ((h >> 2) & 0x1F));
                byte sg = (byte)Math.Min(255, bright + ((h >> 5) & 0x0F));
                byte sb = (byte)Math.Min(255, bright + ((h >> 8) & 0x2F));

                int starSize = ((h >> 20) & 1) == 0 ? 2 : 1;
                renderer.DrawRect(camera, new Vector2(px, py), starSize, starSize, new Color3(sr, sg, sb));
            }
        }
    }

    private static void RenderTerrainBackground(SpriteRenderer renderer, Camera camera,
        Vector2 bgCenter, float bgW, float bgH, float bgLeft, float bgTop, float bgRight, float bgBottom,
        PlanetData? planet)
    {
        byte tr = (byte)((planet?.Color.R ?? 80) * 0.4f);
        byte tg = (byte)((planet?.Color.G ?? 120) * 0.4f);
        byte tb = (byte)((planet?.Color.B ?? 60) * 0.4f);

        renderer.DrawRect(camera, bgCenter, (int)bgW, (int)bgH, new Color3(tr, tg, tb));

        // Terrain detail: scattered dots for ground texture
        int detailGridSize = 40;
        int dx0 = (int)MathF.Floor(bgLeft / detailGridSize) - 1;
        int dy0 = (int)MathF.Floor(bgTop / detailGridSize) - 1;
        int dx1 = (int)MathF.Ceiling(bgRight / detailGridSize) + 1;
        int dy1 = (int)MathF.Ceiling(bgBottom / detailGridSize) + 1;

        for (int dx = dx0; dx <= dx1; dx++)
        {
            for (int dy = dy0; dy <= dy1; dy++)
            {
                int h = (dx * 374761393 + dy * 668265263) ^ (dx * 13 + dy * 29);
                if ((h & 7) > 2) continue;

                float px = dx * detailGridSize + ((h >> 4) & 0x1F) - 16;
                float py = dy * detailGridSize + ((h >> 10) & 0x1F) - 16;

                int var_ = ((h >> 16) & 0x1F) - 16;
                byte dr = (byte)Math.Clamp(tr + var_, 0, 255);
                byte dg = (byte)Math.Clamp(tg + var_, 0, 255);
                byte db = (byte)Math.Clamp(tb + var_, 0, 255);

                renderer.DrawRect(camera, new Vector2(px, py), 3, 3, new Color3(dr, dg, db));
            }
        }
    }

    private static void RenderBoundaryOutline(SpriteRenderer renderer, Camera camera, InteriorData interior)
    {
        float interiorPixelW = interior.Width * GameConfig.TileSize;
        float interiorPixelH = interior.Height * GameConfig.TileSize;
        var tl = new Vector2(0, 0);
        var tr = new Vector2(interiorPixelW, 0);
        var bl = new Vector2(0, interiorPixelH);
        var br = new Vector2(interiorPixelW, interiorPixelH);

        byte lr = interior.Type == InteriorType.Station ? (byte)40 : (byte)50;
        byte lg = interior.Type == InteriorType.Station ? (byte)50 : (byte)45;
        byte lb = interior.Type == InteriorType.Station ? (byte)80 : (byte)35;

        var lineColor = new Color3(lr, lg, lb);
        renderer.DrawLine(camera, tl, tr, lineColor);
        renderer.DrawLine(camera, tr, br, lineColor);
        renderer.DrawLine(camera, br, bl, lineColor);
        renderer.DrawLine(camera, bl, tl, lineColor);
    }

    /// <summary>Renders tiles using TileMapRenderer with interior-specific detail overlays.</summary>
    private static void RenderTiles(SpriteRenderer renderer, Camera camera,
        InteriorData interior, double globalTime)
    {
        TileMapRenderer.RenderTiles(renderer, camera, interior.Width, interior.Height,
            (x, y) =>
            {
                var tile = interior.Tiles[x, y];
                if (tile == InteriorTileType.Void) return null;
                return InteriorGenerator.GetTileColor(tile);
            },
            1200f,
            (x, y, worldPos, hash) =>
            {
                var tile = interior.Tiles[x, y];

                // Wall detail: highlight top edge
                if (tile == InteriorTileType.Wall)
                {
                    var topEdge = new Vector2(x * GameConfig.TileSize + GameConfig.TileSize / 2f,
                                              y * GameConfig.TileSize + 2);
                    renderer.DrawRect(camera, topEdge, GameConfig.TileSize, 2, new Color3(55, 55, 65));
                }

                // Console glow
                if (tile == InteriorTileType.Console)
                {
                    float pulse = MathF.Sin((float)globalTime * 3f + x + y) * 0.3f + 0.7f;
                    byte gr = (byte)(40 * pulse);
                    byte gg = (byte)(120 * pulse);
                    byte gb = (byte)(180 * pulse);
                    renderer.DrawRect(camera, worldPos, GameConfig.TileSize - 8, GameConfig.TileSize - 8, new Color3(gr, gg, gb));
                }

                // Crate detail: cross pattern
                if (tile == InteriorTileType.Crate)
                {
                    renderer.DrawRect(camera, worldPos, GameConfig.TileSize - 6, 2, new Color3(120, 100, 60));
                    renderer.DrawRect(camera, worldPos, 2, GameConfig.TileSize - 6, new Color3(120, 100, 60));
                }

                // Landing pad markings
                if (tile == InteriorTileType.LandingPad)
                {
                    if ((x + y) % 2 == 0)
                    {
                        renderer.DrawRect(camera, worldPos, 4, 4, new Color3(80, 80, 40));
                    }
                }
            });
    }

    /// <summary>Renders room name labels above each room.</summary>
    private static void RenderRoomLabels(SpriteRenderer renderer, Camera camera, InteriorData interior)
    {
        foreach (var room in interior.Rooms)
        {
            float roomLabelW = renderer.MeasureText(room.Name, 3f) / 2f / camera.Zoom;
            var labelPos = new Vector2(
                (room.X + room.Width / 2f) * GameConfig.TileSize - roomLabelW,
                room.Y * GameConfig.TileSize - 8
            );
            renderer.DrawText(camera, labelPos, room.Name, new Color3(120, 120, 160), 3f);
        }
    }

    /// <summary>Renders all NPCs with body, head, nametag, and role tag.</summary>
    private static void RenderNpcs(SpriteRenderer renderer, Camera camera, InteriorData interior)
    {
        foreach (var npc in interior.Npcs)
        {
            var npcPos = new Vector2(
                npc.TileX * GameConfig.TileSize + GameConfig.TileSize / 2f,
                npc.TileY * GameConfig.TileSize + GameConfig.TileSize / 2f
            );

            // Shadow beneath feet
            var shadowPos = npcPos + new Vector2(0, 8);
            renderer.DrawRect(camera, shadowPos, 12, 3, new Color4(0, 0, 0, 60));

            // Body
            renderer.DrawRect(camera, npcPos, 10, 14, npc.Color);

            // Head circle approximation
            var headPos = npcPos - new Vector2(0, 8);
            renderer.DrawRect(camera, headPos, 8, 8, new Color3((byte)Math.Min(npc.Color.R + 30, 255),
                (byte)Math.Min(npc.Color.G + 30, 255), (byte)Math.Min(npc.Color.B + 30, 255)));

            // Nametag (centered)
            float nameW = renderer.MeasureText(npc.Name, 1.5f) / 2f / camera.Zoom;
            var namePos = npcPos - new Vector2(nameW, 18);
            renderer.DrawText(camera, namePos, npc.Name, new Color3(200, 200, 200), 1.5f);

            // Role tag (centered)
            float roleW = renderer.MeasureText(npc.Role, 1.5f) / 2f / camera.Zoom;
            var rolePos = npcPos + new Vector2(-roleW, 12);
            renderer.DrawText(camera, rolePos, npc.Role, npc.Color, 1.5f);
        }
    }

    /// <summary>Renders floating indicator markers above each interactable.</summary>
    private static void RenderInteractableMarkers(SpriteRenderer renderer, Camera camera,
        InteriorData interior, double globalTime)
    {
        foreach (var interactable in interior.Interactables)
        {
            var intPos = new Vector2(
                interactable.TileX * GameConfig.TileSize + GameConfig.TileSize / 2f,
                interactable.TileY * GameConfig.TileSize + GameConfig.TileSize / 2f
            );

            float bob = MathF.Sin((float)globalTime * 2f) * 3f;
            var indicatorPos = intPos - new Vector2(0, 20 + bob);

            var intColor = GetInteractableColor(interactable.Type);

            renderer.DrawRect(camera, indicatorPos, 6, 6, intColor);
            float intLabelW = renderer.MeasureText(interactable.Name, 1.5f) / 2f / camera.Zoom;
            renderer.DrawText(camera, indicatorPos - new Vector2(intLabelW, 10), interactable.Name, intColor, 1.5f);
        }
    }

    /// <summary>Renders the player avatar texture at the given position.</summary>
    private static void RenderPlayerAvatar(SpriteRenderer renderer, Camera camera,
        Vector2 playerPos, AvatarRenderer avatarRenderer)
    {
        avatarRenderer.Render(renderer, camera, playerPos);
    }

    /// <summary>Renders the dialogue box with NPC info, text, and continue prompt.</summary>
    public static void RenderDialogue(SpriteRenderer renderer, int w, int h,
        InteriorNpc npc, int dialogueLine)
    {
        float boxW = 600;
        float boxH = 120;
        float boxX = w / 2f - boxW / 2f;
        float boxY = h - boxH - 20;

        // Background
        renderer.DrawRectScreen(boxX - 2, boxY - 2, boxW + 4, boxH + 4, new Color4(60, 60, 100, 200));
        renderer.DrawRectScreen(boxX, boxY, boxW, boxH, new Color4(15, 15, 35, 240));

        // NPC name and role
        renderer.DrawTextScreen(boxX + 15, boxY + 10, npc.Name.ToUpper(),
            npc.Color, 2f);
        renderer.DrawTextScreen(boxX + 15 + renderer.MeasureText(npc.Name + "  ", 2f), boxY + 10,
            npc.Role, new Color3(120, 120, 150), 1.5f);

        // Dialogue line
        if (dialogueLine < npc.DialogueLines.Length)
        {
            string line = npc.DialogueLines[dialogueLine];

            // Word wrap at ~50 chars
            int lineY = 0;
            int charsPerLine = 55;
            for (int i = 0; i < line.Length; i += charsPerLine)
            {
                int end = Math.Min(i + charsPerLine, line.Length);
                if (end < line.Length && end > i)
                {
                    int lastSpace = line.LastIndexOf(' ', end - 1, end - i);
                    if (lastSpace > i) end = lastSpace + 1;
                }
                string segment = line[i..end].TrimEnd();
                renderer.DrawTextScreen(boxX + 15, boxY + 40 + lineY * 18, segment, new Color3(200, 200, 200), 1.5f);
                lineY++;
            }
        }

        // Continue prompt
        string continueText = dialogueLine < npc.DialogueLines.Length - 1
            ? "[ENTER] CONTINUE" : "[ENTER] CLOSE";
        renderer.DrawTextScreen(boxX + boxW - 200, boxY + boxH - 25, continueText, new Color3(100, 200, 100), 1.5f);
    }


    /// <summary>Returns the color associated with an interactable type.</summary>
    public static Color3 GetInteractableColor(InteractableType type) => type switch
    {
        InteractableType.RepairStation => new Color3(100, 255, 100),
        InteractableType.HealthStation => new Color3(100, 200, 255),
        InteractableType.MissionBoard => new Color3(100, 180, 255),
        InteractableType.ShipCustomization => new Color3(100, 220, 255),
        InteractableType.AvatarCustomization => new Color3(100, 255, 180),
        InteractableType.VehicleCustomization => new Color3(255, 180, 80),
        InteractableType.ShipDealer => new Color3(255, 200, 80),
        InteractableType.CargoTerminal => new Color3(255, 180, 50),
        InteractableType.ExitDoor => new Color3(255, 100, 100),
        _ => new Color3(200, 200, 200)
    };
}
