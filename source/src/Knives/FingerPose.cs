using System.Numerics;

namespace MastabaInspect.Knives;

internal readonly struct FingerPose
{
    public readonly Vector3[] Points;
    public readonly Quaternion[] Rotations;
    public readonly float TargetError;
    public FingerPose(Vector3[] points, Quaternion[] rotations, float targetError) => (Points, Rotations, TargetError) = (points, rotations, targetError);
}
