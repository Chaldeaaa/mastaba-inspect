using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace MastabaInspect.Firearms;
internal static class RevolverMotionTests
{
    private static void Main()
    {
        Span<Quaternion> aligned = stackalloc Quaternion[3];
        Span<Quaternion> thumbAligned = stackalloc Quaternion[3];
        Require(Motion.Sample(0).Weight == 0 && Motion.Sample(0).Release == 0, "Ready start");
        Require(Motion.Sample(Motion.DURATION).Weight == 0 && Motion.Sample(Motion.DURATION).Release == 0, "Ready end");
        Require(Motion.Sample(.28f).Release > .8f && Motion.Sample(.28f).Weight > 0, "Left hand release overlaps presentation approach");
        Require(Motion.Sample(6.1f).Release < 1 && Motion.Sample(6.1f).Weight > 0, "Regrip overlaps ready return");
        Require(Motion.Sample(.35f).Fingers == 0 && Motion.Sample(.8f).Fingers == 0 && Motion.Sample(.85f).Weight > .9f, "Fingers remain in grip until front becomes visible");
        Require(Motion.Sample(1).Fingers > 0 && Motion.Sample(1).Weight < 1, "Fingers settle during front approach");
        Require(Motion.Sample(1.55f).Fingers == 1 && Motion.Sample(1.55f).Weight == 1, "Fingers settle for presentation");
        Require(Motion.Sample(5.85f).Fingers < .5f && Motion.Sample(5.85f).Weight < 1, "Fingers return with the ready movement");
        Quaternion front = Motion.Rotation(Motion.Sample(1.55f));
        Quaternion back = Motion.Rotation(Motion.Sample(3.45f));
        Require(Vector3.Transform(Vector3.UnitX, front).Z > .9f, "Front face");
        Require(Vector3.Transform(Vector3.UnitX, back).Z < -.9f, "Opposite face");
        Require(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, front), Vector3.Transform(Vector3.UnitZ, back)) < .000001f, "Barrel axis remains stable through reversal");
        Require(MathF.Abs(Quaternion.Dot(front, Motion.Rotation(Motion.Sample(5.35f)))) > .99999f, "Reverse returns to front");
        float maximumVelocityJump = 0;
        foreach (PoseKey key in Motion.Keys)
        {
            PoseKey value = Motion.Sample(key.Time);
            Require(MathF.Abs(value.Weight - key.Weight) < .00001f && MathF.Abs(value.Reverse - key.Reverse) < .00001f && MathF.Abs(value.Release - key.Release) < .00001f && MathF.Abs(value.Fingers - key.Fingers) < .00001f, "Key pose");
            if (key.Time > 0 && key.Time < Motion.DURATION)
            {
                Vector4 before = Motion.Sample(key.Time - .0005f).Channels;
                Vector4 after = Motion.Sample(key.Time + .0005f).Channels;
                Vector4 incoming = (value.Channels - before) / .0005f;
                Vector4 outgoing = (after - value.Channels) / .0005f;
                float jump = Vector4.Distance(incoming, outgoing);
                maximumVelocityJump = MathF.Max(maximumVelocityJump, jump);
                Require(jump < .06f, $"Continuous transition velocity at {key.Time}: {jump}");
            }
        }

        int stoppedPresentationFrames = 0;
        for (int tick = 138; tick < 684; tick++)
        {
            float time = tick / 120f;
            float travel = MathF.Abs(Motion.Sample(time + 1f / 120).Reverse - Motion.Sample(time).Reverse);
            if (travel < .000001f)
            {
                stoppedPresentationFrames++;
            }
        }

        Require(stoppedPresentationFrames == 0, "Presentation rolls continuously through both faces");
        Require(Motion.Sample(.0005f).Channels.X < .00001f && Motion.Sample(Motion.DURATION - .0005f).Weight < .00001f, "Ready endpoints ease to rest");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "R66Bounds.json")));
        List<Vector3> points = new();
        foreach (JsonElement point in fixture.RootElement.GetProperty("points").EnumerateArray())
        {
            points.Add(new Vector3(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle()));
        }

        int framingCases = 0;
        int projectionChecks = 0;
        float maximumProjectionError = 0;
        Vector3[] offsets = new Vector3[points.Count];
        Vector3[] muzzleOffsets = points.ToArray();
        float[] aspects =
        {
            4f / 3,
            16f / 10,
            16f / 9,
            21f / 9
        };
        for (int degrees = 30; degrees <= 100; degrees += 5)
        {
            float vertical = MathF.Tan(degrees * MathF.PI / 360);
            foreach (float aspect in aspects)
            {
                foreach (float shift in new[]
                {
                    0f,
                    .08f
                }

                )
                {
                    ViewFrame frame = new(-.8f * vertical * aspect + shift, .8f * vertical * aspect + shift, -.92f * vertical, .12f * vertical, .07f);
                    Vector3 pivot = Motion.PresentationPivot(muzzleOffsets, frame);
                    for (int tick = 126; tick <= 702; tick++)
                    {
                        PoseKey sample = Motion.Sample(tick / 120f);
                        Quaternion rotation = Motion.Rotation(sample);
                        for (int index = 0; index < points.Count; index++)
                        {
                            offsets[index] = Vector3.Transform(points[index], rotation);
                        }

                        Require(float.IsFinite(pivot.LengthSquared()), "Finite framing");
                        foreach (Vector3 offset in offsets)
                        {
                            Vector3 projected = pivot + offset;
                            float x = projected.X / projected.Z;
                            float y = projected.Y / projected.Z;
                            float error = MathF.Max(MathF.Max(frame.Left - x, x - frame.Right), MathF.Max(frame.Bottom - y, y - frame.Top));
                            maximumProjectionError = MathF.Max(maximumProjectionError, error);
                            Require(error <= .000001f && projected.Z >= frame.Near - .000001f, "Weapon stays inside framing rectangle");
                            projectionChecks++;
                        }

                        framingCases++;
                    }
                }
            }
        }

        Vector3[] segments =
        {
            new(.02f, .018f, .005f),
            new(.025f, -.013f, .006f),
            new(.012f, -.015f, .002f)
        };
        Quaternion[] fingerRotations =
        {
            Quaternion.Identity,
            Quaternion.Identity,
            Quaternion.Identity
        };
        Vector3[] shoulders =
        {
            new(.18f, -.12f, -.02f),
            new(-.18f, -.12f, -.02f)
        };
        Vector3[] elbows =
        {
            new(.28f, -.32f, .14f),
            new(-.18f, -.32f, .14f)
        };
        Vector3[] hands =
        {
            new(.12f, -.18f, .35f),
            new(.08f, -.20f, .39f)
        };
        float maximumLengthError = 0;
        float maximumGripError = 0;
        float minimumHandDepth = float.PositiveInfinity;
        float maximumHandDepth = float.NegativeInfinity;
        float maximumThumbAngle = 0;
        float maximumForearmStep = 0;
        float[] forearmTwists =
        {
            0,
            0
        };
        Quaternion[] previousLower =
        {
            Quaternion.Identity,
            Quaternion.Identity
        };
        for (int tick = 0; tick <= 816; tick++)
        {
            PoseKey sample = Motion.Sample(tick / 120f);
            Require(sample.Weight >= -.000001f && sample.Weight <= 1.000001f && sample.Release >= -.000001f && sample.Release <= 1.000001f && sample.Fingers >= -.000001f && sample.Fingers <= 1.000001f && sample.Reverse >= -.030001f && sample.Reverse <= 1.000001f, "Motion channels stay inside their ranges");
            Quaternion rotation = Quaternion.Slerp(Quaternion.Identity, Motion.Rotation(sample), sample.Weight);
            Vector3 rightTarget = Vector3.Lerp(hands[0], new Vector3(.14f, -.20f, .48f), sample.Weight);
            Vector3 leftTarget = Vector3.Lerp(hands[1], new Vector3(-.25f, -.65f, .39f), sample.Release);
            Vector3 item = hands[0] + new Vector3(-.02f, .04f, .08f);
            Vector3[] targets =
            {
                rightTarget,
                leftTarget
            };
            for (int arm = 0; arm < 2; arm++)
            {
                Quaternion delta = arm == 0 ? rotation : Quaternion.Identity;
                ArmPose pose = Motion.SolveArm(shoulders[arm], elbows[arm], hands[arm], Quaternion.Identity, Quaternion.Identity, Quaternion.Identity, targets[arm], delta, ref forearmTwists[arm]);
                if (tick > 0)
                {
                    float step = 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(previousLower[arm], pose.LowerRotation)), 0, 1));
                    maximumForearmStep = MathF.Max(maximumForearmStep, step);
                    Require(step < .2f, $"Continuous forearm rotation: frame={tick}; arm={arm}; degrees={step * 180 / MathF.PI:G9}");
                }

                previousLower[arm] = pose.LowerRotation;
                float upperError = MathF.Abs(Vector3.Distance(pose.Shoulder, pose.Elbow) - Vector3.Distance(shoulders[arm], elbows[arm]));
                float lowerError = MathF.Abs(Vector3.Distance(pose.Elbow, pose.Hand) - Vector3.Distance(elbows[arm], hands[arm]));
                maximumLengthError = MathF.Max(maximumLengthError, MathF.Max(upperError, lowerError));
                Require(upperError < .000001f && lowerError < .000001f, "Arm segment lengths");
                Require(Vector3.Distance(pose.Shoulder, shoulders[arm]) < .000001f, "Shoulder remains attached");
                Require(Vector3.Distance(pose.Hand, pose.Shoulder) <= (Vector3.Distance(shoulders[arm], elbows[arm]) + Vector3.Distance(elbows[arm], hands[arm])) * .99951f, "Hand stays within arm reach");
                Vector3 rotatedElbow = shoulders[arm] + Vector3.Transform(elbows[arm] - shoulders[arm], pose.UpperRotation);
                Vector3 rotatedHand = rotatedElbow + Vector3.Transform(hands[arm] - elbows[arm], pose.LowerRotation);
                Require(Vector3.Distance(rotatedElbow, pose.Elbow) < .000001f && Vector3.Distance(rotatedHand, pose.Hand) < .000001f, $"Joint rotations reproduce connected arm without position edits: frame={tick}; arm={arm}; elbowError={Vector3.Distance(rotatedElbow, pose.Elbow):G9}; handError={Vector3.Distance(rotatedHand, pose.Hand):G9}");
                if (arm == 0)
                {
                    Vector3 itemTarget = pose.Hand + Vector3.Transform(item - hands[0], rotation);
                    if (sample.Weight == 1)
                    {
                        minimumHandDepth = MathF.Min(minimumHandDepth, pose.Hand.Z);
                        maximumHandDepth = MathF.Max(maximumHandDepth, pose.Hand.Z);
                    }

                    float error = Vector3.Distance(pose.Hand - itemTarget, Vector3.Transform(hands[arm] - item, rotation));
                    maximumGripError = MathF.Max(maximumGripError, error);
                    Require(error < .000001f, "Right hand keeps grip");
                }
            }

            if (sample.Release == 1)
            {
                Require(Vector3.Distance(leftTarget, rightTarget) > .5f, "Left hand leaves firearm");
            }

            Vector3 direction = Vector3.Normalize(Vector3.Transform(new Vector3(.035f, -.24f, 1), rotation));
            FingerAlignment.Straighten(segments, fingerRotations, direction, sample.Fingers, aligned);
            for (int joint = 0; joint < 3; joint++)
            {
                Vector3 moved = Vector3.Transform(segments[joint], aligned[joint]);
                Require(float.IsFinite(moved.LengthSquared()) && MathF.Abs(moved.Length() - segments[joint].Length()) < .000001f, "Finger joint length");
                if (sample.Fingers == 1)
                {
                    Require(Vector3.Dot(Vector3.Normalize(moved), direction) > .99999f, "Index is straight and points forward");
                }

                if (sample.Fingers == 0)
                {
                    Require(MathF.Abs(Quaternion.Dot(aligned[joint], Quaternion.Identity)) > .99999f, "Index returns to native pose");
                }
            }
        }

        Require(maximumHandDepth - minimumHandDepth < .000001f, "Hand depth remains fixed during both face presentations");
        Vector3[] thumbPoints =
        {
            Vector3.Zero,
            segments[0],
            segments[0] + segments[1],
            segments[0] + segments[1] + segments[2]
        };
        Vector3 distal = segments[1] + segments[2];
        Vector3 bendAxis = Vector3.Normalize(Vector3.Cross(segments[0], distal));
        Quaternion contactBend = Quaternion.CreateFromAxisAngle(bendAxis, .2f);
        Quaternion contactRoot = Quaternion.CreateFromYawPitchRoll(.25f, -.2f, .12f);
        Vector3 contact = Vector3.Transform(segments[0] + Vector3.Transform(distal, contactBend), contactRoot);
        float maximumThumbContactError = 0;
        float maximumThumbHingeChange = 0;
        for (int tick = 0; tick <= 816; tick++)
        {
            PoseKey sample = Motion.Sample(tick / 120f);
            Quaternion orientation = Motion.Rotation(sample);
            Vector3 origin = new(.12f, -.08f, .48f);
            Vector3[] worldPoints = Array.ConvertAll(thumbPoints, point => origin + Vector3.Transform(point, orientation));
            Quaternion[] nativeRotations =
            {
                orientation,
                orientation,
                orientation
            };
            FingerAlignment.ThumbContact(worldPoints, nativeRotations, origin + Vector3.Transform(contact, orientation), sample.Fingers, thumbAligned);
            ReadOnlySpan<Quaternion> rotations = thumbAligned;
            Quaternion rootDelta = Quaternion.Normalize(rotations[0] * Quaternion.Inverse(orientation));
            Quaternion lowerDelta = Quaternion.Normalize(rotations[1] * Quaternion.Inverse(orientation));
            float angle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(rootDelta.W), 0, 1));
            maximumThumbAngle = MathF.Max(maximumThumbAngle, angle);
            float hingeChange = 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(rootDelta, lowerDelta)), 0, 1));
            maximumThumbHingeChange = MathF.Max(maximumThumbHingeChange, hingeChange);
            Require(angle <= 1.134465f && hingeChange <= .610866f, "Thumb joint rotation limits");
            Require(MathF.Abs(Quaternion.Dot(rotations[1], rotations[2])) > .99999f, "Thumb distal joint retains its bend");
            Vector3 endpoint = origin;
            for (int segment = 0; segment < segments.Length; segment++)
            {
                Vector3 moved = Vector3.Transform(segments[segment], rotations[segment]);
                endpoint += moved;
                Require(MathF.Abs(moved.Length() - segments[segment].Length()) < .000001f, "Thumb segments retain length");
                if (sample.Fingers == 0)
                {
                    Require(MathF.Abs(Quaternion.Dot(rotations[segment], orientation)) > .99999f, "Thumb restores native grip");
                }
            }

            if (sample.Fingers >= .999999f)
            {
                float error = Vector3.Distance(endpoint, origin + Vector3.Transform(contact, orientation));
                maximumThumbContactError = MathF.Max(maximumThumbContactError, error);
                Require(error < .000002f, "Thumb reaches contact through weapon rotation");
            }
        }

        foreach (Vector3 target in new[]
        {
            new Vector3(0, .2f, .1f),
            new Vector3(-.2f, -.1f, -.1f),
            new Vector3(.001f, .001f, .001f)
        }

        )
        {
            FingerAlignment.ThumbContact(thumbPoints, fingerRotations, target, 1, thumbAligned);
            ReadOnlySpan<Quaternion> rotations = thumbAligned;
            float rootAngle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(rotations[0].W), 0, 1));
            float hingeAngle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(rotations[0], rotations[1])), 0, 1));
            Require(float.IsFinite(rootAngle) && rootAngle <= 1.134465f && hingeAngle <= .610866f, "Unreachable contact remains within thumb rotation limits");
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        float accumulatedRotation = 0;
        for (int iteration = 0; iteration < 2048; iteration++)
        {
            FingerAlignment.Straighten(segments, fingerRotations, Vector3.UnitZ, 1, aligned);
            FingerAlignment.ThumbContact(thumbPoints, fingerRotations, contact, 1, thumbAligned);
            accumulatedRotation += aligned[0].W + thumbAligned[0].W;
        }

        long fingerSolverAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(fingerSolverAllocatedBytes == 0, "Firearm finger solving allocates no managed memory after warmup");
        Require(float.IsFinite(accumulatedRotation), "Repeated firearm finger solving remains finite");
        Quaternion forearmTurn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(hands[0] - elbows[0]), MathF.PI * .45f);
        float testTwist = 0;
        ArmPose twisted = Motion.SolveArm(shoulders[0], elbows[0], hands[0], Quaternion.Identity, Quaternion.Identity, Quaternion.Identity, hands[0], forearmTurn, ref testTwist);
        float forearmAngle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(twisted.LowerRotation.W), 0, 1));
        Require(MathF.Abs(forearmAngle - MathF.PI * .45f * .7f) < .00001f, "Forearm shares wrist axial rotation");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed = true,
            duration = Motion.DURATION,
            framesChecked = 817,
            sourceParts = 7,
            sourceBounds = points.Count,
            framingCases,
            projectionChecks,
            maximumProjectionError,
            cameraFovRange = new[] { 30, 100 },
            cameraAspects = aspects,
            maximumLengthError,
            maximumGripError,
            indexAlignmentChecked = true,
            leftHandIndependent = true,
            shoulderAnchored = true,
            rotationOnlyArmChecked = true,
            maximumForearmStepDegrees = maximumForearmStep * 180 / MathF.PI,
            handPresentationDepthVariation = maximumHandDepth - minimumHandDepth,
            thumbMaximumAngleDegrees = maximumThumbAngle * 180 / MathF.PI,
            thumbMaximumHingeChangeDegrees = maximumThumbHingeChange * 180 / MathF.PI,
            maximumThumbContactError,
            thumbDistalBendPreserved = true,
            thumbContactChecked = true,
            fingerTimingChecked = true,
            fingerSolverAllocatedBytes,
            maximumVelocityJump,
            stoppedPresentationFrames,
            continuousMotionChecked = true,
            inGameTested = false
        }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
