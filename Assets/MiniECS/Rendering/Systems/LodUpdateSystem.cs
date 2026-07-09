using MiniECS;
using UnityEngine;
using Unity.Jobs;

namespace MiniECS.Rendering
{
    public struct LodUpdateSystem : ISystem, IClientSystem, IParallelBatchSystem
    {
        static int frameCounter;
        static Camera cachedCamera;
        static Vector3 cachedCameraPos;

        public void OnCreate(ref SystemContext ctx)
        {
            frameCounter = 0;
        }

        public void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize)
        {
            totalWork = 0;
            batchSize = 0;

            frameCounter++;
            if (frameCounter % 10 != 1)
                return;

            if (cachedCamera == null)
                cachedCamera = Camera.main;
            if (cachedCamera == null)
                return;
            cachedCameraPos = cachedCamera.transform.position;

            var posPool = EntityManager.Pool<PositionComponent>();
            var rotPool = EntityManager.Pool<RotationComponent>();
            var modPool = EntityManager.Pool<ModelIdComponent>();
            var animPool = EntityManager.Pool<AnimationComponent>();
            var lodPool = EntityManager.Pool<LodComponent>();
            var active = AoISystem.GetActiveEntities();

            int mc = ModelRegistry.Count;
            if (!active.IsCreated || mc == 0)
                return;

            for (int i = 0; i < active.Length; i++)
            {
                int id = active[i];
                if (!rotPool.Has(id) || !modPool.Has(id))
                    continue;

                int modIdx = modPool.GetIndex(id);
                int baseModelId = modPool.components[modIdx].Value;
                if (baseModelId < 0 || baseModelId >= mc)
                    continue;

                int lodLevel = 1;
                int lodModelId = baseModelId;

                if (animPool.Has(id))
                {
                    int posIdx = posPool.GetIndex(id);
                    Vector3 pos = posPool.components[posIdx].Value;
                    var vatData = ModelRegistry.GetVatData(baseModelId);
                    if (vatData != null && vatData.sqrCullDistance > 0f)
                    {
                        float distSqr = (pos - cachedCameraPos).sqrMagnitude;

                        if (distSqr > vatData.sqrCullDistance)
                        {
                            lodModelId = -1;
                        }
                        else if (distSqr > vatData.sqrLod3Distance && vatData.lodUltraMaterial != null)
                        {
                            lodLevel = 3;
                            lodModelId = baseModelId + 2;
                        }
                        else if (distSqr > vatData.sqrLod2Distance && vatData.lodLowMaterial != null)
                        {
                            lodLevel = 2;
                            lodModelId = baseModelId + 1;
                        }
                    }
                }

                if (lodModelId >= mc)
                    lodModelId = baseModelId;

                if (lodPool.Has(id))
                {
                    int lodIdx = lodPool.GetIndex(id);
                    lodPool.components[lodIdx] = new LodComponent
                    {
                        LodModelId = lodModelId,
                        LodLevel = lodLevel
                    };
                }
            }
        }

        public JobHandle ScheduleBatches(ref SystemContext ctx, int totalWork, int batchSize, JobHandle dep)
        {
            return dep;
        }

        public void OnUpdate(ref SystemContext ctx) { }

        public void OnDestroy(ref SystemContext ctx) { }
    }
}
