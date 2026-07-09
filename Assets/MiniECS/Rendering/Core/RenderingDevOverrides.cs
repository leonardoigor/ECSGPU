using UnityEngine;

namespace MiniECS.Rendering
{
    public sealed class RenderingDevOverrides : MonoBehaviour
    {
        public float chunkSize = 20f;
        public int maxEntities = 100000;
        public int maxChunks = 4096;
        public int bucketCount = 8192;
        public float aoiRadius = 80f;
        public bool rebuildIfCreated = true;

        void Start()
        {
            SpatialHashManager.ConfigureDefaults(chunkSize, maxEntities, maxChunks, bucketCount, rebuildIfCreated);
            SpatialHashManager.EnsureInitialized();
            AoISystem.QueryRadius = aoiRadius;
        }
    }
}

