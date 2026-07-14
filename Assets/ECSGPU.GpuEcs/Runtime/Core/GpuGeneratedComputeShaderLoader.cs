using System;
using UnityEngine;

namespace ECSGPU.GpuEcs
{
    public static class GpuGeneratedComputeShaderLoader
    {
        const string ResourceFolder = "ECSGPU.GpuEcs/Generated/ComputeShaders";

        public static ComputeShader Load<TSystem>() where TSystem : struct
        {
            return Load(typeof(TSystem));
        }

        public static ComputeShader Load(Type systemType)
        {
            if (systemType == null)
                return null;

            return Resources.Load<ComputeShader>($"{ResourceFolder}/{systemType.Name}");
        }
    }
}
