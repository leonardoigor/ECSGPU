using Unity.Mathematics;

namespace ECSGPU.GpuEcs
{
    /// <summary>
    /// GPU collider broad+narrow phase. Uses an O(n^2) all-pairs loop (like the
    /// NBody example) with bounding-sphere collision, since the kernel transpiler
    /// only transpiles the [GpuKernel] body inline (no helper methods, no switch,
    /// no local arrays, no quaternion math). World centers are precomputed on the
    /// CPU by GpuCollisionBridge. Each thread handles entity idx and only resolves
    /// pairs (idx, other) with other > idx, pushing the lower-index entity out of
    /// penetration - matching the asymmetric CPU resolution.
    /// </summary>
    [GpuSystem("CSGpuCollisionDetect", ThreadX = 256)]
    public partial struct GpuCollisionDetectKernel
    {
        [GpuReadWrite] public GpuComponentBuffer<GpuPositionComponent> _positions;
        [GpuReadWrite] public GpuComponentBuffer<GpuColliderComponent> _colliders;
        [GpuReadWrite] public GpuComponentBuffer<GpuCollisionResultComponent> _results;
        public uint _entityCount;
        public float _resolve;

        [GpuKernel]
        public void Execute()
        {
            uint idx = GpuDispatch.threadId.x;
            if (idx >= _entityCount) return;

            float3 posA = _positions[idx].Value;
            GpuColliderComponent colA = _colliders[idx];
            float3 centerA = colA.WorldCenter;

            GpuCollisionResultComponent myResult;
            myResult.OtherId = 0;
            myResult.Normal = new float3(0.0f, 0.0f, 0.0f);
            myResult.Depth = 0.0f;
            myResult.IsTrigger = 0;

            float3 push = new float3(0.0f, 0.0f, 0.0f);

            for (uint other = 0; other < _entityCount; other++)
            {
                if (other <= idx) continue;

                float3 posB = _positions[other].Value;
                GpuColliderComponent colB = _colliders[other];
                float3 centerB = colB.WorldCenter;

                float3 d = centerB - centerA;
                float dist = GpuMath.length(d);
                float rsum = colA.BoundingRadius + colB.BoundingRadius;

                if (dist < rsum)
                {
                    float3 n;
                    if (dist > 0.0001f)
                    {
                        n = d / dist;
                    }
                    else
                    {
                        n = new float3(1.0f, 0.0f, 0.0f);
                    }

                    float depth = rsum - dist;
                    int isTrigger = colA.IsTrigger | colB.IsTrigger;

                    if (depth > myResult.Depth)
                    {
                        myResult.OtherId = (int)other;
                        myResult.Normal = n;
                        myResult.Depth = depth;
                        myResult.IsTrigger = isTrigger;
                    }

                    if (_resolve > 0.5f && isTrigger == 0)
                    {
                        push = push - n * depth;
                    }
                }
            }

            _results[idx] = myResult;

            if (_resolve > 0.5f)
            {
                GpuPositionComponent p;
                p.Value = posA + push;
                _positions[idx] = p;
            }
        }
    }
}
