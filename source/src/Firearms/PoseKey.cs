using System.Numerics;

namespace MastabaInspect.Firearms;
internal readonly record struct PoseKey(float Time, float Weight, float Reverse, float Release, float Fingers)
{
    internal Vector4 Channels => new(Weight, Reverse, Release, Fingers);
}
