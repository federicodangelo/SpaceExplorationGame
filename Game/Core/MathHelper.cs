namespace SpaceExplorationGame.Core;

/// <summary>
/// General-purpose math utilities shared across the game.
/// </summary>
public static class MathHelper
{
    /// <summary>
    /// Linearly interpolates between two rotation angles (in degrees), always
    /// taking the shortest arc so the result never spins more than 180°.
    /// </summary>
    public static float LerpRotation(float fromDeg, float toDeg, float t)
    {
        float delta = DiffRotation(fromDeg, toDeg);
        return fromDeg + delta * Math.Clamp(t, 0f, 1f);
    }

    /// <summary> 
    /// Calculates the difference between two rotation angles (in degrees), returning a value in the range [-180, 180]. 
    /// </summary>
    public static float DiffRotation(float fromDeg, float toDeg)
    {
        float delta = toDeg - fromDeg;
        return ((delta % 360f) + 540f) % 360f - 180f;
    }
}
