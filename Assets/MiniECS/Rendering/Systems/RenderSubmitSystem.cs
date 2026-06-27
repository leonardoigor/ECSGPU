using MiniECS;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct RenderSubmitSystem : ISystem, IClientSystem, IMainThreadSystem
    {
        ComputeBuffer argsBuffer;
        MaterialPropertyBlock mpb;
        Bounds worldBounds;

        public void OnCreate(ref SystemContext ctx)
        {
            argsBuffer = new ComputeBuffer(5, sizeof(uint),
                ComputeBufferType.IndirectArguments);
            mpb = new MaterialPropertyBlock();
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
        }

        public void OnUpdate(ref SystemContext ctx)
        {
            RenderCollectSystem.Scatter();
            var batch = BatchData.Instance;
            int modelCount = ModelRegistry.Count;

            for (int id = 0; id < modelCount; id++)
            {
                int instanceCount = batch.GetCount(id);
                if (instanceCount == 0)
                    continue;

                var mesh = ModelRegistry.GetMesh(id);
                var material = ModelRegistry.GetMaterial(id);

                batch.UploadAndDraw(id, mesh, material, mpb, argsBuffer, worldBounds);
            }
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            argsBuffer?.Release();
            BatchData.Instance.Dispose();
        }
    }
}
