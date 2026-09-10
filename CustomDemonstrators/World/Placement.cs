using UnityEngine;

namespace CustomDemonstrators.World;

// Where a hand-placed demonstrator slot or garage stands: an offset from its anchor (usually the
// origin shift parent) and the yaw it faces there.
internal readonly struct Placement(Vector3 offset, float yaw)
{
    internal readonly Vector3 Offset = offset;
    internal readonly float Yaw = yaw;
}
