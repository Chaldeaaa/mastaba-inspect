using RootMotion.FinalIK;
using UnityEngine;
using NQuaternion = System.Numerics.Quaternion;
using NVector = System.Numerics.Vector3;

namespace MastabaInspect.Firearms;
internal sealed class ArmBinding
{
    private readonly LimbIK limb;
    private readonly IKSolverLimb solver;
    private readonly Transform upper;
    private readonly Transform lower;
    private readonly Transform hand;
    private readonly TransformLease upperPose = new();
    private readonly TransformLease lowerPose = new();
    private readonly TransformLease handPose = new();
    private float positionWeight;
    private float rotationWeight;
    private float forearmTwist;
    private bool saved;
    internal ArmBinding(LimbIK value)
    {
        limb = value;
        solver = value.solver;
        upper = solver.bone1.transform;
        lower = solver.bone2.transform;
        hand = solver.bone3.transform;
    }

    internal Vector3 HandPosition => hand.position;
    internal Transform HandTransform => hand;

    internal static bool CanBind(LimbIK limb, Transform item)
    {
        if (!limb || limb.solver == null || !limb.solver.initiated)
        {
            return false;
        }

        IKSolverLimb solver = limb.solver;
        Transform upper = solver.bone1?.transform;
        Transform lower = solver.bone2?.transform;
        Transform hand = solver.bone3?.transform;
        return upper && lower && hand && lower.IsChildOf(upper) && hand.IsChildOf(lower) && !upper.IsChildOf(item) && (hand.position - lower.position).sqrMagnitude > .0001f;
    }

    internal void Capture()
    {
        if (limb.enabled)
        {
            solver.Update();
        }

        upperPose.Capture(upper);
        lowerPose.Capture(lower);
        handPose.Capture(hand);
        positionWeight = solver.IKPositionWeight;
        rotationWeight = solver.IKRotationWeight;
        saved = true;
        solver.IKPositionWeight = 0;
        solver.IKRotationWeight = 0;
    }

    internal Vector3 Apply(Vector3 target, Quaternion delta)
    {
        ArmPose pose = Motion.SolveArm(ToNumerics(upper.position), ToNumerics(lower.position), ToNumerics(hand.position), ToNumerics(upper.rotation), ToNumerics(lower.rotation), ToNumerics(hand.rotation), ToNumerics(target), ToNumerics(delta), ref forearmTwist);
        upper.rotation = ToUnity(pose.UpperRotation);
        lower.rotation = ToUnity(pose.LowerRotation);
        hand.rotation = ToUnity(pose.HandRotation);
        upperPose.Seal();
        lowerPose.Seal();
        handPose.Seal();
        return hand.position;
    }

    internal void Restore()
    {
        if (!saved)
        {
            return;
        }

        saved = false;
        try
        {
            upperPose.Restore();
            lowerPose.Restore();
            handPose.Restore();
        }
        finally
        {
            if (solver.IKPositionWeight == 0)
            {
                solver.IKPositionWeight = positionWeight;
            }

            if (solver.IKRotationWeight == 0)
            {
                solver.IKRotationWeight = rotationWeight;
            }
        }
    }

    internal static NVector ToNumerics(Vector3 value) => new(value.x, value.y, value.z);
    internal static NQuaternion ToNumerics(Quaternion value) => new(value.x, value.y, value.z, value.w);
    internal static Vector3 ToUnity(NVector value) => new(value.X, value.Y, value.Z);
    internal static Quaternion ToUnity(NQuaternion value) => new(value.X, value.Y, value.Z, value.W);
}
