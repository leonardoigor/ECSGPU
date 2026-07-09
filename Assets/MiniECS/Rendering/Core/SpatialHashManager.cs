using MiniECS;
using UnityEngine;

namespace MiniECS.Rendering
{
    public static class SpatialHashManager
    {
        static SpatialHash _instance;
        static bool _initialized;
        static float _defaultChunkSize = 20f;
        static int _defaultMaxEntities = 100000;
        static int _defaultMaxChunks = 4096;
        static int _defaultBucketCount = 8192;

        public static ref SpatialHash Instance
        {
            get
            {
                EnsureInitialized();
                return ref _instance;
            }
        }

        public static bool IsCreated => _initialized && _instance.IsCreated;

        public static void ConfigureDefaults(float chunkSize, int maxEntities, int maxChunks, int bucketCount, bool rebuildIfCreated = false)
        {
            _defaultChunkSize = chunkSize;
            _defaultMaxEntities = Mathf.Max(1, maxEntities);
            _defaultMaxChunks = Mathf.Max(1, maxChunks);
            _defaultBucketCount = Mathf.Max(1, bucketCount);

            if (rebuildIfCreated && IsCreated)
                Rebuild(_defaultChunkSize, _defaultMaxEntities, _defaultMaxChunks, _defaultBucketCount);
        }

        public static void EnsureInitialized(int minMaxEntities = 0)
        {
            if (IsCreated)
                return;

            int maxEntities = Mathf.Max(_defaultMaxEntities, minMaxEntities);
            Initialize(_defaultChunkSize, maxEntities, _defaultMaxChunks, _defaultBucketCount);
        }

        public static void EnsureCapacityForEntity(int entityId)
        {
            if (!IsCreated)
            {
                EnsureInitialized(entityId);
                return;
            }

            if (entityId <= _instance.MaxEntities)
                return;

            int newMaxEntities = Mathf.NextPowerOfTwo(entityId + 1);
            Rebuild(_instance.ChunkSize, newMaxEntities, _instance.MaxChunks, _instance.BucketCount);
        }

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

        static void Rebuild(float chunkSize, int maxEntities, int maxChunks, int bucketCount)
        {
            if (_initialized)
            {
                _instance.Dispose();
                _initialized = false;
            }

            _instance = new SpatialHash(chunkSize, maxEntities, maxChunks, bucketCount);
            _initialized = true;

            var posPool = EntityManager.Pool<PositionComponent>();
            foreach (var id in EntityManager.GetActiveEntityIds())
            {
                if (!posPool.Has(id))
                    continue;

                int idx = posPool.GetIndex(id);
                _instance.InsertEntity(id, posPool.components[idx].Value);
            }
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
