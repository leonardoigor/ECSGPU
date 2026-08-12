using Unity.Mathematics;

namespace ECSGPU.GpuEcs
{
    /// <summary>
    /// GPU-side mirror of the CPU ColliderComponent. Shape/IsTrigger are kept as
    /// ints (the transpiler does not map byte). The GPU narrow phase uses a
    /// precomputed world-space bounding sphere (WorldCenter + BoundingRadius) for
    /// performance and because the compute-kernel transpiler forbids helper methods
    /// / local arrays / switch / quaternion math, which rules out full analytic SAT
    /// or GJK on the GPU. The CPU path (MiniECS) provides exact GJK/EPA for every
    /// shape including convex meshes.
    /// </summary>
    [GpuComponent]
    public struct GpuColliderComponent : IGpuComponent
    {
        public int Shape;            // 0 = sphere, 1 = box, 2 = capsule, 3 = convex mesh
        public int IsTrigger;
        public float3 WorldCenter;  // precomputed on CPU = entityPos + R * CenterOffset
        public float BoundingRadius; // world-space bounding sphere radius around WorldCenter
    }

    [GpuComponent]
    public struct GpuCollisionResultComponent : IGpuComponent
    {
        public int OtherId;
        public float3 Normal;       // A -> B
        public float Depth;
        public int IsTrigger;
    }
}
