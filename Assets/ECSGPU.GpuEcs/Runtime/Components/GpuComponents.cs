using Unity.Mathematics;

namespace ECSGPU.GpuEcs
{
    [GpuComponent]
    public struct GpuPositionComponent : IGpuComponent
    {
        public float3 Value;
    }

    [GpuComponent]
    public struct GpuVelocityComponent : IGpuComponent
    {
        public float3 Value;
    }

    [GpuComponent]
    public struct GpuMassComponent : IGpuComponent
    {
        public float Value;
    }

    [GpuComponent]
    public struct GpuForceComponent : IGpuComponent
    {
        public float3 Value;
    }

    [GpuComponent]
    public struct GpuRotationComponent : IGpuComponent
    {
        public quaternion Value;
    }

    [GpuComponent]
    public struct GpuColorComponent : IGpuComponent
    {
        public float4 Value;
    }
}
