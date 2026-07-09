using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct AoISystem : ISystem, IClientSystem, IMainThreadSystem
    {
        static NativeList<int> _activeEntities;
        static NativeArray<int> _prevChunkKeys;
        static bool _initialized;
        static float _queryRadius;
        static int _prevKeysLength;

        public static NativeList<int> GetActiveEntities() => _activeEntities;
        public static int ActiveCount => _activeEntities.IsCreated ? _activeEntities.Length : 0;
        public static float QueryRadius { get => _queryRadius; set => _queryRadius = value; }

        public void OnCreate(ref SystemContext ctx)
        {
            _activeEntities = new NativeList<int>(Allocator.Persistent);
            _queryRadius = 100f;
            _prevKeysLength = 0;
            _initialized = true;
        }

        public void OnUpdate(ref SystemContext ctx)
        {
            if (!_initialized) return;

            ref var hash = ref SpatialHashManager.Instance;
            if (!hash.IsCreated) return;

            var cam = Camera.main;
            if (cam == null) return;

            var pos = cam.transform.position;

            SyncPositions(ref hash);

            _activeEntities.Clear();
            hash.CollectEntitiesInRadius(pos.x, pos.z, _queryRadius, _activeEntities);
        }

        static void SyncPositions(ref SpatialHash hash)
        {
            var posPool = EntityManager.Pool<PositionComponent>();

            for (int i = 0; i < _activeEntities.Length; i++)
            {
                int id = _activeEntities[i];
                if (!posPool.Has(id)) continue;

                int idx = posPool.GetIndex(id);
                Vector3 p = posPool.components[idx].Value;
                int key = SpatialHash.KeyFromPosition(p.x, p.z, hash.ChunkSize);

                EnsurePrevKeysCapacity(id + 1);

                if (_prevChunkKeys[id] != key)
                {
                    hash.MoveEntity(id, p);
                    _prevChunkKeys[id] = key;
                }
            }
        }

        static void EnsurePrevKeysCapacity(int minSize)
        {
            if (_prevChunkKeys.IsCreated && _prevChunkKeys.Length >= minSize)
                return;

            var newArr = new NativeArray<int>(minSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (_prevChunkKeys.IsCreated)
            {
                NativeArray<int>.Copy(_prevChunkKeys, newArr, _prevChunkKeys.Length);
                _prevChunkKeys.Dispose();
            }
            _prevChunkKeys = newArr;
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            if (_activeEntities.IsCreated)
                _activeEntities.Dispose();
            if (_prevChunkKeys.IsCreated)
                _prevChunkKeys.Dispose();
            _initialized = false;
        }
    }
}
