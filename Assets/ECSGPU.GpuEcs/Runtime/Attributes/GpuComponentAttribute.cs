using System;

namespace ECSGPU.GpuEcs
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class GpuComponentAttribute : Attribute
    {
        public string HlslName { get; set; }
    }
}
