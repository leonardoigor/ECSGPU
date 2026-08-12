using Unity.Mathematics;

namespace MiniECS.Physics
{
    public enum ColliderShapeType : byte
    {
        Sphere = 0,
        Box = 1,
        Capsule = 2,
        ConvexMesh = 3
    }

    /// <summary>
    /// Colliders converted from Unity BoxCollider / SphereCollider / CapsuleCollider / MeshCollider.
    /// Blittable so it can be copied into NativeArrays and consumed by Jobs.
    /// </summary>
    public struct ColliderComponent : IComponentData
    {
        public ColliderShapeType Shape;
        public byte IsTrigger;     // 1 = trigger, 0 = solid
        public byte Layer;         // collision layer (reserved)
        public byte Padding;
        public float3 CenterOffset; // local offset from the entity transform
        public float Radius;        // sphere / capsule radius
        public float3 HalfExtents; // box half extents
        public float CapsuleHeight; // total capsule height (including caps)
        public int CapsuleAxis;     // 0 = X, 1 = Y, 2 = Z
        public int MeshHullIndex;   // ConvexHullRegistry index (ConvexMesh only)
    }
}
