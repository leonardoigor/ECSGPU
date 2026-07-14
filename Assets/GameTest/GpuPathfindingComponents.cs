using Unity.Mathematics;
using ECSGPU.GpuEcs;

namespace ECSGPU.GpuEcs.Examples
{
    [GpuComponent]
    public struct GpuGridCoordComponent : IGpuComponent
    {
        public int2 Value;
    }

    [GpuComponent]
    public struct GpuPathStateComponent : IGpuComponent
    {
        public int Distance;
        public int BestDirection;
        public uint Walkable;
        public uint Padding;
    }

    [GpuComponent]
    public struct GpuPathStateNextComponent : IGpuComponent
    {
        public int Distance;
        public int BestDirection;
        public uint Walkable;
        public uint Padding;
    }
}
