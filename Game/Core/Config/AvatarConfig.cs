namespace SpaceExplorationGame.Core.Config;

public static class AvatarConfig
{
    // Avatar
    public const float AvatarBaseWalkSpeed = 200f;   // pixels/sec
    public const float AvatarBaseMaxHealth = 100f;

    // Planet Vehicle
    public const float VehicleAcceleration = 300f;    // pixels/sec^2
    public const float VehicleMaxSpeed = 600f;        // pixels/sec (3x avatar)
    public const float VehicleRotationSpeed = 150f;   // degrees/sec
    public const float VehicleFriction = 0.98f;       // per-frame velocity damping
    public const float VehicleBrakeMultiplier = 0.92f; // brake damping per frame
    public const float VehicleMountRadius = 35f;      // distance to mount/dismount
}
