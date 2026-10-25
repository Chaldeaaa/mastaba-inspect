using System.Numerics;

namespace MastabaInspect.Knives;

internal readonly struct ArmPose
{
    public readonly Vector3 ShoulderPosition, ElbowPosition, HandPosition;
    public readonly Quaternion ShoulderRotation, ElbowRotation, HandRotation;
    public ArmPose(Vector3 shoulderPosition, Vector3 elbowPosition, Vector3 handPosition, Quaternion shoulderRotation, Quaternion elbowRotation, Quaternion handRotation) => (ShoulderPosition, ElbowPosition, HandPosition, ShoulderRotation, ElbowRotation, HandRotation) = (shoulderPosition, elbowPosition, handPosition, shoulderRotation, elbowRotation, handRotation);
}
