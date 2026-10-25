using System;
using System.Numerics;

namespace MastabaInspect.Firearms;
internal static class Motion
{
    internal static readonly PoseKey[] Keys =
    {
        new(0, 0, -.03f, 0, 0),
        new(.28f, .14f, -.028f, .82f, 0),
        new(.85f, .94f, -.022f, 1, 0),
        new(1.15f, 1, -.012f, 1, .65f),
        new(1.55f, 1, 0, 1, 1),
        new(2.05f, 1, .045f, 1, 1),
        new(2.95f, 1, .88f, 1, 1),
        new(3.45f, 1, 1, 1, 1),
        new(3.95f, 1, .92f, 1, 1),
        new(4.9f, 1, .07f, 1, 1),
        new(5.35f, 1, 0, 1, 1),
        new(5.7f, 1, -.015f, 1, .5f),
        new(6.1f, .82f, -.03f, .96f, 0),
        new(DURATION, 0, -.03f, 0, 0)
    };
    internal static PoseKey Sample(float time)
    {
        if (!float.IsFinite(time) || time <= 0)
        {
            return Keys[0];
        }

        if (time >= DURATION)
        {
            return Keys[^1];
        }

        int index = 0;
        while (time > Keys[index + 1].Time)
        {
            index++;
        }

        PoseKey from = Keys[index];
        PoseKey to = Keys[index + 1];
        float span = to.Time - from.Time;
        Vector4 slope = (to.Channels - from.Channels) / span;
        Vector4 start = Vector4.Zero;
        Vector4 end = Vector4.Zero;
        if (index > 0)
        {
            PoseKey previous = Keys[index - 1];
            float before = from.Time - previous.Time;
            start = Tangent((from.Channels - previous.Channels) / before, slope, before, span);
        }

        if (index + 2 < Keys.Length)
        {
            PoseKey next = Keys[index + 2];
            float after = next.Time - to.Time;
            end = Tangent(slope, (next.Channels - to.Channels) / after, span, after);
        }

        float u = (time - from.Time) / span;
        float square = u * u;
        float cube = square * u;
        Vector4 channels = (2 * cube - 3 * square + 1) * from.Channels + (cube - 2 * square + u) * span * start + (-2 * cube + 3 * square) * to.Channels + (cube - square) * span * end;
        return new PoseKey(time, channels.X, channels.Y, channels.Z, channels.W);
    }

    private static Vector4 Tangent(Vector4 before, Vector4 after, float left, float right)
    {
        float first = 2 * right + left;
        float second = right + 2 * left;
        return new Vector4(before.X * after.X <= 0 ? 0 : (first + second) / (first / before.X + second / after.X), before.Y * after.Y <= 0 ? 0 : (first + second) / (first / before.Y + second / after.Y), before.Z * after.Z <= 0 ? 0 : (first + second) / (first / before.Z + second / after.Z), before.W * after.W <= 0 ? 0 : (first + second) / (first / before.W + second / after.W));
    }

