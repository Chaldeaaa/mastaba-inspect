using System;
using System.Collections.Generic;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace MastabaInspect.Knives;
internal sealed class FingerChain
{
    private readonly Transform[] joints;
    private readonly Transform tip;
    private readonly Vector3 tipOffset;
    private readonly TransformLease[] poses =
    {
        new(),
        new(),
        new()
    };
    internal int Kind { get; }
    internal string Name => joints[0].name;
    internal Vector3 BasePosition => joints[0].position;

    private FingerChain(Transform[] bones, Transform end, int kind)
    {
        joints = bones;
        tip = end;
        Kind = kind;
        tipOffset = bones[1].InverseTransformPoint(bones[2].position) * .65f;
    }

    internal static FingerChain Find(Transform hand, string name, int kind)
    {
        Transform first = null;
        int shortestDepth = int.MaxValue;
        foreach (var bone in hand.GetComponentsInChildren<Transform>())
        {
            if (!bone || bone.childCount == 0 || !bone.name.EndsWith("1", StringComparison.Ordinal) || bone.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            int depth = 0;
            var parent = bone;
            while (parent && parent != hand && depth < 16)
            {
                depth++;
                parent = parent.parent;
            }

            if (parent == hand && depth < shortestDepth)
            {
                first = bone;
                shortestDepth = depth;
            }
        }

        if (!first)
            return null;
        string prefix = first.name[..^1];
        var chain = new List<Transform>
        {
            first
        };
        while (chain.Count < 3)
        {
            var current = chain[^1];
            Transform next = null;
            for (int i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                if (string.Equals(child.name, prefix + (chain.Count + 1), StringComparison.OrdinalIgnoreCase) && (child.position - current.position).sqrMagnitude > 1e-10f)
                {
                    next = child;
                    break;
                }
            }

            if (!next)
                return null;
            chain.Add(next);
        }

        Transform end = null;
        for (int i = 0; i < chain[2].childCount; i++)
        {
            var child = chain[2].GetChild(i);
            if ((string.Equals(child.name, prefix + "4", StringComparison.OrdinalIgnoreCase) || child.name.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0) && (child.position - chain[2].position).sqrMagnitude > 1e-10f)
            {
                end = child;
                break;
            }
        }

        return new FingerChain(chain.ToArray(), end, kind);
    }

    internal void Capture()
    {
        for (int i = 0; i < 3; i++)
            poses[i].Capture(joints[i]);
    }

    internal void Seal()
    {
        foreach (var pose in poses)
            pose.Seal();
    }

    internal void Restore()
    {
        try
        {
            poses[0].Restore();
        }
        finally
        {
            try
            {
                poses[1].Restore();
            }
            finally
            {
                poses[2].Restore();
            }
        }
    }

    internal void Straighten(Vector3 direction, float weight)
    {
        NVector[] segments = new NVector[3];
        NQuaternion[] rotations = new NQuaternion[3];
        for (int index = 0; index < 3; index++)
        {
            Vector3 end;
            if (index < 2)
            {
                end = joints[index + 1].position;
            }
            else if (tip)
            {
                end = tip.position;
            }
            else
            {
                end = joints[2].TransformPoint(tipOffset);
            }

            Vector3 segment = end - joints[index].position;
            Quaternion rotation = joints[index].rotation;
            segments[index] = new NVector(segment.x, segment.y, segment.z);
            rotations[index] = new NQuaternion(rotation.x, rotation.y, rotation.z, rotation.w);
        }

        NQuaternion[] aligned = FingerAlignment.Straighten(segments, rotations, new NVector(direction.x, direction.y, direction.z), weight);
        for (int index = 0; index < 3; index++)
        {
            NQuaternion rotation = aligned[index];
            joints[index].rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }
    }

    internal void Contact(Vector3 target, float weight)
    {
        NVector[] points = new NVector[4];
        NQuaternion[] rotations = new NQuaternion[3];
        for (int index = 0; index < 3; index++)
        {
            Vector3 position = joints[index].position;
            Quaternion rotation = joints[index].rotation;
            points[index] = new NVector(position.x, position.y, position.z);
            rotations[index] = new NQuaternion(rotation.x, rotation.y, rotation.z, rotation.w);
        }

        Vector3 endpoint = tip ? tip.position : joints[2].TransformPoint(tipOffset);
        points[3] = new NVector(endpoint.x, endpoint.y, endpoint.z);
        NQuaternion[] aligned = FingerAlignment.ThumbContact(points, rotations, new NVector(target.x, target.y, target.z), weight);
        for (int index = 0; index < 3; index++)
        {
            NQuaternion rotation = aligned[index];
            joints[index].rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }
    }

    internal float Apply(NVector pivot, NVector axis, NVector sideAxis, float halfWidth, float degrees, MotionSample sample)
    {
        if (sample.UprightA <= 0)
            return 0;
        var points = new NVector[4];
        var rotations = new NQuaternion[3];
        for (int i = 0; i < 3; i++)
        {
            var p = joints[i].position;
            var q = joints[i].rotation;
            points[i] = new(p.x, p.y, p.z);
            rotations[i] = new(q.x, q.y, q.z, q.w);
        }

        var endpoint = tip ? tip.position : joints[2].TransformPoint(tipOffset);
        points[3] = new(endpoint.x, endpoint.y, endpoint.z);
        var target = Kind == 0 ? InspectMotion.ThumbTarget(points[3], points[0], pivot, axis, sideAxis, halfWidth, degrees, sample) : InspectMotion.FingerTarget(points[3], pivot, axis, degrees, sample, Kind);
        var posed = InspectMotion.PoseFinger(points, rotations, target, Kind == 0);
        for (int i = 0; i < 3; i++)
        {
            var q = posed.Rotations[i];
            joints[i].rotation = new(q.X, q.Y, q.Z, q.W);
        }

        return posed.TargetError;
    }
}
