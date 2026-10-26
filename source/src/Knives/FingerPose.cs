using System;
using System.Numerics;

namespace MastabaInspect.Knives;

internal readonly ref struct FingerPose
{
    public readonly ReadOnlySpan<Vector3> Points;
    public readonly ReadOnlySpan<Quaternion> Rotations;
    public readonly float TargetError;
    public FingerPose(ReadOnlySpan<Vector3> points, ReadOnlySpan<Quaternion> rotations, float targetError)
    {
        Points = points;
        Rotations = rotations;
        TargetError = targetError;
    }
}
