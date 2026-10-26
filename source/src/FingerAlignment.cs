using System;
using System.Numerics;
using MastabaInspect.Firearms;

namespace MastabaInspect;
internal static class FingerAlignment
{
    internal static void Straighten(ReadOnlySpan<Vector3> segments, ReadOnlySpan<Quaternion> rotations, Vector3 direction, float weight, Span<Quaternion> result)
    {
        direction = Vector3.Normalize(direction);
        Quaternion parentDelta = Quaternion.Identity;
        for (int index = 0; index < rotations.Length; index++)
        {
            Vector3 from = Vector3.Normalize(Vector3.Transform(segments[index], parentDelta));
            Quaternion delta = Motion.FromTo(from, direction);
            delta = Quaternion.Slerp(Quaternion.Identity, delta, weight);
            parentDelta = Quaternion.Normalize(delta * parentDelta);
            result[index] = Quaternion.Normalize(parentDelta * rotations[index]);
        }
    }

    internal static void ThumbContact(ReadOnlySpan<Vector3> points, ReadOnlySpan<Quaternion> rotations, Vector3 target, float weight, Span<Quaternion> result)
    {
        Vector3 proximal = points[1] - points[0];
        Vector3 distal = points[3] - points[1];
        Vector3 direction = target - points[0];
        float firstLength = proximal.Length();
        float secondLength = distal.Length();
        float nativeBend = MathF.Acos(Math.Clamp(Vector3.Dot(proximal, distal) / (firstLength * secondLength), -1, 1));
        float targetBend = MathF.Acos(Math.Clamp((direction.LengthSquared() - firstLength * firstLength - secondLength * secondLength) / (2 * firstLength * secondLength), -1, 1));
        targetBend = Math.Clamp(targetBend, MathF.Max(.08726646f, nativeBend - .6108652f), MathF.Max(.08726646f, MathF.Min(1.9198622f, nativeBend + .6108652f)));
        Vector3 axis = Vector3.Cross(proximal, distal);
        if (axis.LengthSquared() < 1e-12f)
        {
            axis = Vector3.Cross(proximal, direction);
            if (axis.LengthSquared() < 1e-12f)
            {
                axis = Vector3.Cross(proximal, MathF.Abs(proximal.X / firstLength) < .8f ? Vector3.UnitX : Vector3.UnitY);
            }
        }

        Quaternion bend = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), targetBend - nativeBend);
        Quaternion root = Motion.FromTo(proximal + Vector3.Transform(distal, bend), direction);
        float rootAngle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(root.W), 0, 1));
        if (rootAngle > THUMB_ROOT_LIMIT)
        {
            root = Quaternion.Slerp(Quaternion.Identity, root, THUMB_ROOT_LIMIT / rootAngle);
        }

        root = Quaternion.Slerp(Quaternion.Identity, root, weight);
        bend = Quaternion.Slerp(Quaternion.Identity, bend, weight);
        Quaternion distalRotation = Quaternion.Normalize(root * bend);
        result[0] = Quaternion.Normalize(root * rotations[0]);
        result[1] = Quaternion.Normalize(distalRotation * rotations[1]);
        result[2] = Quaternion.Normalize(distalRotation * rotations[2]);
    }

#region 常量
    private const float THUMB_ROOT_LIMIT = 65 * MathF.PI / 180;
#endregion
}
