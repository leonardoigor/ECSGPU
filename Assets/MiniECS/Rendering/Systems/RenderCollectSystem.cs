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
        static NativeArray<Vector4> animCache;
        static NativeArray<Color> colorCache;
        static NativeArray<InstanceData> flatInstanceData;
        static NativeArray<int> prefix;
        static int modelCount;
        static int validCount;

        static Plane[] frustumPlanes = new Plane[6];
        static Camera cachedCamera;
        static float cullingRadius = 2.0f;

        public void OnCreate(ref SystemContext ctx) { }

        public void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize)
        {
            totalWork = 0;
            batchSize = 0;
            RenderCollectSystem.validCount = 0;

            var posPool = EntityManager.Pool<PositionComponent>();
            var rotPool = EntityManager.Pool<RotationComponent>();
            var modPool = EntityManager.Pool<ModelIdComponent>();
            var animPool = EntityManager.Pool<AnimationComponent>();
            var colorPool = EntityManager.Pool<ColorComponent>();

            var positions = posPool.components;
            var rotations = rotPool.components;
            var modelIds = modPool.components;
            var anims = animPool.components;
            var active = AoISystem.GetActiveEntities();

            int mc = ModelRegistry.Count;
            if (!active.IsCreated || active.Length == 0 || mc == 0)
                return;

            var batch = BatchData.Instance;
            batch.EnsureModelCapacity(mc);
            batch.ClearCounts(mc);

            if (cachedCamera == null)
                cachedCamera = Camera.main;
            if (cachedCamera != null)
                GeometryUtility.CalculateFrustumPlanes(cachedCamera, frustumPlanes);

            var lodPool = EntityManager.Pool<LodComponent>();

            int activeCount = active.Length;
            int[] tempModelIds = new int[activeCount];
            int[] tempLodLevels = new int[activeCount];
            float[] tempAnimFrames = new float[activeCount];
            int[] tempEntityIds = new int[activeCount];
            int validCount = 0;

            for (int i = 0; i < activeCount; i++)
            {
                int id = active[i];
                if (!rotPool.Has(id) || !modPool.Has(id))
                    continue;

                int modIdx = modPool.GetIndex(id);
                int baseModelId = modelIds[modIdx].Value;
                if (baseModelId < 0 || baseModelId >= mc)
                    continue;

                int posIdx = posPool.GetIndex(id);
                Vector3 pos = positions[posIdx].Value;

                bool visible = true;
                for (int p = 0; p < 6; p++)
                {
                    if (frustumPlanes[p].GetDistanceToPoint(pos) < -cullingRadius)
                    {
                        visible = false;
                        break;
                    }
                }
                if (!visible) continue;

                int lodLevel = 1;
                int finalModelId = baseModelId;
                float rawAnimFrame = 0f;

                if (animPool.Has(id))
                {
                    int animIdx = animPool.GetIndex(id);
                    var anim = anims[animIdx];
                    rawAnimFrame = anim.CurrentFrame;
                }

                if (lodPool.Has(id))
                {
                    int lodIdx = lodPool.GetIndex(id);
                    var lod = lodPool.components[lodIdx];
                    if (lod.LodModelId < 0)
                        continue;
                    finalModelId = lod.LodModelId;
                    lodLevel = lod.LodLevel;
                }

                if (finalModelId >= mc)
                    finalModelId = baseModelId;

                batch.GetCount(finalModelId)++;
                tempEntityIds[validCount] = id;
                tempModelIds[validCount] = finalModelId;
                tempLodLevels[validCount] = lodLevel;
                tempAnimFrames[validCount] = rawAnimFrame;
                validCount++;
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
                    batch.SetInstanceData(m, new InstanceData[c], c);
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
            Vector4[] animArr = new Vector4[validCount];
            Color[] colorArr = new Color[validCount];

            for (int i = 0; i < validCount; i++)
            {
                int id = tempEntityIds[i];
                int finalModelId = tempModelIds[i];
                int lodLevel = tempLodLevels[i];
                float rawAnimFrame = tempAnimFrames[i];

                int posIdx = posPool.GetIndex(id);
                posArr[i] = positions[posIdx].Value;

                int rotIdx = rotPool.GetIndex(id);
                rotArr[i] = rotations[rotIdx].Value;

                if (colorPool.Has(id))
                {
                    int colorIdx = colorPool.GetIndex(id);
                    colorArr[i] = colorPool.components[colorIdx].Value;
                }
                else
                    colorArr[i] = Color.white;

                if (animPool.Has(id))
                {
                    int animIdx = animPool.GetIndex(id);
                    var anim = anims[animIdx];
                    float clipStart = 0f;
                    float clipFrameCount = anim.FrameCount;
                    var vatData = ModelRegistry.GetVatData(ModelRegistry.GetLodBaseId(finalModelId));
                    if (vatData != null && vatData.clips != null && vatData.clips.Length > 0)
                    {
                        int clipIndex = Mathf.Clamp(anim.AnimIndex, 0, vatData.clips.Length - 1);
                        clipStart = vatData.clips[clipIndex].startFrame;
                        clipFrameCount = vatData.clips[clipIndex].frameCount;
                    }

                    float quantizedFrame = rawAnimFrame;
                    if (lodLevel == 2)
                    {
                        quantizedFrame = Mathf.Floor(rawAnimFrame / 2f) * 2f;
                        int bucket = id % 2;
                        quantizedFrame += bucket * 0.5f;
                    }
                    else if (lodLevel == 3)
                    {
                        quantizedFrame = Mathf.Floor(rawAnimFrame / 4f) * 4f;
                        int bucket = id % 4;
                        quantizedFrame += bucket * 0.5f;
                    }

                    animArr[i] = new Vector4(anim.AnimIndex, quantizedFrame, clipStart, clipFrameCount);
                }
                else
                {
                    animArr[i] = Vector4.zero;
                }
            }

            DisposeOld();
            modelOfEntity = new NativeArray<int>(modelOfEntityArr, Allocator.TempJob);
            slotForEntity = new NativeArray<int>(slotForEntityArr, Allocator.TempJob);
            positionCache = new NativeArray<Vector3>(posArr, Allocator.TempJob);
            rotationCache = new NativeArray<Quaternion>(rotArr, Allocator.TempJob);
            animCache = new NativeArray<Vector4>(animArr, Allocator.TempJob);
            colorCache = new NativeArray<Color>(colorArr, Allocator.TempJob);
            flatInstanceData = new NativeArray<InstanceData>(validCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
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
                AnimData = animCache,
                Colors = colorCache,
                Prefix = prefix,
                FlatInstanceData = flatInstanceData
            };
            return job.Schedule(totalWork, batchSize, dep);
        }

        public static void Scatter()
        {
            if (validCount <= 0)
                return;

            var dataArr = flatInstanceData.ToArray();
            var prefixArr = prefix.ToArray();
            var modelOfEntityArr = modelOfEntity.ToArray();
            var slotForEntityArr = slotForEntity.ToArray();

            for (int i = 0; i < validCount; i++)
            {
                int modelId = modelOfEntityArr[i];
                int slot = slotForEntityArr[i];
                int readIdx = prefixArr[modelId] + slot;
                BatchData.Instance.GetInstanceData(modelId)[slot] = dataArr[readIdx];
            }

            DisposeOld();
        }

        static void DisposeOld()
        {
            if (modelOfEntity.IsCreated) modelOfEntity.Dispose();
            if (slotForEntity.IsCreated) slotForEntity.Dispose();
            if (positionCache.IsCreated) positionCache.Dispose();
            if (rotationCache.IsCreated) rotationCache.Dispose();
            if (animCache.IsCreated) animCache.Dispose();
            if (colorCache.IsCreated) colorCache.Dispose();
            if (flatInstanceData.IsCreated) flatInstanceData.Dispose();
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
