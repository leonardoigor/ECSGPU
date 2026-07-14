using UnityEngine;

namespace ECSGPU.GpuEcs
{
    [DisallowMultipleComponent]
    public sealed class GpuWorldDebug : MonoBehaviour
    {
        public GpuWorld World { get; set; }
    }
}
