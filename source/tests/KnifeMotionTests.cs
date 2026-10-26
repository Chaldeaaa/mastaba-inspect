using System;
using System.Numerics;
using MastabaInspect.Knives;
using System.Text.Json;

internal sealed class KnifeMotionTests
{
    private int assertions;
    private long fingerSolverAllocatedBytes;
    private void Check(bool ok, string why)
    {
        assertions++;
        if (!ok)
            throw new Exception(why);
    }

    private bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 3e-5f;
    private bool Near(Quaternion a, Quaternion b) => Math.Abs(Quaternion.Dot(a, b)) > .99999f;
    private float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0, 1)) * 180 / MathF.PI;
    public static int Main()
    {
        return new KnifeMotionTests().Run();
    }

    private int Run()
    {
        try
        {
            KnifeProfiles();
            Sequence();
            Rig();
            GripRoll();
            ThumbTargets();
            Fingers();
            Session();
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, assertions, fingerSolverAllocatedBytes, inGameTested = false }));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private void KnifeProfiles()
    {
        Check(KnifeProfile.Supported.Length == 2, "Exactly two supported knives");
        var mastaba = KnifeProfile.Supported[0];
        var wox = KnifeProfile.Supported[1];
        Check(mastaba.OfflineID == 53 && !mastaba.IntegratedBlade, "Mastaba uses separate blade and handle");
        Check(wox.OfflineID == 68 && wox.IntegratedBlade, "WOX uses an integrated knife mesh");
        Check(Near(mastaba.FrameRotation, Quaternion.Identity), "Mastaba frame is unchanged");
        Check(Near(Vector3.Transform(Vector3.UnitZ, wox.FrameRotation), Vector3.UnitY), "WOX tip follows mesh +Y");
        Check(Near(Vector3.Transform(Vector3.UnitX, wox.FrameRotation), Vector3.UnitZ), "WOX broad face follows mesh +Z");
        var mastabaMeshRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);
        var woxMeshRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
        Check(Near(Vector3.Transform(Vector3.UnitZ, mastabaMeshRotation * mastaba.FrameRotation), Vector3.Transform(Vector3.UnitZ, woxMeshRotation * wox.FrameRotation)), "Both prefab tips share the same held direction");
        Check(Near(Vector3.Transform(Vector3.UnitX, mastabaMeshRotation * mastaba.FrameRotation), -Vector3.Transform(Vector3.UnitX, woxMeshRotation * wox.FrameRotation)), "WOX face polarity is captured independently");
        Vector3[] vertices =
        {
            new(-.015f, -.11f, -.012f),
            new(.025f, -.11f, .014f),
            new(-.015f, -.13f, -.012f),
            new(.025f, -.13f, .014f),
            new(-.046f, -.02f, -.0213f),
            new(.037f, -.02f, .0213f),
            new(-.026f, .222f, -.005f),
            new(.026f, .222f, .005f),
            new(-.033f, -.221f, -.014f),
            new(.033f, -.221f, .014f)
        };
        var grip = new Vector3(.02f, -.12f, .018f);
        var section = wox.MeasureGrip(vertices, grip);
        Check(Near(section.Center, new Vector3(.005f, -.12f, .001f)), "Grip pivot uses the handle cross-section rather than full knife bounds");
        Check(Math.Abs(section.HalfWidth - .013f) < 1e-6f, "Blade, guard and pommel do not enlarge the finger clearance");
        var reversed = (Vector3[])vertices.Clone();
        Array.Reverse(reversed);
        var reversedSection = wox.MeasureGrip(reversed, grip);
        Check(Near(section.Center, reversedSection.Center) && section.HalfWidth == reversedSection.HalfWidth, "Mesh vertex order does not change the grip");
        var shifted = Array.ConvertAll(vertices, p => p + new Vector3(.1f, .2f, -.3f));
        var shiftedSection = wox.MeasureGrip(shifted, grip + new Vector3(.1f, .2f, -.3f));
        Check(Near(shiftedSection.Center, section.Center + new Vector3(.1f, .2f, -.3f)) && Math.Abs(shiftedSection.HalfWidth - section.HalfWidth) < 1e-6f, "Grip measurement follows mesh-origin changes");
        bool rejected = false;
        try
        {
            wox.MeasureGrip(Array.Empty<Vector3>(), grip);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Check(rejected, "An empty integrated mesh cannot produce a grip");
    }

    private void Sequence()
    {
        foreach (var t in new[]
        {
            -1,
            0,
            InspectMotion.DURATION,
            100,
            float.NaN
        }

        )
            Check(InspectMotion.Sample(t).Weight == 0, "Native rest at endpoints");
        Check(InspectMotion.Sample(1.2f).ForearmRoll == 180, "First turn completes full outward roll");
        Check(InspectMotion.Sample(2.8f).ForearmRoll == 0 && InspectMotion.Sample(2.8f).UprightA == 1, "Second turn reverses fully into upright A");
        Check(InspectMotion.Sample(4.2f).ForearmRoll == 180, "Third turn returns to B");
        foreach (var pair in new[]
        {
            (0f, .9f),
            (1.5f, 2.4f),
            (3.2f, 4.1f),
            (4.3f, 5.2f)
        }

        )
        {
            float direction = pair.Item1 == 0 || pair.Item1 == 3.2f ? 1 : -1;
            var previous = InspectMotion.Sample(pair.Item1).ForearmRoll;
            for (int i = 1; i <= 100; i++)
            {
                var current = InspectMotion.Sample(pair.Item1 + (pair.Item2 - pair.Item1) * i / 100).ForearmRoll;
                Check((current - previous) * direction >= -.0001f, "Rotation direction matches phase");
                previous = current;
            }
        }

        for (int i = 0; i <= 100; i++)
        {
            float u = i / 100f;
            var first = InspectMotion.Sample(.9f * u);
            var last = InspectMotion.Sample(5.2f - .9f * u);
            Check(Math.Abs(first.PoseB - last.PoseB) < 2e-6f && Math.Abs(first.UprightA - last.UprightA) < 2e-6f, "2->1 reverses 1->2");
            var second = InspectMotion.Sample(1.5f + .9f * u);
            var third = InspectMotion.Sample(4.1f - .9f * u);
            Check(Math.Abs(second.PoseB - third.PoseB) < 2e-6f && Math.Abs(second.UprightA - third.UprightA) < 2e-6f, "3->2 reverses 2->3");
        }
    }

    private void Rig()
    {
        var s = new Vector3(.2f, -.3f, -.12f);
        var e = new Vector3(.23f, -.44f, .12f);
        var h = new Vector3(.16f, -.28f, .39f);
        var sq = Quaternion.CreateFromYawPitchRoll(.4f, .8f, -.1f);
        var eq = Quaternion.CreateFromYawPitchRoll(1.2f, .1f, .4f);
        var hq = Quaternion.CreateFromYawPitchRoll(.5f, .3f, -.2f);
        var z = Vector3.Normalize(new Vector3(-.88f, .25f, .4f));
        var x = new Vector3(0, .84f, -.53f);
        x = Vector3.Normalize(x - z * Vector3.Dot(x, z));
        var y = Vector3.Cross(z, x);
        var bq = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
        var arm = Vector3.Normalize(h - e);
        var thumb = Vector3.Transform(arm, Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(arm, Vector3.UnitX)), 20 * MathF.PI / 180));
        var nativeWrist = Quaternion.Inverse(eq) * hq;
        var upperOffset = Vector3.Transform(e - s, Quaternion.Inverse(sq));
        var wristOffset = Vector3.Transform(h - e, Quaternion.Inverse(eq));
        var itemPosition = h + new Vector3(-.07f, .01f, .03f);
        var itemRotation = bq;
        var itemOffset = Vector3.Transform(itemPosition - h, Quaternion.Inverse(hq));
        var itemRelative = Quaternion.Inverse(hq) * itemRotation;
        var cameraQ = Quaternion.CreateFromYawPitchRoll(1.1f, -.6f, .2f);
        var origin = new Vector3(12, 3, -8);
        Vector3 World(Vector3 p) => origin + Vector3.Transform(p, cameraQ);
        ArmPose Pose(MotionSample sample) => InspectMotion.PoseArm(s, e, h, sq, eq, hq, bq, Quaternion.Identity, thumb, sample, -1, 1);
        var previous = Pose(InspectMotion.Sample(0));
        float maxShoulderTravel = 0, maxWrist = 0, maxTime = 0, minFirstTipY = 1, minSecondTipY = 1;
        for (int i = 0; i <= 312; i++)
        {
            var sample = InspectMotion.Sample(i / 60f);
            var pose = Pose(sample);
            Check(Math.Abs(Vector3.Distance(pose.ElbowPosition, pose.ShoulderPosition) - Vector3.Distance(e, s)) < 1e-5f, "Upper arm length preserved");
            Check(Math.Abs(Vector3.Distance(pose.HandPosition, pose.ElbowPosition) - Vector3.Distance(h, e)) < 1e-5f, "Forearm length preserved");
            Check(Near(pose.ElbowPosition, pose.ShoulderPosition + Vector3.Transform(upperOffset, pose.ShoulderRotation)), "Upper FK matches live transform hierarchy");
            Check(Near(pose.HandPosition, pose.ElbowPosition + Vector3.Transform(wristOffset, pose.ElbowRotation)), "Forearm FK matches live transform hierarchy");
            float wrist = Angle(Quaternion.Inverse(pose.ElbowRotation) * pose.HandRotation, nativeWrist);
            maxWrist = Math.Max(wrist, maxWrist);
            Check(wrist < InspectMotion.MAX_WRIST_ADJUSTMENT + .05f, "Wrist adjustment bounded");
            Check(Vector3.Distance(pose.HandPosition, h) < .12f, "Presentation hand stays within 12cm of native position");
            Check(Vector3.Distance(pose.HandPosition, previous.HandPosition) < .01f, "Smooth hand path");
            Check(Angle(pose.HandRotation, previous.HandRotation) < 10, "Smooth orientation across phase boundaries");
            var item = InspectMotion.FollowHand(itemPosition, itemRotation, h, hq, pose.HandPosition, pose.HandRotation);
            var stepTip = Vector3.Transform(Vector3.UnitZ, item.Rotation);
            if (i / 60f <= .9f)
                minFirstTipY = Math.Min(minFirstTipY, stepTip.Y);
            if (i / 60f >= 1.5f && i / 60f <= 2.4f)
                minSecondTipY = Math.Min(minSecondTipY, stepTip.Y);
            Check(Near(Vector3.Transform(item.Position - pose.HandPosition, Quaternion.Inverse(pose.HandRotation)), itemOffset), "Knife stays in grip");
            Check(Near(Quaternion.Inverse(pose.HandRotation) * item.Rotation, itemRelative), "Knife relative rotation preserved");
            var moved = InspectMotion.PoseArm(World(s), World(e), World(h), cameraQ * sq, cameraQ * eq, cameraQ * hq, cameraQ * bq, cameraQ, Vector3.Transform(thumb, cameraQ), sample, -1, 1);
            Check(Near(moved.HandPosition, World(pose.HandPosition)) && Near(moved.ShoulderPosition, World(pose.ShoulderPosition)), "Camera heading equivariance");
            Check(Near(moved.HandRotation, cameraQ * pose.HandRotation), "Camera rotation equivariance");
            if (Vector3.Distance(pose.ShoulderPosition, s) > maxShoulderTravel)
            {
                maxShoulderTravel = Vector3.Distance(pose.ShoulderPosition, s);
                maxTime = i / 60f;
            }

            previous = pose;
        }

        foreach (float time in new[]
        {
            0,
            1.2f,
            2.8f,
            4.2f,
            5.2f
        }

        )
        {
            var pose = Pose(InspectMotion.Sample(time));
            var blade = pose.HandRotation * Quaternion.Inverse(hq) * bq;
            float facing = Vector3.Dot(Vector3.Transform(Vector3.UnitX, blade), -Vector3.UnitZ);
            var tip = Vector3.Transform(Vector3.UnitZ, blade);
            Console.WriteLine($"Pose {time:F2}: shoulderDelta={Vector3.Distance(pose.ShoulderPosition, s):F3}; forearm={Vector3.Normalize(pose.HandPosition - pose.ElbowPosition)}; tip={tip}");
            bool b = time == 1.2f || time == 4.2f;
            Check(b ? facing < -.5f : facing > .5f, "Actual blade sides follow A/B/A/B/A");
            if (b)
                Check(tip.X > .25f && tip.Y > .9f, "B tip points upper right");
            if (time == 2.8f)
            {
                Check(Math.Abs(tip.X) < .01f && tip.Y > .99f, "Upright A tip points up");
                var posedThumb = Vector3.Transform(thumb, pose.HandRotation * Quaternion.Inverse(hq));
                Check(Vector3.Dot(Vector3.Normalize(posedThumb), Vector3.Normalize(pose.HandPosition - pose.ElbowPosition)) > .9999f, "Upright A aligns thumb and forearm within wrist range");
            }

            if (time == 0 || time == 5.2f)
                Check(Near(pose.ShoulderPosition, s) && Near(pose.HandPosition, h) && Near(pose.HandRotation, hq), "All endpoints restore native pose");
        }

        foreach (float face in new[]
        {
            -1f,
            1f
        }

        )
            foreach (bool b in new[]
            {
                false,
                true
            }

            )
            {
                var q = InspectMotion.DisplayBladeRotation(b, face);
                float dot = Vector3.Dot(Vector3.Transform(Vector3.UnitX * face, q), -Vector3.UnitZ);
                Check(b ? dot < -.9f : dot > .9f, "Native-side-aware A/B assignment");
            }

        var p2 = Pose(new MotionSample(1, 0));
        var p3 = Pose(new MotionSample(0, 1));
        Check(Vector3.Distance(p2.HandPosition, p3.HandPosition) < .025f, "Second turn has a smaller position excursion");
        Check(maxShoulderTravel < .16f, "Reference rig shoulder motion stays within presentation range");
        Check(minFirstTipY > .24f, "First turn sweeps the blade upward");
        Check(minSecondTipY > .9f, "Second turn keeps the tip upright while changing faces");
        Console.WriteLine($"Minimum tip Y: first turn={minFirstTipY:F3}; second turn={minSecondTipY:F3}");
        Console.WriteLine($"Synthetic rig: maximum shoulder translation {maxShoulderTravel:F3} m at {maxTime:F2}s; maximum wrist adjustment {maxWrist:F1} degrees.");
    }

    private void Session()
    {
        var s = new InspectSession();
        s.Tick(10, true, false);
        Check(!s.Active, "Blocked start");
        s.Tick(10, false, true);
        Check(!s.Active, "No automatic start");
        s.Tick(10, true, true);
        s.Tick(11, true, true);
        Check(s.Active && s.Elapsed == 1, "Repeated key does not restart");
        s.Tick(11.1f, false, false);
        Check(!s.Active, "Immediate interruption");
        s.Tick(11.2f, false, true);
        Check(!s.Active, "No automatic resume");
        s.Tick(20, true, true);
        s.Tick(26, false, true);
        Check(!s.Active, "Timed completion");
        foreach (float t in new[]
        {
            .1f,
            1.2f,
            2.8f,
            3.6f,
            4.2f,
            5.1f
        }

        )
        {
            s.Tick(0, true, true);
            s.Tick(t, false, true);
            s.Stop();
            Check(!s.Active && s.Elapsed == 0, "Stop every phase");
        }
    }

    private void GripRoll()
    {
        var pivot = new Vector3(.22f, -.2f, .4f);
        var axis = Vector3.Normalize(new Vector3(0, 1, .04f));
        var face = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
        var camera = new Vector3(0, 0, 0);
        var cameraQ = Quaternion.CreateFromYawPitchRoll(1.2f, -.8f, .5f);
        foreach (float tilt in new[]
        {
            -55f,
            55f
        }

        )
        {
            var blade = InspectMotion.RollInGrip(pivot, Quaternion.Identity, pivot, axis, tilt);
            var normal = Vector3.Transform(face, blade.Rotation);
            float degrees = InspectMotion.FaceRollAngle(axis, normal, camera - pivot);
            var correction = InspectMotion.RollInGrip(pivot, blade.Rotation, pivot, axis, degrees);
            var correctedFace = Vector3.Transform(face, correction.Rotation);
            var projectedView = camera - pivot;
            projectedView -= axis * Vector3.Dot(projectedView, axis);
            Check(Vector3.Dot(correctedFace, Vector3.Normalize(projectedView)) > .9999f, "A face follows perspective view, including lateral screen offset");
            Check(Math.Abs(degrees) <= 100, "Grip roll stays within calibrated range");
            float rotatedDegrees = InspectMotion.FaceRollAngle(Vector3.Transform(axis, cameraQ), Vector3.Transform(normal, cameraQ), Vector3.Transform(camera - pivot, cameraQ));
            Check(Math.Abs(degrees - rotatedDegrees) < .001f, "Roll calibration independent of world heading");
        }

        var item = pivot + new Vector3(.01f, .2f, .005f);
        var native = Quaternion.CreateFromYawPitchRoll(.5f, .2f, -.9f);
        var localPivot = Vector3.Transform(pivot - item, Quaternion.Inverse(native));
        for (int i = 0; i <= 100; i++)
        {
            float u = i / 100f;
            var a = InspectMotion.Sample(1.5f + .9f * u);
            var b = InspectMotion.Sample(4.1f - .9f * u);
            float amount = InspectMotion.GripRollWeight(a);
            Check(Math.Abs(amount - InspectMotion.GripRollWeight(b)) < 3e-6f, "Grip roll reverses exactly with 3->2");
            var rolled = InspectMotion.RollInGrip(item, native, pivot, axis, 72 * amount);
            Check(Near(rolled.Position + Vector3.Transform(localPivot, rolled.Rotation), pivot), "Handle center remains fixed while rolling");
            var delta = rolled.Rotation * Quaternion.Inverse(native);
            Check(Near(Vector3.Transform(axis, delta), axis), "Grip roll keeps accepted tip direction");
            for (int finger = 0; finger < 5; finger++)
            {
                var tip = pivot + new Vector3(.015f, .01f, -.03f + finger * .01f);
                var target = InspectMotion.FingerTarget(tip, pivot, axis, 72, a, finger);
                Check(Near(target, InspectMotion.FingerTarget(tip, pivot, axis, 72, b, finger)), "Finger paths reverse with knife");
                Check(Math.Abs(Vector3.Dot(target - tip, axis)) < 1e-6f, "Fingertip maintains height along handle");
            }
        }

        foreach (float t in new[]
        {
            0,
            1.2f,
            4.2f,
            5.2f
        }

        )
        {
            var sample = InspectMotion.Sample(t);
            Check(InspectMotion.GripRollWeight(sample) == 0, "Pinch roll only changes middle turn");
            Check(Near(InspectMotion.FingerTarget(item, pivot, axis, 72, sample, 0), item), "Native grip retained at rest and B");
        }
    }

    private void ThumbTargets()
    {
        var tip = new Vector3(-.003f, .022f, -.013f);
        var thumbBase = new Vector3(.035f, -.020f, -.025f);
        var extents = new Vector3(.009427f, .030202f, 0);
        var axis = Vector3.UnitZ;
        var previous = tip;
        for (int i = 0; i <= 100; i++)
        {
            float time = i / 100f;
            var forward = InspectMotion.Sample(1.5f + .9f * time);
            var reverse = InspectMotion.Sample(4.1f - .9f * time);
            var rotation = Quaternion.CreateFromAxisAngle(axis, 72.3f * InspectMotion.GripRollWeight(forward) * MathF.PI / 180);
            var side = Vector3.Transform(Vector3.UnitX, rotation);
            var target = InspectMotion.ThumbTarget(tip, thumbBase, Vector3.Zero, axis, side, extents.X, 72.3f, forward);
            var reverseTarget = InspectMotion.ThumbTarget(tip, thumbBase, Vector3.Zero, axis, side, extents.X, 72.3f, reverse);
            Check(Near(target, reverseTarget), "Thumb returns along the same side path");
            Check(Math.Abs(target.Z - tip.Z) < 1e-6f, "Thumb keeps its height along handle");
            Check(Vector3.Distance(target, previous) < .003f, "Thumb side transfer is continuous");
            if (forward.UprightA >= .32f)
            {
                float x = Math.Abs(Vector3.Dot(target, side));
                float y = Math.Abs(Vector3.Dot(target, Vector3.Cross(axis, side)));
                Check(x > extents.X || y > extents.Y, "Released thumb target lies outside handle bounds");
                Check(Math.Max(x - extents.X, y - extents.Y) > .005f, "Side target leaves room for glove thickness");
            }

            previous = target;
        }

        foreach (float time in new[]
        {
            0,
            1.2f,
            4.2f,
            5.2f
        }

        )
        {
            var target = InspectMotion.ThumbTarget(tip, thumbBase, Vector3.Zero, axis, Vector3.UnitX, extents.X, 72.3f, InspectMotion.Sample(time));
            Check(Near(target, tip), "Thumb restores native grip outside middle turn");
        }

        var centered = InspectMotion.ThumbTarget(new Vector3(0, 0, tip.Z), thumbBase, Vector3.Zero, axis, Vector3.UnitX, extents.X, 72.3f, new MotionSample(0, 1));
        Check(float.IsFinite(centered.X) && float.IsFinite(centered.Y), "Centered fingertip has a defined outward direction");
        Check(Math.Abs(centered.X) > extents.X || Math.Abs(centered.Y) > extents.Y, "Centered fingertip transfers to handle side");
    }

    private void Fingers(bool thumbOnly = false)
    {
        float maxError = 0, maxRoot = 0;
        var native = new[]
        {
            new Vector3(.035f, -.020f, -.025f),
            new Vector3(.031f, .009f, -.020f),
            new Vector3(.013f, .023f, -.016f),
            new Vector3(-.003f, .022f, -.013f)
        };
        var rotations = new[]
        {
            Quaternion.CreateFromYawPitchRoll(.4f, .2f, -.2f),
            Quaternion.CreateFromYawPitchRoll(.1f, -.3f, .8f),
            Quaternion.CreateFromYawPitchRoll(.3f, .1f, 1.1f)
        };
        var worldQ = Quaternion.CreateFromYawPitchRoll(1.2f, .6f, -.3f);
        var worldOrigin = new Vector3(4, 2, -3);
        Span<Vector3> solvedPoints = stackalloc Vector3[4];
        Span<Quaternion> solvedRotations = stackalloc Quaternion[3];
        Span<Vector3> transformedPoints = stackalloc Vector3[4];
        Span<Quaternion> transformedRotations = stackalloc Quaternion[3];
        for (int kind = 0; kind < (thumbOnly ? 1 : 5); kind++)
            for (int k = 0; k <= 50; k++)
            {
                var sample = new MotionSample(1 - k / 50f, k / 50f);
                var side = Vector3.Transform(Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 72 * InspectMotion.GripRollWeight(sample) * MathF.PI / 180));
                var target = kind == 0 ? InspectMotion.ThumbTarget(native[3], native[0], Vector3.Zero, Vector3.UnitZ, side, .009427f, 72, sample) : InspectMotion.FingerTarget(native[3], Vector3.Zero, Vector3.UnitZ, 72, sample, kind);
                var pose = InspectMotion.PoseFinger(native, rotations, target, kind == 0, solvedPoints, solvedRotations);
                if (kind == 0 && sample.UprightA >= .32f)
                {
                    Check(Math.Abs(Vector3.Dot(pose.Points[3], side)) - .009427f > .008f, "Posed thumb retains side clearance after applying joint limits");
                }

                maxError = Math.Max(maxError, pose.TargetError);
                Check(pose.TargetError <= Vector3.Distance(native[3], target) + .0002f, "Finger solver approaches its contact target");
                Check(Near(pose.Points[0], native[0]), "Finger root remains on palm");
                var baseAngle = Angle(pose.Rotations[0], rotations[0]);
                maxRoot = Math.Max(maxRoot, baseAngle);
                Check(baseAngle < (kind == 0 ? 28.05f : 16.05f), "Proximal joint opposition bounded");
                for (int j = 0; j < 3; j++)
                {
                    Check(Math.Abs(Vector3.Distance(pose.Points[j + 1], pose.Points[j]) - Vector3.Distance(native[j + 1], native[j])) < 1e-6f, "Finger segment length preserved");
                    var local = Vector3.Transform(native[j + 1] - native[j], Quaternion.Inverse(rotations[j]));
                    Check(Near(pose.Points[j] + Vector3.Transform(local, pose.Rotations[j]), pose.Points[j + 1]), "Finger solution matches transform FK");
                    if (j > 0)
                    {
                        var a = Vector3.Normalize(pose.Points[j] - pose.Points[j - 1]);
                        var b = Vector3.Normalize(pose.Points[j + 1] - pose.Points[j]);
                        float bend = MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1, 1)) * 180 / MathF.PI;
                        Check(bend >= 2.95f && bend <= 155.05f, "Finger hinges remain within curl limits");
                        var relative = Quaternion.Inverse(pose.Rotations[j - 1]) * pose.Rotations[j];
                        var before = Quaternion.Inverse(rotations[j - 1]) * rotations[j];
                        Check(Angle(relative, before) <= 25.05f, "Middle/distal adjustment bounded");
                    }
                }

                if (k == 0)
                    for (int j = 0; j < 3; j++)
                        Check(Near(pose.Rotations[j], rotations[j]), "Exact native finger rest");
                if (k == 50)
                {
                    var movedPoints = Array.ConvertAll(native, p => worldOrigin + Vector3.Transform(p, worldQ));
                    var movedRotations = Array.ConvertAll(rotations, q => worldQ * q);
                    var moved = InspectMotion.PoseFinger(movedPoints, movedRotations, worldOrigin + Vector3.Transform(target, worldQ), kind == 0, transformedPoints, transformedRotations);
                    Check(Vector3.Distance(moved.Points[3], worldOrigin + Vector3.Transform(pose.Points[3], worldQ)) < .00005f, "Finger solver follows camera heading");
                }
            }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        float accumulatedError = 0;
        for (int iteration = 0; iteration < 2048; iteration++)
        {
            var pose = InspectMotion.PoseFinger(native, rotations, native[3] + new Vector3(.01f, 0, 0), iteration % 5 == 0, solvedPoints, solvedRotations);
            accumulatedError += pose.TargetError;
        }

        fingerSolverAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(fingerSolverAllocatedBytes == 0, "Finger solving allocates no managed memory after warmup");
        Check(float.IsFinite(accumulatedError), "Repeated finger solving remains finite");
        Console.WriteLine($"Synthetic fingers: maximum contact error={maxError * 1000:F2}mm; proximal adjustment={maxRoot:F1} degrees.");
    }
}
