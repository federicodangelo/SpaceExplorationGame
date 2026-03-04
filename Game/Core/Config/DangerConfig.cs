namespace SpaceExplorationGame.Core.Config;

static public class DangerConfig
{
    // Danger levels
    public const int MinDangerLevel = 1;
    public const int MaxDangerLevel = 5;


    static public float GetHealthMultiplier(int dangerLevel) => dangerLevel switch
    {
        1 => 0.4f,   // SAFE     – weaker hull
        2 => 0.5f,    // LOW      – slightly weaker
        3 => 0.6f,    // MEDIUM   – baseline
        4 => 0.8f,    // HIGH     – tougher hull
        5 => 1.0f,    // EXTREME  – significantly tougher
        _ => 1f,    // fallback (0 = ANY)
    };

    static public float GetDamageMultiplier(int dangerLevel) => dangerLevel switch
    {
        1 => 0.6f,
        2 => 0.8f,
        3 => 1.0f,
        4 => 1.2f,
        5 => 1.5f,
        _ => 1f
    };

    /// <summary>Aim inaccuracy radius for NPC ships (world units). Higher danger = smaller radius = more accurate.</summary>
    static public float GetInaccuracy(int dangerLevel) => dangerLevel switch
    {
        1 => 200f,   // SAFE     – enemies spray widely
        2 => 150f,   // LOW      – noticeably inaccurate
        3 => 100f,   // MEDIUM   – moderate challenge
        4 => 80f,   // HIGH     – mostly on-target
        5 => 50f,   // EXTREME  – near-perfect aim
        _ => 60f,   // fallback (0 = ANY)
    };

    /// <summary>
    /// Aim inaccuracy radius for surface (walking) NPCs (world units).
    /// Values are much smaller than ship inaccuracy because walking combat happens
    /// at close range. Higher danger level = smaller radius = more accurate enemies.
    /// </summary>
    static public float GetSurfaceAimInaccuracyRadius(int dangerLevel) => dangerLevel switch
    {
        1 => 40f,    // SAFE     – very imprecise
        2 => 30f,    // LOW      – noticeably inaccurate
        3 => 20f,    // MEDIUM   – moderate challenge
        4 => 12f,    // HIGH     – mostly on-target
        5 => 5f,    // EXTREME  – near-perfect aim
        _ => 15f,    // fallback (0 = ANY)
    };
}
