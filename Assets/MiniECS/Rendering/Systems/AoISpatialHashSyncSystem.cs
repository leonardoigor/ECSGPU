using MiniECS;
using Unity.Collections;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct AoISpatialHashSyncSystem : ISystem, IClientSystem, IMainThreadSystem
    {
        static NativeArray<int> _prevKeysPlusOne;
        static bool _initialized;

        public void OnCreate(ref SystemContext ctx)
        {
            _initialized = true;
        }

        public void OnUpdate(ref SystemContext ctx)
        {
            if (!_initialized) return;

            SpatialHashManager.EnsureInitialized();
            if (!SpatialHashManager.IsCreated) return;

            var posPool = EntityManager.Pool<PositionComponent>();

            int maxId = 0;
            foreach (var id in EntityManager.GetActiveEntityIds())
            {
                if (!posPool.Has(id))
                    continue;
                if (id > maxId) maxId = id;
            }

            if (maxId == 0)
                return;

            SpatialHashManager.EnsureCapacityForEntity(maxId);
            ref var hash = ref SpatialHashManager.Instance;
            if (!hash.IsCreated) return;

            EnsureCapacity(maxId + 1);

            foreach (var id in EntityManager.GetActiveEntityIds())
            {
                if (!posPool.Has(id))
                    continue;

                int idx = posPool.GetIndex(id);
                Vector3 p = posPool.components[idx].Value;
                int keyPlusOne = SpatialHash.KeyFromPosition(p.x, p.z, hash.ChunkSize) + 1;

                int prev = _prevKeysPlusOne[id];
                if (prev == 0)
                {
                    hash.InsertEntity(id, p);
                    _prevKeysPlusOne[id] = keyPlusOne;
                }
                else if (prev != keyPlusOne)
                {
                    hash.MoveEntity(id, p);
                    _prevKeysPlusOne[id] = keyPlusOne;
                }
            }
        }

        static void EnsureCapacity(int minSize)
        {
            if (_prevKeysPlusOne.IsCreated && _prevKeysPlusOne.Length >= minSize)
                return;

            var newArr = new NativeArray<int>(minSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (_prevKeysPlusOne.IsCreated)
            {
                NativeArray<int>.Copy(_prevKeysPlusOne, newArr, _prevKeysPlusOne.Length);
                _prevKeysPlusOne.Dispose();
            }
            _prevKeysPlusOne = newArr;
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            if (_prevKeysPlusOne.IsCreated)
                _prevKeysPlusOne.Dispose();
            _initialized = false;
        }
    }
}

