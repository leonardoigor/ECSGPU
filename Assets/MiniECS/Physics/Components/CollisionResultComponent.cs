using Unity.Mathematics;

namespace MiniECS.Physics
{
    /// <summary>
    /// Per-entity collision state, updated every frame by CollisionSystem.
    /// Holds the first active contact for quick queries (full contact list is
    /// surfaced via CollisionEvents).
    /// </summary>
    public struct CollisionResultComponent : IComponentData
    {
        public int OtherId;
        public float3 Normal;   // points from this entity toward the other
        public float Depth;     // penetration depth (0 if no contact)
        public int IsTrigger;   // 1 if this contact was a trigger
    }
}
