using System;
using System.Numerics;

namespace MastabaInspect.Knives;

internal readonly struct KnifeProfile
{
    internal static readonly KnifeProfile[] Supported =
    {
        new(53, "Mastaba Fixed Blade", false, Vector3.UnitZ, Vector3.UnitX),
        new(68, "WOX Compact", true, Vector3.UnitY, Vector3.UnitZ)
    };
    internal readonly uint OfflineID;
    internal readonly string Name;
    internal readonly bool IntegratedBlade;
    internal readonly Vector3 Forward;
    internal readonly Vector3 Right;
    internal readonly Quaternion FrameRotation;
    private KnifeProfile(uint offlineID, string name, bool integratedBlade, Vector3 forward, Vector3 right)
    {
        OfflineID = offlineID;
        Name = name;
        IntegratedBlade = integratedBlade;
        Forward = forward;
        Right = right;
        var up = Vector3.Cross(forward, right);
        FrameRotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1));
    }

    internal (Vector3 Center, float HalfWidth) MeasureGrip(ReadOnlySpan<Vector3> vertices, Vector3 grip)
    {
        if (vertices.IsEmpty)
            throw new ArgumentException("Knife mesh has no vertices.", nameof(vertices));
        float gripHeight = Vector3.Dot(grip, Forward);
        float nearest = float.PositiveInfinity;
        foreach (var point in vertices)
            nearest = Math.Min(nearest, Math.Abs(Vector3.Dot(point, Forward) - gripHeight));
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var point in vertices)
        {
            if (Math.Abs(Vector3.Dot(point, Forward) - gripHeight) > nearest + .015f)
                continue;
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        var center = (min + max) * .5f;
        center += Forward * (gripHeight - Vector3.Dot(center, Forward));
        return (center, Math.Abs(Vector3.Dot((max - min) * .5f, Right)));
    }
}
