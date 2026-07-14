using System;

namespace ECSGPU.GpuEcs
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class GpuKernelAttribute : Attribute
    {
    }
}
