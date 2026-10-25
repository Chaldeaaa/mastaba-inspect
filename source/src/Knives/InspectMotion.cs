using System;
using System.Numerics;

namespace MastabaInspect.Knives;

internal static class InspectMotion
{
    private static readonly float[] Times =
    {
        0,
        .9f,
        1.5f,
        2.4f,
        3.2f,
        4.1f,
        4.3f,
        DURATION
    };
    private static readonly MotionSample[] Keys =
    {
        new(0, 0),
        new(1, 0),
        new(1, 0),
        new(0, 1),
        new(0, 1),
        new(1, 0),
        new(1, 0),
        new(0, 0)
    };
    public static MotionSample Sample(float time)
    {
        if (!float.IsFinite(time) || time <= 0)
            return Keys[0];
        if (time >= DURATION)
            return Keys[^1];
        int i = 0;
        while (time > Times[i + 1])
            i++;
        float u = (time - Times[i]) / (Times[i + 1] - Times[i]);
        u = u * u * (3 - 2 * u);
        return new(Keys[i].PoseB + (Keys[i + 1].PoseB - Keys[i].PoseB) * u, Keys[i].UprightA + (Keys[i + 1].UprightA - Keys[i].UprightA) * u);
    }

    public static float LiftRollSign(Vector3 forearm, Vector3 blade, Vector3 up) => Vector3.Dot(Vector3.Cross(forearm, blade), up) >= 0 ? 1 : -1;
    private static Vector3 Unit(Vector3 v, Vector3 fallback) => v.LengthSquared() < 1e-10f ? fallback : Vector3.Normalize(v);
    private static Quaternion Turn(Vector3 axis, float degrees) => Quaternion.CreateFromAxisAngle(Unit(axis, Vector3.UnitZ), degrees * MathF.PI / 180);
    private static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Unit(from, Vector3.UnitZ);
        to = Unit(to, from);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        if (dot > 0 && Vector3.Cross(from, to).LengthSquared() < 1e-16f)
            return Quaternion.Identity;
        if (dot < -.999999f)
            return Turn(Vector3.Cross(from, Math.Abs(from.X) < .8f ? Vector3.UnitX : Vector3.UnitY), 180);
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }

    public static Quaternion DisplayBladeRotation(bool faceB, float faceSign)
    {
        var z = Vector3.Normalize(faceB ? new Vector3(.32f, .945f, .06f) : new Vector3(0, 1, .04f));
        var facing = -Vector3.UnitZ * faceSign * (faceB ? -1 : 1);
        var x = Vector3.Normalize(facing - z * Vector3.Dot(facing, z));
        var y = Vector3.Cross(z, x);
        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
    }

    private static Quaternion PresentationDelta(bool faceB, float faceSign, Quaternion bladeRotation, Quaternion cameraRotation, Vector3 forearm)
    {
        var target = cameraRotation * DisplayBladeRotation(faceB, faceSign);
        var delta = target * Quaternion.Inverse(bladeRotation);
        var tip = Vector3.Transform(Vector3.UnitZ, target);
        var arm = Vector3.Transform(forearm, delta);
        var forward = Vector3.Transform(Vector3.UnitZ, cameraRotation);
        float c = Vector3.Dot(arm - tip * Vector3.Dot(arm, tip), forward);
        float s = Vector3.Dot(Vector3.Cross(tip, arm), forward);
        float oblique = Math.Clamp(MathF.Atan2(s, c) * 180 / MathF.PI, -55, 55);
        return Quaternion.Normalize(Turn(tip, oblique) * delta);
    }

    public static ArmPose PoseArm(Vector3 shoulder, Vector3 elbow, Vector3 hand, Quaternion shoulderRotation, Quaternion elbowRotation, Quaternion handRotation, Quaternion bladeRotation, Quaternion cameraRotation, Vector3 thumbDirection, MotionSample sample, float rollSign, float faceSign)
    {
        if (sample.Weight <= 0)
            return new(shoulder, elbow, hand, shoulderRotation, elbowRotation, handRotation);
        var arm = Unit(hand - elbow, Vector3.UnitZ);
        var bDelta = PresentationDelta(true, faceSign, bladeRotation, cameraRotation, arm);
        var aDelta = PresentationDelta(false, faceSign, bladeRotation, cameraRotation, arm);
        var bSwing = Quaternion.Normalize(bDelta * Quaternion.Inverse(Turn(arm, 180 * rollSign)));
        var swing = Quaternion.Slerp(bSwing, aDelta, sample.UprightA / sample.Weight);
        swing = Quaternion.Slerp(Quaternion.Identity, swing, sample.Weight);
        var handDelta = Quaternion.Normalize(swing * Turn(arm, sample.ForearmRoll * rollSign));
        var finalHandRotation = Quaternion.Normalize(handDelta * handRotation);
        var armDirection = Vector3.Transform(arm, handDelta);
        var thumb = Vector3.Transform(Unit(thumbDirection, arm), handDelta);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(armDirection, thumb), -1, 1)) * 180 / MathF.PI;
        var wristCorrection = Quaternion.Slerp(Quaternion.Identity, FromTo(armDirection, thumb), sample.UprightA * (angle > 0 ? Math.Min(1, MAX_WRIST_ADJUSTMENT / angle) : 0));
        var forearmDelta = Quaternion.Normalize(wristCorrection * handDelta);
        var offset = new Vector3(-.080f, .020f, 0) * sample.PoseB + new Vector3(-.095f, .025f, .005f) * sample.UprightA;
        var finalHand = hand + Vector3.Transform(offset, cameraRotation);
        var finalElbow = finalHand - Vector3.Transform(hand - elbow, forearmDelta);
        var upperDirection = Unit(finalElbow - shoulder, Unit(elbow - shoulder, -Vector3.UnitY));
        var finalShoulder = finalElbow - upperDirection * Vector3.Distance(elbow, shoulder);
        var upperDelta = FromTo(elbow - shoulder, upperDirection);
        return new(finalShoulder, finalElbow, finalHand, Quaternion.Normalize(upperDelta * shoulderRotation), Quaternion.Normalize(forearmDelta * elbowRotation), finalHandRotation);
    }

    public static (Vector3 Position, Quaternion Rotation) FollowHand(Vector3 itemPosition, Quaternion itemRotation, Vector3 nativeHandPosition, Quaternion nativeHandRotation, Vector3 posedHandPosition, Quaternion posedHandRotation)
    {
        var delta = Quaternion.Normalize(posedHandRotation * Quaternion.Inverse(nativeHandRotation));
        return (posedHandPosition + Vector3.Transform(itemPosition - nativeHandPosition, delta), Quaternion.Normalize(delta * itemRotation));
    }

    public static float GripRollWeight(MotionSample sample)
    {
        float u = Math.Clamp((sample.UprightA - .12f) / .78f, 0, 1);
        return u * u * (3 - 2 * u);
    }

    public static float FaceRollAngle(Vector3 axis, Vector3 aFace, Vector3 towardCamera)
    {
        axis = Unit(axis, Vector3.UnitZ);
        var from = aFace - axis * Vector3.Dot(aFace, axis);
        var to = towardCamera - axis * Vector3.Dot(towardCamera, axis);
        if (from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f)
            return 0;
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        return Math.Clamp(MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to)) * 180 / MathF.PI, -100, 100);
    }

    public static (Vector3 Position, Quaternion Rotation) RollInGrip(Vector3 position, Quaternion rotation, Vector3 pivot, Vector3 axis, float degrees)
    {
        var delta = Turn(axis, degrees);
        return (pivot + Vector3.Transform(position - pivot, delta), Quaternion.Normalize(delta * rotation));
    }

    public static Vector3 FingerTarget(Vector3 tip, Vector3 pivot, Vector3 axis, float rollDegrees, MotionSample sample, int finger)
    {
        if (sample.UprightA <= 0)
            return tip;
        axis = Unit(axis, Vector3.UnitZ);
        var radial = tip - pivot;
        radial -= axis * Vector3.Dot(radial, axis);
        float roll = GripRollWeight(sample);
        float fraction = finger == 1 ? .35f : 0;
        float opening = (finger == 1 ? .0015f : .004f) * 4 * sample.UprightA * (1 - sample.UprightA) + (finger == 1 ? .0005f : .0015f) * sample.UprightA;
        var turn = Turn(axis, rollDegrees * roll * fraction);
        return pivot + Vector3.Transform(tip - pivot, turn) + Vector3.Transform(Unit(radial, Vector3.UnitX) * opening, turn);
    }

    public static Vector3 ThumbTarget(Vector3 tip, Vector3 thumbBase, Vector3 pivot, Vector3 axis, Vector3 sideAxis, float halfWidth, float rollDegrees, MotionSample sample)
    {
        if (sample.UprightA <= 0)
        {
            return tip;
        }

        axis = Vector3.Normalize(axis);
        sideAxis = Vector3.Normalize(sideAxis - axis * Vector3.Dot(sideAxis, axis));
        var endpointSide = Vector3.Transform(sideAxis, Turn(axis, rollDegrees * (1 - GripRollWeight(sample))));
        float side = Vector3.Dot(tip - pivot, endpointSide);
        if (Math.Abs(side) < .0001f)
        {
            side = Vector3.Dot(thumbBase - pivot, endpointSide);
        }

        float sideDistance = MathF.CopySign(halfWidth + .010f, side);
        var sideTarget = tip + sideAxis * (sideDistance - Vector3.Dot(tip - pivot, sideAxis));
        float release = Math.Clamp(sample.UprightA / .32f, 0, 1);
        release *= release * (3 - 2 * release);
        return Vector3.Lerp(tip, sideTarget, release);
    }

    public static FingerPose PoseFinger(Vector3[] nativePoints, Quaternion[] nativeRotations, Vector3 target, bool thumb)
    {
        int count = nativeRotations.Length;
        if (count != 3 || nativePoints.Length != count + 1)
            throw new ArgumentException("A finger requires three joints and one fingertip.");
        var points = (Vector3[])nativePoints.Clone();
        var rotations = (Quaternion[])nativeRotations.Clone();
        var hinges = new Vector3[count];
        var bend = new float[count];
        var curl = new float[count];
        for (int i = 1; i < count; i++)
        {
            var incoming = Unit(points[i] - points[i - 1], Vector3.UnitZ);
            var outgoing = Unit(points[i + 1] - points[i], incoming);
            hinges[i] = Unit(Vector3.Cross(incoming, outgoing), Unit(Vector3.Cross(incoming, target - points[i]), Vector3.UnitX));
            bend[i] = MathF.Acos(Math.Clamp(Vector3.Dot(incoming, outgoing), -1, 1)) * 180 / MathF.PI;
        }

        for (int iteration = 0; iteration < 10; iteration++)
        {
            if (Vector3.DistanceSquared(points[count], target) < 2.5e-9f)
                break;
            for (int i = count - 1; i >= 0; i--)
            {
                Quaternion delta;
                if (i == 0)
                {
                    var step = FromTo(points[count] - points[i], target - points[i]);
                    float angle = QuaternionAngle(step);
                    step = Quaternion.Slerp(Quaternion.Identity, step, angle > 8 ? 8 / angle : 1);
                    var total = Quaternion.Normalize(step * rotations[0] * Quaternion.Inverse(nativeRotations[0]));
                    angle = QuaternionAngle(total);
                    float limit = thumb ? 28 : 16;
                    total = Quaternion.Slerp(Quaternion.Identity, total, angle > limit ? limit / angle : 1);
                    delta = Quaternion.Normalize(total * nativeRotations[0] * Quaternion.Inverse(rotations[0]));
                }
                else
                {
                    var parentDelta = rotations[i - 1] * Quaternion.Inverse(nativeRotations[i - 1]);
                    var hinge = Vector3.Transform(hinges[i], parentDelta);
                    float step = FaceRollAngle(hinge, points[count] - points[i], target - points[i]);
                    float low = -Math.Min(25, Math.Max(0, bend[i] - 3));
                    float high = Math.Min(25, Math.Max(0, 155 - bend[i]));
                    float next = Math.Clamp(curl[i] + Math.Clamp(step, -8, 8), low, high);
                    delta = Turn(hinge, next - curl[i]);
                    curl[i] = next;
                }

                for (int j = i + 1; j <= count; j++)
                    points[j] = points[i] + Vector3.Transform(points[j] - points[i], delta);
                for (int j = i; j < count; j++)
                    rotations[j] = Quaternion.Normalize(delta * rotations[j]);
            }
        }

        return new(points, rotations, Vector3.Distance(points[count], target));
    }

    private static float QuaternionAngle(Quaternion q) => 2 * MathF.Acos(Math.Clamp(Math.Abs(Quaternion.Normalize(q).W), 0, 1)) * 180 / MathF.PI;
#region 常量
    public const float DURATION = 5.2f;
    public const float MAX_WRIST_ADJUSTMENT = 25;
#endregion
}