    internal static Quaternion Rotation(PoseKey sample)
    {
        Vector3 frontForward = Vector3.Normalize(new Vector3(-.96f, .16f, .20f));
        Vector3 frontRight = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, frontForward));
        Vector3 frontUp = Vector3.Cross(frontForward, frontRight);
        Quaternion front = Quaternion.CreateFromRotationMatrix(new Matrix4x4(frontRight.X, frontRight.Y, frontRight.Z, 0, frontUp.X, frontUp.Y, frontUp.Z, 0, frontForward.X, frontForward.Y, frontForward.Z, 0, 0, 0, 0, 1));
        return Quaternion.Normalize(front * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * sample.Reverse));
    }

    internal static Vector3 PresentationPivot(ReadOnlySpan<Vector3> muzzleOffsets, ViewFrame frame)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, Rotation(Keys[2]));
        Vector3 leftPlane = new(-1, 0, frame.Left);
        Vector3 rightPlane = new(1, 0, -frame.Right);
        Vector3 bottomPlane = new(0, -1, frame.Bottom);
        Vector3 topPlane = new(0, 1, -frame.Top);
        float leftAxial = Vector3.Dot(leftPlane, forward);
        float rightAxial = Vector3.Dot(rightPlane, forward);
        float bottomAxial = Vector3.Dot(bottomPlane, forward);
        float topAxial = Vector3.Dot(topPlane, forward);
        float leftRadial = MathF.Sqrt(MathF.Max(0, leftPlane.LengthSquared() - leftAxial * leftAxial));
        float rightRadial = MathF.Sqrt(MathF.Max(0, rightPlane.LengthSquared() - rightAxial * rightAxial));
        float bottomRadial = MathF.Sqrt(MathF.Max(0, bottomPlane.LengthSquared() - bottomAxial * bottomAxial));
        float topRadial = MathF.Sqrt(MathF.Max(0, topPlane.LengthSquared() - topAxial * topAxial));
        float depthRadial = MathF.Sqrt(MathF.Max(0, 1 - forward.Z * forward.Z));
        float left = float.NegativeInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;
        float top = float.NegativeInfinity;
        float near = float.PositiveInfinity;
        foreach (Vector3 point in muzzleOffsets)
        {
            float radius = MathF.Sqrt(point.X * point.X + point.Y * point.Y);
            left = MathF.Max(left, point.Z * leftAxial + radius * leftRadial);
            right = MathF.Max(right, point.Z * rightAxial + radius * rightRadial);
            bottom = MathF.Max(bottom, point.Z * bottomAxial + radius * bottomRadial);
            top = MathF.Max(top, point.Z * topAxial + radius * topRadial);
            near = MathF.Min(near, point.Z * forward.Z - radius * depthRadial);
        }

        float depth = MathF.Max(.45f, frame.Near - near);
        depth = MathF.Max(depth, (left + right) / (frame.Right - frame.Left));
        depth = MathF.Max(depth, (bottom + top) / (frame.Top - frame.Bottom)) + .001f;
        return new Vector3(((frame.Left + frame.Right) * depth + left - right) * .5f, ((frame.Bottom + frame.Top) * depth + bottom - top) * .5f, depth);
    }

    internal static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        Vector3 cross = Vector3.Cross(from, to);
        if (dot < -.999999f)
        {
            float length = cross.Length();
            if (length > .000001f)
            {
                return Quaternion.CreateFromAxisAngle(cross / length, MathF.Atan2(length, dot));
            }

            Vector3 axis = Vector3.Cross(from, MathF.Abs(from.X) < .8f ? Vector3.UnitX : Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }

        return Quaternion.Normalize(new Quaternion(cross, 1 + dot));
    }

    internal static ArmPose SolveArm(Vector3 shoulder, Vector3 elbow, Vector3 hand, Quaternion upperRotation, Quaternion lowerRotation, Quaternion handRotation, Vector3 target, Quaternion rotation, ref float forearmTwist)
    {
        float upper = Vector3.Distance(shoulder, elbow);
        float lower = Vector3.Distance(elbow, hand);
        Vector3 direction = Vector3.Normalize(target - shoulder);
        float distance = Math.Clamp(Vector3.Distance(target, shoulder), MathF.Abs(upper - lower) + .00001f, (upper + lower) * .9995f);
        Vector3 targetHand = shoulder + direction * distance;
        Vector3 pole = elbow - shoulder;
        pole -= direction * Vector3.Dot(pole, direction);
        if (pole.LengthSquared() < 1e-9f)
        {
            pole = Vector3.Cross(direction, MathF.Abs(direction.Y) < .8f ? Vector3.UnitY : Vector3.UnitX);
        }

        pole = Vector3.Normalize(pole);
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        float height = MathF.Sqrt(MathF.Max(0, upper * upper - along * along));
        Vector3 targetElbow = shoulder + direction * along + pole * height;
        Quaternion upperDelta = FromTo(elbow - shoulder, targetElbow - shoulder);
        Quaternion lowerDelta = FromTo(hand - elbow, targetHand - targetElbow);
        Quaternion desiredHand = Quaternion.Normalize(rotation * handRotation);
        Quaternion wristDelta = Quaternion.Normalize(desiredHand * Quaternion.Inverse(lowerDelta * handRotation));
        Vector3 forearmAxis = Vector3.Normalize(targetHand - targetElbow);
        Vector3 twistVector = forearmAxis * Vector3.Dot(new Vector3(wristDelta.X, wristDelta.Y, wristDelta.Z), forearmAxis);
        if (twistVector.LengthSquared() + wristDelta.W * wristDelta.W > 1e-10f)
        {
            float angle = 2 * MathF.Atan2(Vector3.Dot(twistVector, forearmAxis), wristDelta.W);
            angle += 2 * MathF.PI * MathF.Round((forearmTwist - angle) / (2 * MathF.PI));
            forearmTwist = angle;
        }

        lowerDelta = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(forearmAxis, forearmTwist * .7f) * lowerDelta);
        return new ArmPose(shoulder, targetElbow, targetHand, Quaternion.Normalize(upperDelta * upperRotation), Quaternion.Normalize(lowerDelta * lowerRotation), desiredHand);
    }

#region 常量
    internal const float DURATION = 6.8f;
#endregion
}
