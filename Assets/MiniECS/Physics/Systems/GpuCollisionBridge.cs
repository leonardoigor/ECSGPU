using MiniECS;
using MiniECS.Rendering;
using Unity.Mathematics;
using UnityEngine;
using ECSGPU.GpuEcs;

namespace MiniECS.Physics
{
    /// <summary>
    /// Glue between the MiniECS CPU collider data and the GPU collision kernel.
    /// When CollisionConfig.RunCollidersOnGpu is set, this system uploads
    /// Position/Rotation/Collider into a GpuWorld, dispatches
    /// GpuCollisionDetectKernel (bounding-sphere narrow phase), and writes the
    /// resolved positions + CollisionResultComponent back into MiniECS.
    /// Lives in Assembly-CSharp so it can reference both MiniECS and ECSGPU.GpuEcs.
    /// </summary>
    public struct GpuCollisionBridge : ISystem, IMainThreadSystem
    {
        GpuWorld _world;
        ComputeShader _shader;

        public void OnCreate(ref SystemContext ctx)
        {
            _world = new GpuWorld();
            _shader = GpuGeneratedComputeShaderLoader.Load<GpuCollisionDetectKernel>();
            if (_shader == null)
                Debug.LogError("[GpuCollisionBridge] Generated compute shader 'GpuCollisionDetectKernel' not found. Recompile may be required.");
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            if (_world != null) _world.Dispose();
            _world = null;
        }

        public void OnUpdate(ref SystemContext ctx)
        {
            if (!CollisionConfig.RunCollidersOnGpu) return;
            if (_shader == null) return;

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

            var positions = new GpuPositionComponent[valid];
            var colliders = new GpuColliderComponent[valid];
            var results = new GpuCollisionResultComponent[valid];
            var ids = new int[valid];

            int w = 0;
            for (int i = 0; i < colPool.count; i++)
            {
                int id = colPool.entities[i];
                if (!posPool.Has(id)) continue;

                int pi = posPool.GetIndex(id);
                var c = colPool.components[colPool.GetIndex(id)];
                quaternion rot = rotPool.Has(id)
                    ? (quaternion)rotPool.components[rotPool.GetIndex(id)].Value
                    : quaternion.identity;

                positions[w].Value = (float3)posPool.components[pi].Value;
                colliders[w].Shape = (int)c.Shape;
                colliders[w].IsTrigger = c.IsTrigger;
                colliders[w].WorldCenter = (float3)posPool.components[pi].Value + math.mul(rot, c.CenterOffset);
                colliders[w].BoundingRadius = BoundingRadius(c);
                ids[w] = id;
                w++;
            }

            _world.SetComponentData(positions);
            _world.SetComponentData(colliders);
            _world.SetComponentData(results);

            var kernel = new GpuCollisionDetectKernel
            {
                _positions = _world.GetBuffer<GpuPositionComponent>(),
                _colliders = _world.GetBuffer<GpuColliderComponent>(),
                _results = _world.GetBuffer<GpuCollisionResultComponent>(),
                _entityCount = (uint)valid,
                _resolve = CollisionConfig.ResolvePenetration ? 1f : 0f
            };
            kernel.Execute(_shader, _world, valid);

            _world.GetBuffer<GpuPositionComponent>().GetData(positions);
            _world.GetBuffer<GpuCollisionResultComponent>().GetData(results);

            var resPool = EntityManager.Pool<CollisionResultComponent>();
            for (int i = 0; i < valid; i++)
            {
                int id = ids[i];

                if (CollisionConfig.ResolvePenetration)
                {
                    int pi = posPool.GetIndex(id);
                    var p = posPool.components[pi];
                    p.Value = (Vector3)positions[i].Value;
                    posPool.components[pi] = p;
                }

                resPool.Add(id, new CollisionResultComponent
                {
                    OtherId = results[i].OtherId,
                    Normal = results[i].Normal,
                    Depth = results[i].Depth,
                    IsTrigger = results[i].IsTrigger
                });
            }
        }

        static float BoundingRadius(ColliderComponent c)
        {
            if (c.Shape == ColliderShapeType.Sphere)
                return c.Radius;
            if (c.Shape == ColliderShapeType.Box)
                return math.length(c.HalfExtents);
            if (c.Shape == ColliderShapeType.Capsule)
            {
                float h = math.max(0f, (c.CapsuleHeight - 2f * c.Radius) * 0.5f);
                return h + c.Radius;
            }
            if (c.Shape == ColliderShapeType.ConvexMesh && c.MeshHullIndex >= 0)
            {
                ConvexHullRegistry.GetHull(c.MeshHullIndex, out var pts, out int start, out int count);
                float r = 0f;
                for (int i = 0; i < count; i++)
                {
                    float d = math.length(pts[start + i]);
                    if (d > r) r = d;
                }
                return r;
            }
            return c.Radius;
        }
    }
}
