using MiniECS;
using MiniECS.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MiniECS.Physics
{
    /// <summary>
    /// CPU collider system (MiniECS). Runs a uniform-grid broad phase and a
    /// GJK/EPA narrow phase over every collider. Solid pairs are resolved by
    /// pushing the lower-index entity out of penetration; triggers only fire
    /// events. Skipped entirely when CollisionConfig.RunCollidersOnGpu is set.
    /// Auto-registered via SystemRegistry.RegisterAll.
    /// </summary>
    public struct CollisionSystem : ISystem, IParallelBatchSystem
    {
        static NativeArray<int> _ids;
        static NativeArray<float3> _pos;
        static NativeArray<quaternion> _rot;
        static NativeArray<ColliderComponent> _col;
        static NativeArray<CollisionResultComponent> _res;
        static NativeArray<float3> _push;
        static NativeArray<float3> _centers;
        static NativeArray<int3> _cellCoord;
        static NativeMultiHashMap<int, int> _grid;
        static NativeArray<int> _evtCount;
        static NativeArray<CollisionEvent> _evtBuf;
        static NativeHashSet<long> _prevPairs;
        static float _cellSize;
        static int _maxContacts;

        public void OnCreate(ref SystemContext ctx) { }
        public void OnUpdate(ref SystemContext ctx) { }
        public void OnDestroy(ref SystemContext ctx)
        {
            DisposeBuffers();
            if (_prevPairs.IsCreated) _prevPairs.Dispose();
            CollisionEvents.Dispose();
        }

        static int HashCell(int3 c) => (c.x * 73856093) ^ (c.y * 19349663) ^ (c.z * 83492791);

        static void DisposeBuffers()
        {
            if (_ids.IsCreated) _ids.Dispose();
            if (_pos.IsCreated) _pos.Dispose();
            if (_rot.IsCreated) _rot.Dispose();
            if (_col.IsCreated) _col.Dispose();
            if (_res.IsCreated) _res.Dispose();
            if (_push.IsCreated) _push.Dispose();
            if (_centers.IsCreated) _centers.Dispose();
            if (_grid.IsCreated) _grid.Dispose();
            if (_cellCoord.IsCreated) _cellCoord.Dispose();
            if (_evtCount.IsCreated) _evtCount.Dispose();
            if (_evtBuf.IsCreated) _evtBuf.Dispose();
            _ids = default; _pos = default; _rot = default; _col = default;
            _res = default; _push = default; _centers = default; _cellCoord = default; _grid = default;
            _evtCount = default; _evtBuf = default;
        }

        public void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize)
        {
            DisposeBuffers();
            totalWork = 0;
            batchSize = 0;

            if (CollisionConfig.RunCollidersOnGpu)
                return;

            var colPool = EntityManager.Pool<ColliderComponent>();
            var posPool = EntityManager.Pool<PositionComponent>();
            var rotPool = EntityManager.Pool<RotationComponent>();

            int valid = 0;
            for (int i = 0; i < colPool.count; i++)
            {
                int id = colPool.entities[i];
                if (posPool.Has(id)) valid++;
            }
            if (valid == 0) return;

            _ids = new NativeArray<int>(valid, Allocator.TempJob);
            _pos = new NativeArray<float3>(valid, Allocator.TempJob);
            _rot = new NativeArray<quaternion>(valid, Allocator.TempJob);
            _col = new NativeArray<ColliderComponent>(valid, Allocator.TempJob);
            _res = new NativeArray<CollisionResultComponent>(valid, Allocator.TempJob);
            _push = new NativeArray<float3>(valid, Allocator.TempJob);
            _centers = new NativeArray<float3>(valid, Allocator.TempJob);
            _cellCoord = new NativeArray<int3>(valid, Allocator.TempJob);

            _maxContacts = CollisionConfig.MaxContactsPerEntity;
            _evtCount = new NativeArray<int>(valid, Allocator.TempJob);
            _evtBuf = new NativeArray<CollisionEvent>(valid * _maxContacts, Allocator.TempJob);

            float maxDiag = 0f;
            int write = 0;
            for (int i = 0; i < colPool.count; i++)
            {
                int id = colPool.entities[i];
                if (!posPool.Has(id)) continue;

                int posIndex = posPool.GetIndex(id);
                var c = colPool.components[colPool.GetIndex(id)];

                quaternion rot = rotPool.Has(id)
                    ? (quaternion)rotPool.components[rotPool.GetIndex(id)].Value
                    : quaternion.identity;

                float3 worldCenter = (float3)posPool.components[posIndex].Value + math.mul(rot, c.CenterOffset);

                _ids[write] = id;
                _centers[write] = worldCenter;
                _pos[write] = (float3)posPool.components[posIndex].Value;
                _rot[write] = rot;
                _col[write] = c;

                float3 hvec = AabbHalfExtents(c, rot);
                maxDiag = math.max(maxDiag, math.length(hvec));
                write++;
            }

            float cfgCell = CollisionConfig.BroadPhaseCellSize;
            _cellSize = maxDiag > 1e-4f ? math.max(cfgCell, 2f * maxDiag) : cfgCell;
            _cellSize = math.max(_cellSize, 1e-3f);

            _grid = new NativeMultiHashMap<int, int>(valid * 27 + 1, Allocator.TempJob);
            for (int i = 0; i < valid; i++)
            {
                int3 cell = (int3)math.floor(_centers[i] / _cellSize);
                _cellCoord[i] = cell;
                _grid.Add(HashCell(cell), i);
            }

            totalWork = valid;
            batchSize = 64;
        }

        static float3 AabbHalfExtents(in ColliderComponent c, quaternion rot)
        {
            if (c.Shape == ColliderShapeType.Sphere)
                return new float3(c.Radius);
            if (c.Shape == ColliderShapeType.Box)
            {
                float3 r = math.mul(rot, c.HalfExtents);
                return new float3(math.abs(r.x), math.abs(r.y), math.abs(r.z));
            }
            if (c.Shape == ColliderShapeType.Capsule)
            {
                float h = math.max(0f, (c.CapsuleHeight - 2f * c.Radius) * 0.5f);
                float3 axis = c.CapsuleAxis == 0 ? new float3(1f, 0f, 0f)
                    : c.CapsuleAxis == 1 ? new float3(0f, 1f, 0f) : new float3(0f, 0f, 1f);
                float3 a = math.abs(math.mul(rot, axis)) * (h + c.Radius);
                return a + new float3(c.Radius);
            }
            // ConvexMesh: bounding radius from hull points
            if (c.MeshHullIndex >= 0)
            {
                ConvexHullRegistry.GetHull(c.MeshHullIndex, out var pts, out int start, out int count);
                float r = 0f;
                for (int i = 0; i < count; i++)
                {
                    float d = math.length(pts[start + i]);
                    if (d > r) r = d;
                }
                return new float3(r);
            }
            return new float3(c.Radius);
        }

        [BurstCompile]
        struct NarrowPhaseJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Ids;
            [ReadOnly] public NativeArray<float3> Pos;
            [ReadOnly] public NativeArray<quaternion> Rot;
            [ReadOnly] public NativeArray<ColliderComponent> Col;
            [ReadOnly] public NativeArray<int3> CellCoord;
            [ReadOnly] public NativeMultiHashMap<int, int> Grid;
            public float CellSize;
            public int MaxContacts;
            public int Resolve;
            public NativeArray<CollisionResultComponent> Res;
            public NativeArray<float3> Push;
            public NativeArray<int> EvtCount;
            public NativeArray<CollisionEvent> EvtBuf;

            public void Execute(int i)
            {
                CollisionResultComponent res = default;
                float3 push = float3.zero;
                int evtN = 0;

                CollisionMath.CShape a = CollisionMath.BuildShape(Col[i], Pos[i], Rot[i]);
                int3 baseCell = CellCoord[i];

                for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    int3 cell = baseCell + new int3(ox, oy, oz);
                    int h = HashCellLocal(cell);
                    if (Grid.TryGetFirstValue(h, out int j, out var it))
                    {
                        do
                        {
                            if (j == i) continue;
                            if (CellCoord[j].x != cell.x || CellCoord[j].y != cell.y || CellCoord[j].z != cell.z)
                                continue;

                            CollisionMath.CShape b = CollisionMath.BuildShape(Col[j], Pos[j], Rot[j]);
                            if (CollisionMath.Collide(a, b, out float3 n, out float depth))
                            {
                                int isTrigger = (a.IsTrigger | b.IsTrigger);
                                if (depth > res.Depth)
                                {
                                    res.OtherId = Ids[j];
                                    res.Normal = n;
                                    res.Depth = depth;
                                    res.IsTrigger = isTrigger;
                                }
                                if (Resolve != 0 && isTrigger == 0 && i < j)
                                    push -= n * depth;
                                if (i < j && evtN < MaxContacts)
                                {
                                    CollisionEvent e;
                                    e.EntityA = Ids[i];
                                    e.EntityB = Ids[j];
                                    e.Normal = n;
                                    e.Penetration = depth;
                                    e.IsTrigger = isTrigger;
                                    e.Phase = CollisionPhase.Enter;
                                    EvtBuf[i * MaxContacts + evtN] = e;
                                    evtN++;
                                }
                            }
                        } while (Grid.TryGetNextValue(out j, ref it));
                    }
                }

                Res[i] = res;
                Push[i] = push;
                EvtCount[i] = evtN;
            }

            static int HashCellLocal(int3 c) => (c.x * 73856093) ^ (c.y * 19349663) ^ (c.z * 83492791);
        }

        public JobHandle ScheduleBatches(ref SystemContext ctx, int totalWork, int batchSize, JobHandle dep)
        {
            if (totalWork <= 0) return default;

            var job = new NarrowPhaseJob
            {
                Ids = _ids,
                Pos = _pos,
                Rot = _rot,
                Col = _col,
                CellCoord = _cellCoord,
                Grid = _grid,
                CellSize = _cellSize,
                MaxContacts = _maxContacts,
                Resolve = CollisionConfig.ResolvePenetration ? 1 : 0,
                Res = _res,
                Push = _push,
                EvtCount = _evtCount,
                EvtBuf = _evtBuf
            };

            var handle = job.Schedule(totalWork, batchSize, dep);
            handle.Complete();

            ProcessEvents(totalWork);
            Writeback(totalWork);
            DisposeBuffers();
            return default;
        }

        static long PairKey(int a, int b)
        {
            long lo = math.min(a, b);
            long hi = math.max(a, b);
            return (lo << 32) | (uint)hi;
        }

        static void ProcessEvents(int total)
        {
            CollisionEvents.ClearFrame();
            var cur = new System.Collections.Generic.HashSet<long>();

            for (int i = 0; i < total; i++)
            {
                int n = _evtCount[i];
                if (n == 0) continue;
                for (int k = 0; k < n; k++)
                {
                    CollisionEvent e = _evtBuf[i * _maxContacts + k];
                    long key = PairKey(e.EntityA, e.EntityB);
                    bool wasIn = _prevPairs.IsCreated && _prevPairs.Contains(key);
                    e.Phase = wasIn ? CollisionPhase.Stay : CollisionPhase.Enter;
                    cur.Add(key);
                    if (CollisionConfig.GenerateEvents)
                        CollisionEvents.List.Add(e);
                }
            }

            if (_prevPairs.IsCreated)
            {
                var enumerator = _prevPairs.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    long key = enumerator.Current;
                    if (!cur.Contains(key))
                    {
                    int lo = (int)(key >> 32);
                    int hi = (int)(key & 0xffffffffL);
                        CollisionEvent e;
                        e.EntityA = lo;
                        e.EntityB = hi;
                        e.Normal = float3.zero;
                        e.Penetration = 0f;
                        e.IsTrigger = 0;
                        e.Phase = CollisionPhase.Exit;
                        if (CollisionConfig.GenerateEvents)
                            CollisionEvents.List.Add(e);
                    }
                }
                _prevPairs.Dispose();
            }

            CollisionEvents.Publish();

            _prevPairs = new NativeHashSet<long>(cur.Count + 1, Allocator.Persistent);
            foreach (var k in cur) _prevPairs.Add(k);
        }

        static void Writeback(int total)
        {
            var posPool = EntityManager.Pool<PositionComponent>();
            var resPool = EntityManager.Pool<CollisionResultComponent>();

            for (int i = 0; i < total; i++)
            {
                int id = _ids[i];
                float3 p = _push[i];
                if (p.x != 0f || p.y != 0f || p.z != 0f)
                {
                    int pi = posPool.GetIndex(id);
                    var pos = posPool.components[pi];
                    pos.Value = (Vector3)(_pos[i] + p);
                    posPool.components[pi] = pos;
                }
                resPool.Add(id, _res[i]);
            }
        }
    }
}
