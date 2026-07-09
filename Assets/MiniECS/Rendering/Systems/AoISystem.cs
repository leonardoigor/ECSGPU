using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct AoISystem : ISystem, IClientSystem, IMainThreadSystem
    {
        static NativeList<int> _activeEntities;
        static bool _initialized;
        static float _queryRadius;

        public static NativeList<int> GetActiveEntities() => _activeEntities;
        public static int ActiveCount => _activeEntities.IsCreated ? _activeEntities.Length : 0;
        public static float QueryRadius { get => _queryRadius; set => _queryRadius = value; }

        public void OnCreate(ref SystemContext ctx)
        {
            _activeEntities = new NativeList<int>(Allocator.Persistent);
            _queryRadius = 80f;
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

            _activeEntities.Clear();
            hash.CollectEntitiesInRadius(pos.x, pos.z, _queryRadius, _activeEntities);
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            if (_activeEntities.IsCreated)
                _activeEntities.Dispose();
            _initialized = false;
        }
    }
}
