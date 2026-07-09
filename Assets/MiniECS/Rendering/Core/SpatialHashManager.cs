using UnityEngine;

namespace MiniECS.Rendering
{
    public static class SpatialHashManager
    {
        static SpatialHash _instance;
        static bool _initialized;

        public static ref SpatialHash Instance
        {
            get
            {
                if (!_initialized)
                    Debug.LogError("[SpatialHashManager] Not initialized. Call Initialize() first.");
                return ref _instance;
            }
        }

        public static bool IsCreated => _initialized && _instance.IsCreated;

        public static void Initialize(float chunkSize, int maxEntities, int maxChunks, int bucketCount)
        {
            if (_initialized)
            {
                _instance.Dispose();
                _initialized = false;
            }
            _instance = new SpatialHash(chunkSize, maxEntities, maxChunks, bucketCount);
            _initialized = true;
            Debug.Log($"[SpatialHashManager] Initialized: chunkSize={chunkSize} maxEntities={maxEntities} maxChunks={maxChunks}");
        }

        public static void Dispose()
        {
            if (_initialized)
            {
                _instance.Dispose();
                _initialized = false;
            }
        }
    }
}
