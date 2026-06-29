using MiniECS;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct RenderSubmitSystem : ISystem, IClientSystem, IMainThreadSystem
    {
        static ComputeBuffer argsBuffer;
        static MaterialPropertyBlock mpb;
        static Bounds worldBounds;
        static bool initialized;
        static Camera mainCamera;
        static VatRenderHook hook;

        public void OnCreate(ref SystemContext ctx)
        {
            if (initialized) return;
            argsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
            mpb = new MaterialPropertyBlock();
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
            mainCamera = Camera.main;

            if (mainCamera == null)
                Debug.LogError("[RenderSubmitSystem] OnCreate: Camera.main is NULL!");
            else
                Debug.Log($"[RenderSubmitSystem] OnCreate: mainCamera={mainCamera.name} pos={mainCamera.transform.position}");

            var vatShader = Shader.Find("Custom/VatInstancing");
            if (vatShader == null)
                Debug.LogError("[RenderSubmitSystem] OnCreate: Shader 'Custom/VatInstancing' NOT FOUND!");
            else if (!vatShader.isSupported)
                Debug.LogError("[RenderSubmitSystem] OnCreate: Shader 'Custom/VatInstancing' is NOT SUPPORTED!");
            else
                Debug.Log("[RenderSubmitSystem] OnCreate: Shader 'Custom/VatInstancing' OK");

            EnsureHook();
            initialized = true;
        }

        static void EnsureHook()
        {
            if (mainCamera == null)
                return;

            hook = mainCamera.GetComponent<VatRenderHook>();
            if (hook == null)
            {
                hook = mainCamera.gameObject.AddComponent<VatRenderHook>();
            }

            DebugFlyCamera debugCam = mainCamera.GetComponent<DebugFlyCamera>();
            if (debugCam == null)
            {
                debugCam = mainCamera.gameObject.AddComponent<DebugFlyCamera>();
            }

            hook.SetCamera(mainCamera);
        }

        internal static void RenderForCamera(Camera cam)
        {
            if (!initialized || cam == null || cam != mainCamera)
                return;
            var batch = BatchData.Instance;
            int modelCount = ModelRegistry.Count;

            for (int id = 0; id < modelCount; id++)
            {
                int instanceCount = batch.GetCount(id);
                if (instanceCount == 0)
                    continue;

                var mesh = ModelRegistry.GetMesh(id);
                var material = ModelRegistry.GetMaterial(id);

                mpb.Clear();
                batch.UploadAndDraw(id, mesh, material, mpb, argsBuffer, worldBounds);
            }
        }

        public void OnUpdate(ref SystemContext ctx)
        {
            RenderCollectSystem.Scatter();
            EnsureHook();
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            if (initialized)
            {
                argsBuffer?.Release();
                BatchData.Instance.Dispose();
                initialized = false;
            }
        }
    }
}
