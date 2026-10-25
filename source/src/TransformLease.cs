using UnityEngine;

namespace MastabaInspect;

internal sealed class TransformLease
{
    private Transform target;
    private Transform parent;
    private Vector3 position;
    private Quaternion rotation;
    private Vector3 appliedPosition;
    private Quaternion appliedRotation;
    private bool sealedPose;
    internal void Capture(Transform value)
    {
        target = value;
        parent = value.parent;
        position = value.localPosition;
        rotation = value.localRotation;
        sealedPose = false;
    }

    internal void Seal()
    {
        appliedPosition = target.localPosition;
        appliedRotation = target.localRotation;
        sealedPose = true;
    }

    internal void Restore()
    {
        Transform value = target;
        target = null;
        if (!value || value.parent != parent)
        {
            return;
        }

        if (!sealedPose || (value.localPosition - appliedPosition).sqrMagnitude < 1e-10f)
        {
            value.localPosition = position;
        }

        if (!sealedPose || Quaternion.Angle(value.localRotation, appliedRotation) < .05f)
        {
            value.localRotation = rotation;
        }

        parent = null;
    }
}
