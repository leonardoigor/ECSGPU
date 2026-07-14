using System;

namespace ECSGPU.GpuEcs
{
    [AttributeUsage(AttributeTargets.Field)]
    public class GpuReadOnlyAttribute : Attribute
    {
    }
}
