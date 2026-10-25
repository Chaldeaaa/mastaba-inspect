using System.Numerics;

namespace MastabaInspect.Firearms;

internal readonly record struct ArmPose(Vector3 Shoulder, Vector3 Elbow, Vector3 Hand, Quaternion UpperRotation, Quaternion LowerRotation, Quaternion HandRotation);
