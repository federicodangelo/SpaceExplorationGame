using System.Numerics;
using SpaceExplorationGame.Core;
using Engine.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player's spaceship using layered primitives.
/// Each ship type has a distinct silhouette, shaded hull faces, panel highlights,
/// swept wings with leading-edge accents, engine nozzle rings, soft exhaust glows,
/// and a cockpit dome with a glass glint.
/// Ship local space: nose points in the +X direction, centre at (0,0).
/// </summary>
public class SpaceshipRenderer
{
    public SpaceshipRenderer()
    {
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Renders the ship in flight (world space).</summary>
    public void Render(ISpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, string shipTypeId, int spriteSize)
    {
        var screen = camera.WorldToScreen(position);
        DrawShipPrimitivesScreen(renderer, screen.X, screen.Y, camera.Zoom, rotation, shipTypeId, spriteSize);
    }

    /// <summary>Renders the ship directly in screen space.</summary>
    public void RenderScreen(ISpriteRenderer renderer,
        float screenX, float screenY, float rotation, string shipTypeId, int spriteSize, float zoom = 1f)
    {
        DrawShipPrimitivesScreen(renderer, screenX, screenY, zoom, rotation, shipTypeId, spriteSize);
    }

    /// <summary>Renders the ship with a label below (world space).</summary>
    public void RenderWithLabel(ISpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, string shipTypeId, int spriteSize)
    {
        var screen = camera.WorldToScreen(position);
        DrawShipPrimitivesScreen(renderer, screen.X, screen.Y, camera.Zoom, rotation, shipTypeId, spriteSize);
        renderer.DrawText(camera, position + new Vector2(-12, 14), "SHIP", new Color3(180, 180, 200));
    }

    public void RenderShadow(ISpriteRenderer renderer, Camera camera, Vector2 position, int spriteSize)
    {
        // Shadow (centered under the ship, offset downward during flight)
        float shadowW = spriteSize * 0.8f;
        float shadowH = spriteSize * 0.25f;
        byte a = 80;
        renderer.DrawRect(camera,
            position + new Vector2(0, shadowH * 0.5f),
            (int)shadowW, (int)shadowH,
            new Color4(0, 0, 0, a));
    }

    /// <summary>Renders damage smoke and sparks when hull is low.</summary>
    public static void RenderDamageEffects(ISpriteRenderer renderer, Camera camera,
        Vector2 position, float hullPercent, float globalTime)
    {
        if (hullPercent >= 0.6f) return;

        float severity = 1f - (hullPercent / 0.6f);

        int smokeCount = (int)(severity * 4) + 1;
        for (int i = 0; i < smokeCount; i++)
        {
            float phase = globalTime * 1.5f + i * 2.3f;
            float offsetX = MathF.Sin(phase * 1.3f + i) * 8f;
            float offsetY = MathF.Cos(phase * 0.9f + i * 0.7f) * 6f - MathF.Sin(phase) * 4f;
            float size = 3f + severity * 4f + MathF.Sin(phase) * 2f;
            byte smokeAlpha = (byte)(20 + severity * 30);
            renderer.DrawFilledCircle(camera, position + new Vector2(offsetX, offsetY), size,
                new Color4(80, 70, 60, smokeAlpha));
        }

        if (severity > 0.3f)
        {
            int sparkCount = (int)((severity - 0.3f) * 6) + 1;
            for (int i = 0; i < sparkCount; i++)
            {
                float phase = globalTime * 8f + i * 5.7f;
                float flicker = MathF.Sin(phase);
                if (flicker < 0.3f) continue;
                float ox = MathF.Sin(phase * 0.7f + i * 3f) * 10f;
                float oy = MathF.Cos(phase * 0.5f + i * 2f) * 8f;
                byte sparkAlpha = (byte)(flicker * 200);
                renderer.DrawFilledCircle(camera, position + new Vector2(ox, oy), 1.5f,
                    new Color4(255, 200, 50, sparkAlpha));
            }
        }

        if (severity > 0.7f)
        {
            float pulse = (MathF.Sin(globalTime * 4f) + 1f) * 0.5f;
            byte warnAlpha = (byte)(pulse * 15 * (severity - 0.7f) / 0.3f);
            renderer.DrawFilledCircle(camera, position, 20f, new Color4(255, 50, 30, warnAlpha));
        }
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    private static void DrawShipPrimitivesScreen(ISpriteRenderer renderer, float screenX, float screenY,
        float zoom, float rotationDeg, string shipTypeId, int spriteSize)
    {
        float scale = (spriteSize / 32f) * zoom;
        switch (shipTypeId)
        {
            case "fighter": DrawFighter(renderer, screenX, screenY, rotationDeg, scale); break;
            case "freighter": DrawFreighter(renderer, screenX, screenY, rotationDeg, scale); break;
            case "explorer": DrawExplorer(renderer, screenX, screenY, rotationDeg, scale); break;
            default: DrawScout(renderer, screenX, screenY, rotationDeg, scale); break;
        }
    }

    // ── Ship draw routines ────────────────────────────────────────────────────

    /// <summary>
    /// Scout — nimble green interceptor.
    /// Hexagonal tapered hull, swept mid-wings, single engine pod.
    /// </summary>
    private static void DrawScout(ISpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        // Palette
        var hullTop = new Color4(70, 200, 85, 255);   // lit upper face
        var hullBot = new Color4(40, 130, 55, 255);   // shadowed lower face
        var noseColor = new Color4(105, 230, 110, 255);   // bright nose cone
        var hullGlow = new Color4(145, 240, 150, 130);   // dorsal highlight strip
        var wingBase = new Color4(45, 145, 60, 255);   // main wing fill
        var wingEdge = new Color4(95, 200, 105, 170);   // leading-edge highlight
        var cockpitCol = new Color4(120, 215, 255, 255);   // cockpit glass
        var nozzleCol = new Color4(50, 165, 65, 230);   // engine ring
        var thrustIn = new Color4(80, 255, 190, 230);   // engine exhaust centre
        var thrustOut = new Color4(60, 220, 160, 0);   // engine exhaust fade

        // --- Fuselage (hexagonal cross-section, 4 triangles) ---
        // Upper face (lighter — catches imaginary overhead light)
        Tri(renderer, cx, cy, rot, s,
            (-11, 0), (-7, -3.6f), (6, -2.6f), hullTop);
        Tri(renderer, cx, cy, rot, s,
            (-11, 0), (6, -2.6f), (14, 0), hullTop);
        // Lower face (darker)
        Tri(renderer, cx, cy, rot, s,
            (-11, 0), (-7, 3.6f), (6, 2.6f), hullBot);
        Tri(renderer, cx, cy, rot, s,
            (-11, 0), (6, 2.6f), (14, 0), hullBot);

        // --- Nose cone overlay (brighter) ---
        Tri(renderer, cx, cy, rot, s, (6, -2.6f), (14, 0), (6, 2.6f), noseColor);

        // --- Dorsal highlight strip (thin, semi-transparent) ---
        Tri(renderer, cx, cy, rot, s, (-4, -1.2f), (11, 0), (-4, 1.2f), hullGlow);

        // --- Wings (mid-mounted, swept rearward) ---
        Tri(renderer, cx, cy, rot, s, (0, -2.6f), (-8f, -11f), (-7, -2.6f), wingBase);
        Tri(renderer, cx, cy, rot, s, (0, 2.6f), (-8f, 11f), (-7, 2.6f), wingBase);
        // Leading-edge accent
        Tri(renderer, cx, cy, rot, s, (-0.5f, -2.8f), (-6.5f, -10f), (-5.5f, -2.8f), wingEdge);
        Tri(renderer, cx, cy, rot, s, (-0.5f, 2.8f), (-6.5f, 10f), (-5.5f, 2.8f), wingEdge);

        // --- Engine nozzle ring + exhaust glow ---
        var eng = R(new Vector2(-11.8f * s, 0), rot);
        renderer.DrawSolidRingScreen(cx + eng.X, cy + eng.Y, 1.6f * s, 3.1f * s, nozzleCol);
        renderer.DrawFilledCircleScreen(cx + eng.X, cy + eng.Y, 5.5f * s, thrustIn, thrustOut, 1.8f * s);

        // --- Cockpit dome + glass glint ---
        var cp = R(new Vector2(8.8f * s, 0), rot);
        renderer.DrawFilledCircleScreen(cx + cp.X, cy + cp.Y, 2.7f * s, cockpitCol);
        var gl = R(new Vector2(9.6f * s, -0.8f * s), rot);
        renderer.DrawFilledCircleScreen(cx + gl.X, cy + gl.Y, 0.85f * s, new Color4(255, 255, 255, 210));
    }

    /// <summary>
    /// Fighter — aggressive red/orange attack craft.
    /// Broad delta body, swept-back main wings, twin engine pods, armoured nose tip.
    /// </summary>
    private static void DrawFighter(ISpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hullTop = new Color4(210, 70, 70, 255);
        var hullBot = new Color4(140, 40, 40, 255);
        var noseTip = new Color4(255, 110, 60, 255);
        var hullGlow = new Color4(255, 150, 100, 100);
        var wingMain = new Color4(160, 50, 50, 255);
        var wingEdge = new Color4(230, 90, 60, 160);
        var canard = new Color4(180, 60, 55, 255);   // small front fins
        var cockpitC = new Color4(255, 220, 110, 255);
        var nozzleC = new Color4(180, 55, 40, 220);
        var thrustIn = new Color4(255, 160, 50, 240);
        var thrustOut = new Color4(255, 90, 20, 0);

        // --- Fuselage: wide delta body ---
        // Upper lit face
        Tri(renderer, cx, cy, rot, s, (-10, 0), (-4, -3.8f), (9, -1.5f), hullTop);
        Tri(renderer, cx, cy, rot, s, (-10, 0), (9, -1.5f), (14, 0), hullTop);
        // Lower shadow face
        Tri(renderer, cx, cy, rot, s, (-10, 0), (-4, 3.8f), (9, 1.5f), hullBot);
        Tri(renderer, cx, cy, rot, s, (-10, 0), (9, 1.5f), (14, 0), hullBot);

        // --- Nose armour tip ---
        Tri(renderer, cx, cy, rot, s, (9, -1.5f), (14, 0), (9, 1.5f), noseTip);

        // --- Dorsal glow ---
        Tri(renderer, cx, cy, rot, s, (-2, -1f), (11, 0), (-2, 1f), hullGlow);

        // --- Main swept delta wings ---
        Tri(renderer, cx, cy, rot, s, (-2, -3.8f), (-15, -12f), (-7, -2f), wingMain);
        Tri(renderer, cx, cy, rot, s, (-2, 3.8f), (-15, 12f), (-7, 2f), wingMain);
        // Wing leading-edge highlights
        Tri(renderer, cx, cy, rot, s, (-2.5f, -4f), (-13f, -11f), (-6.5f, -2.2f), wingEdge);
        Tri(renderer, cx, cy, rot, s, (-2.5f, 4f), (-13f, 11f), (-6.5f, 2.2f), wingEdge);

        // --- Forward canards (small swept fins near nose) ---
        Tri(renderer, cx, cy, rot, s, (7, -1.5f), (3, -5.5f), (2, -1.5f), canard);
        Tri(renderer, cx, cy, rot, s, (7, 1.5f), (3, 5.5f), (2, 1.5f), canard);

        // --- Twin engine pods ---
        var engU = R(new Vector2(-10.5f * s, -2.8f * s), rot);
        var engD = R(new Vector2(-10.5f * s, 2.8f * s), rot);
        renderer.DrawSolidRingScreen(cx + engU.X, cy + engU.Y, 1.2f * s, 2.3f * s, nozzleC);
        renderer.DrawSolidRingScreen(cx + engD.X, cy + engD.Y, 1.2f * s, 2.3f * s, nozzleC);
        renderer.DrawFilledCircleScreen(cx + engU.X, cy + engU.Y, 4.5f * s, thrustIn, thrustOut, 1.4f * s);
        renderer.DrawFilledCircleScreen(cx + engD.X, cy + engD.Y, 4.5f * s, thrustIn, thrustOut, 1.4f * s);

        // --- Cockpit ---
        var cp = R(new Vector2(7.5f * s, 0), rot);
        renderer.DrawFilledCircleScreen(cx + cp.X, cy + cp.Y, 2.4f * s, cockpitC);
        var gl = R(new Vector2(8.2f * s, -0.7f * s), rot);
        renderer.DrawFilledCircleScreen(cx + gl.X, cy + gl.Y, 0.75f * s, new Color4(255, 255, 255, 200));
    }

    /// <summary>
    /// Freighter — heavy cargo hauler.
    /// Wide rectangular hull, upper/lower cargo pods, twin large engine bell nozzles.
    /// </summary>
    private static void DrawFreighter(ISpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hullTop = new Color4(190, 165, 85, 255);
        var hullBot = new Color4(130, 110, 55, 255);
        var noseCol = new Color4(160, 150, 80, 255);
        var hullGlow = new Color4(220, 200, 130, 90);
        var podTop = new Color4(145, 120, 60, 255);
        var podBot = new Color4(100, 85, 40, 255);
        var ribCol = new Color4(100, 88, 48, 180);   // structural ribs
        var cockpitC = new Color4(145, 210, 240, 255);
        var nozzleC = new Color4(120, 100, 50, 220);
        var thrustIn = new Color4(255, 190, 70, 240);
        var thrustOut = new Color4(255, 130, 20, 0);

        // --- Main hull body (rectangular) ---
        // Top face
        Quad(renderer, cx, cy, rot, s,
            (-13, -6.5f), (8, -6.5f), (8, 0), (-13, 0), hullTop);
        // Bottom face (darker)
        Quad(renderer, cx, cy, rot, s,
            (-13, 0), (8, 0), (8, 6.5f), (-13, 6.5f), hullBot);

        // --- Nose cone ---
        Tri(renderer, cx, cy, rot, s, (8, -6.5f), (16, 0), (8, 6.5f), noseCol);

        // --- Dorsal glow strip ---
        Tri(renderer, cx, cy, rot, s, (-8, -1.5f), (10, 0), (-8, 1.5f), hullGlow);

        // --- Cargo side pods ---
        // Upper pod (top face brighter)
        Quad(renderer, cx, cy, rot, s,
            (-11, -12.5f), (3, -12.5f), (3, -6.5f), (-11, -6.5f), podTop);
        // Upper pod bottom face
        Quad(renderer, cx, cy, rot, s,
            (-11, -12.5f), (3, -12.5f), (3, -11.5f), (-11, -11.5f), podBot);
        // Lower pod
        Quad(renderer, cx, cy, rot, s,
            (-11, 6.5f), (3, 6.5f), (3, 12.5f), (-11, 12.5f), podTop);
        Quad(renderer, cx, cy, rot, s,
            (-11, 11.5f), (3, 11.5f), (3, 12.5f), (-11, 12.5f), podBot);

        // --- Hull rib lines (structural detail) ---
        DrawLineRot(renderer, cx, cy, rot, s, (-3, -6.5f), (-3, 6.5f), ribCol);
        DrawLineRot(renderer, cx, cy, rot, s, (2, -6.5f), (2, 6.5f), ribCol);

        // --- Twin engine bells ---
        var engU = R(new Vector2(-13.5f * s, -3.5f * s), rot);
        var engD = R(new Vector2(-13.5f * s, 3.5f * s), rot);
        renderer.DrawSolidRingScreen(cx + engU.X, cy + engU.Y, 1.8f * s, 3.5f * s, nozzleC);
        renderer.DrawSolidRingScreen(cx + engD.X, cy + engD.Y, 1.8f * s, 3.5f * s, nozzleC);
        renderer.DrawFilledCircleScreen(cx + engU.X, cy + engU.Y, 6f * s, thrustIn, thrustOut, 2.2f * s);
        renderer.DrawFilledCircleScreen(cx + engD.X, cy + engD.Y, 6f * s, thrustIn, thrustOut, 2.2f * s);

        // --- Wide cockpit ---
        var cp = R(new Vector2(10f * s, 0), rot);
        renderer.DrawFilledCircleScreen(cx + cp.X, cy + cp.Y, 3.0f * s, cockpitC);
        var gl = R(new Vector2(11f * s, -0.9f * s), rot);
        renderer.DrawFilledCircleScreen(cx + gl.X, cy + gl.Y, 1.0f * s, new Color4(255, 255, 255, 195));
    }

    /// <summary>
    /// Explorer — science vessel.
    /// Slender fuselage, long sensor boom, broad flat radiator panels, large ion engine.
    /// </summary>
    private static void DrawExplorer(ISpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hullTop = new Color4(85, 155, 230, 255);
        var hullBot = new Color4(55, 100, 165, 255);
        var boomCol = new Color4(100, 175, 250, 255);   // sensor boom
        var hullGlow = new Color4(160, 220, 255, 90);   // spine highlight
        var panelTop = new Color4(65, 130, 210, 255);   // radiator panel lit
        var panelBot = new Color4(45, 90, 160, 255);   // radiator panel shadow
        var panelEdge = new Color4(130, 200, 255, 150);   // panel rim light
        var sensorCol = new Color4(180, 240, 255, 255);   // sensor dish
        var cockpitC = new Color4(150, 225, 255, 255);
        var nozzleC = new Color4(60, 120, 210, 230);
        var thrustIn = new Color4(100, 200, 255, 240);
        var thrustOut = new Color4(50, 150, 255, 0);

        // --- Fuselage: narrow tapered body ---
        // Upper lit face
        Tri(renderer, cx, cy, rot, s, (-12, 0), (-7, -3f), (10, -1.5f), hullTop);
        Tri(renderer, cx, cy, rot, s, (-12, 0), (10, -1.5f), (13, 0), hullTop);
        // Lower shadow face
        Tri(renderer, cx, cy, rot, s, (-12, 0), (-7, 3f), (10, 1.5f), hullBot);
        Tri(renderer, cx, cy, rot, s, (-12, 0), (10, 1.5f), (13, 0), hullBot);

        // --- Sensor boom (extends ahead of nose) ---
        Tri(renderer, cx, cy, rot, s, (11, 0.6f), (11, -0.6f), (18, 0), boomCol);

        // --- Spine highlight ---
        Tri(renderer, cx, cy, rot, s, (-6, -0.9f), (10, 0), (-6, 0.9f), hullGlow);

        // --- Radiator panels (wide, flat, swept panels) ---
        // Upper panel — lit top face
        Quad(renderer, cx, cy, rot, s,
            (-8, -3f), (4, -3f), (2, -13f), (-9, -13f), panelTop);
        // Upper panel — shadow underside lip
        Tri(renderer, cx, cy, rot, s, (-8, -3f), (4, -3f), (3.5f, -4.5f), panelBot);
        // Upper panel rim light
        DrawLineRot(renderer, cx, cy, rot, s, (-9, -13f), (2, -13f), panelEdge);
        // Lower panel
        Quad(renderer, cx, cy, rot, s,
            (-8, 3f), (4, 3f), (2, 13f), (-9, 13f), panelTop);
        Tri(renderer, cx, cy, rot, s, (-8, 3f), (4, 3f), (3.5f, 4.5f), panelBot);
        DrawLineRot(renderer, cx, cy, rot, s, (-9, 13f), (2, 13f), panelEdge);

        // --- Sensor dish at boom tip ---
        var dish = R(new Vector2(18f * s, 0), rot);
        renderer.DrawFilledCircleScreen(cx + dish.X, cy + dish.Y, 1.6f * s, sensorCol);
        renderer.DrawSolidRingScreen(cx + dish.X, cy + dish.Y, 1.6f * s, 2.2f * s,
            sensorCol.WithAlpha(160));

        // --- Large ion engine ---
        var eng = R(new Vector2(-12.5f * s, 0), rot);
        renderer.DrawSolidRingScreen(cx + eng.X, cy + eng.Y, 2.0f * s, 3.8f * s, nozzleC);
        renderer.DrawFilledCircleScreen(cx + eng.X, cy + eng.Y, 7f * s, thrustIn, thrustOut, 2.4f * s);

        // --- Cockpit ---
        var cp = R(new Vector2(9f * s, 0), rot);
        renderer.DrawFilledCircleScreen(cx + cp.X, cy + cp.Y, 2.6f * s, cockpitC);
        var gl = R(new Vector2(9.8f * s, -0.8f * s), rot);
        renderer.DrawFilledCircleScreen(cx + gl.X, cy + gl.Y, 0.9f * s, new Color4(255, 255, 255, 210));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Shorthand: rotate a pre-normalised local vector by degrees.
    private static Vector2 R(Vector2 v, float deg)
    {
        float r = deg * (MathF.PI / 180f);
        float c = MathF.Cos(r), sn = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * sn, v.X * sn + v.Y * c);
    }

    /// Draw a triangle with integer-friendly local-space coordinates scaled by s.
    private static void Tri(ISpriteRenderer renderer, float cx, float cy, float rot, float s,
        (float x, float y) a, (float x, float y) b, (float x, float y) c, Color4 color)
    {
        var r1 = R(new Vector2(a.x * s, a.y * s), rot);
        var r2 = R(new Vector2(b.x * s, b.y * s), rot);
        var r3 = R(new Vector2(c.x * s, c.y * s), rot);
        renderer.DrawFilledTriangleScreen(
            cx + r1.X, cy + r1.Y,
            cx + r2.X, cy + r2.Y,
            cx + r3.X, cy + r3.Y, color);
    }

    /// Draw a quad (two triangles) with integer-friendly local-space coordinates.
    private static void Quad(ISpriteRenderer renderer, float cx, float cy, float rot, float s,
        (float x, float y) a, (float x, float y) b, (float x, float y) c, (float x, float y) d, Color4 color)
    {
        Tri(renderer, cx, cy, rot, s, a, b, c, color);
        Tri(renderer, cx, cy, rot, s, a, c, d, color);
    }

    /// Draw a line segment in local space.
    private static void DrawLineRot(ISpriteRenderer renderer, float cx, float cy, float rot, float s,
        (float x, float y) a, (float x, float y) b, Color4 color)
    {
        var r1 = R(new Vector2(a.x * s, a.y * s), rot);
        var r2 = R(new Vector2(b.x * s, b.y * s), rot);
        renderer.DrawLineScreen(cx + r1.X, cy + r1.Y, cx + r2.X, cy + r2.Y, color);
    }

    // Legacy vector helpers kept for any callers still using the old api internally.
    private static Vector2 Rotate(Vector2 v, float degrees) => R(v, degrees);

    private static void DrawRotatedTriangleScreen(ISpriteRenderer renderer, float cx, float cy,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Color4 color)
    {
        Tri(renderer, cx, cy, rotationDeg, 1f,
            (p1.X, p1.Y), (p2.X, p2.Y), (p3.X, p3.Y), color);
    }

    private static void DrawRotatedQuadScreen(ISpriteRenderer renderer, float cx, float cy,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, Color4 color)
    {
        DrawRotatedTriangleScreen(renderer, cx, cy, rotationDeg, p1, p2, p3, color);
        DrawRotatedTriangleScreen(renderer, cx, cy, rotationDeg, p1, p3, p4, color);
    }
}
