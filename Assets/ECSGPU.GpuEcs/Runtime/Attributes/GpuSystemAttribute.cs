using System;

namespace ECSGPU.GpuEcs
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class GpuSystemAttribute : Attribute
    {
        public string KernelName { get; }
        public int ThreadX { get; set; } = 64;
        public int ThreadY { get; set; } = 1;
        public int ThreadZ { get; set; } = 1;

        public GpuSystemAttribute(string kernelName)
        {
            KernelName = kernelName;
        }
    }
}
