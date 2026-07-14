using Unity.Mathematics;

namespace ECSGPU.GpuEcs
{
    public readonly struct GpuDispatch
    {
        public static uint3 threadId
        {
            get
            {
                throw new System.InvalidOperationException(
                    "GpuDispatch.threadId can only be used in [GpuKernel] methods. " +
                    "These methods are transpiled to HLSL and never executed on CPU.");
            }
        }
    }
}
