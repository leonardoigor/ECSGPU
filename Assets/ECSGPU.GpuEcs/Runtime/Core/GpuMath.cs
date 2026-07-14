using System;
using Unity.Mathematics;

namespace ECSGPU.GpuEcs
{
    /// <summary>
    /// HLSL-compatible math functions for use in [GpuKernel] methods.
    /// These are transpiled to HLSL intrinsics and never executed on CPU.
    /// Calling them on CPU will throw.
    /// </summary>
    public static class GpuMath
    {
        public static float dot(float3 a, float3 b)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float3 cross(float3 a, float3 b)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float3 normalize(float3 v)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float length(float3 v)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float rsqrt(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float rcp(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float abs(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float3 abs(float3 v)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float sign(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float floor(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float ceil(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float round(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float frac(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float saturate(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float clamp(float x, float min, float max)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float lerp(float a, float b, float t)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float step(float y, float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float smoothstep(float min, float max, float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float sin(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float cos(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float tan(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float atan2(float y, float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float pow(float x, float y)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float sqrt(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float exp(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float log(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float log2(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float exp2(float x)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float max(float a, float b)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
        public static float min(float a, float b)
        {
            throw new InvalidOperationException("GpuMath can only be used in [GpuKernel] methods.");
        }
    }
}
