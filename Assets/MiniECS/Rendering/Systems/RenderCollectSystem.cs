using MiniECS;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct RenderCollectSystem : ISystem, IClientSystem, IParallelBatchSystem
    {
        static NativeArray<int> modelOfEntity;
        static NativeArray<int> slotForEntity;
        static NativeArray<Vector3> positionCache;
        static NativeArray<Quaternion> rotationCache;
        static NativeArray<Matrix4x4> flatMatrices;
        static NativeArray<int> prefix;
        static int modelCount;
        static int validCount;

        public void OnCreate(ref SystemContext ctx) { }

        public void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize)
        {
            totalWork = 0;
            batchSize = 0;
            RenderCollectSystem.validCount = 0;

            var posPool = EntityManager.Pool<PositionComponent>();
            var rotPool = EntityManager.Pool<RotationComponent>();
            var modPool = EntityManager.Pool<ModelIdComponent>();

            var positions = posPool.components;
            var rotations = rotPool.components;
            var modelIds = modPool.components;
            var entities = posPool.entities;
            var rotLookup = rotPool.lookup;
            var modLookup = modPool.lookup;

            int count = entities.Count;
            int mc = ModelRegistry.Count;
            if (count == 0 || mc == 0)
                return;

            var batch = BatchData.Instance;
            batch.EnsureModelCapacity(mc);
            batch.ClearCounts(mc);

            int[] tempModelIds = new int[count];
            int[] validIndices = new int[count];
            int validCount = 0;

            for (int i = 0; i < count; i++)
            {
                int id = entities[i];
                if (rotLookup.TryGetValue(id, out _) && modLookup.TryGetValue(id, out int modIdx))
                {
                    int modelId = modelIds[modIdx].Value;
                    if (modelId >= 0 && modelId < mc)
                    {
                        batch.GetCount(modelId)++;
                        validIndices[validCount] = i;
                        tempModelIds[validCount] = modelId;
                        validCount++;
                    }
                }
            }

            if (validCount == 0)
                return;

            int[] prefixArr = new int[mc];
            int running = 0;
            for (int m = 0; m < mc; m++)
            {
                int c = batch.GetCount(m);
                prefixArr[m] = running;
                running += c;
                if (c > 0)
                    batch.SetMatrices(m, new Matrix4x4[c], c);
            }

            int[] cursor = new int[mc];

            int[] modelOfEntityArr = new int[validCount];
            int[] slotForEntityArr = new int[validCount];

            for (int i = 0; i < validCount; i++)
            {
                int modelId = tempModelIds[i];
                modelOfEntityArr[i] = modelId;
                slotForEntityArr[i] = cursor[modelId];
                cursor[modelId]++;
            }

            Vector3[] posArr = new Vector3[validCount];
            Quaternion[] rotArr = new Quaternion[validCount];

            for (int i = 0; i < validCount; i++)
            {
                int poolIdx = validIndices[i];
                int id = entities[poolIdx];
                posArr[i] = positions[poolIdx].Value;
                rotArr[i] = rotations[rotLookup[id]].Value;
            }

            DisposeOld();
            modelOfEntity = new NativeArray<int>(modelOfEntityArr, Allocator.TempJob);
            slotForEntity = new NativeArray<int>(slotForEntityArr, Allocator.TempJob);
            positionCache = new NativeArray<Vector3>(posArr, Allocator.TempJob);
            rotationCache = new NativeArray<Quaternion>(rotArr, Allocator.TempJob);
            flatMatrices = new NativeArray<Matrix4x4>(validCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            prefix = new NativeArray<int>(prefixArr, Allocator.TempJob);
            modelCount = mc;
            RenderCollectSystem.validCount = validCount;

            totalWork = validCount;
            batchSize = 64;
        }

        public JobHandle ScheduleBatches(ref SystemContext ctx, int totalWork, int batchSize, JobHandle dep)
        {
            var job = new RenderBatchJob
            {
                ModelOfEntity = modelOfEntity,
                SlotForEntity = slotForEntity,
                Positions = positionCache,
                Rotations = rotationCache,
                Prefix = prefix,
                FlatMatrices = flatMatrices
            };
            return job.Schedule(totalWork, batchSize, dep);
        }

        public static void Scatter()
        {
            if (validCount <= 0)
                return;

            var matricesArr = flatMatrices.ToArray();
            var prefixArr = prefix.ToArray();
            var modelOfEntityArr = modelOfEntity.ToArray();
            var slotForEntityArr = slotForEntity.ToArray();

            for (int i = 0; i < validCount; i++)
            {
                int modelId = modelOfEntityArr[i];
                int slot = slotForEntityArr[i];
                int writeIdx = prefixArr[modelId] + slot;
                BatchData.Instance.GetMatrices(modelId)[slot] = matricesArr[writeIdx];
            }

            DisposeOld();
        }

        static void DisposeOld()
        {
            if (modelOfEntity.IsCreated) modelOfEntity.Dispose();
            if (slotForEntity.IsCreated) slotForEntity.Dispose();
            if (positionCache.IsCreated) positionCache.Dispose();
            if (rotationCache.IsCreated) rotationCache.Dispose();
            if (flatMatrices.IsCreated) flatMatrices.Dispose();
            if (prefix.IsCreated) prefix.Dispose();
        }

        public void OnUpdate(ref SystemContext ctx) { }

        public void OnDestroy(ref SystemContext ctx)
        {
            DisposeOld();
            validCount = 0;
        }
    }
}
