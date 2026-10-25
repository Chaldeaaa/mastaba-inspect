namespace MastabaInspect.Knives;

internal readonly struct MotionSample
{
    public readonly float PoseB, UprightA;
    public float ForearmRoll => 180 * PoseB;
    public float Weight => PoseB + UprightA;

    public MotionSample(float poseB, float uprightA) => (PoseB, UprightA) = (poseB, uprightA);
}
