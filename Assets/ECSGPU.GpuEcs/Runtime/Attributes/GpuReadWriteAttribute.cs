using System;

namespace ECSGPU.GpuEcs
{
    [AttributeUsage(AttributeTargets.Field)]
    public class GpuReadWriteAttribute : Attribute
    {
    }
}
