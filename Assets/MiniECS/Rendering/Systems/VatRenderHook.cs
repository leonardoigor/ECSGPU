using UnityEngine;

namespace MiniECS.Rendering
{
    [RequireComponent(typeof(Camera))]
    public sealed class VatRenderHook : MonoBehaviour
    {
        Camera cachedCamera;

        void Awake()
        {
            if (cachedCamera == null)
                cachedCamera = GetComponent<Camera>();
        }

        internal void SetCamera(Camera cam)
        {
            cachedCamera = cam;
        }

        void OnPostRender()
        {
            if (cachedCamera == null)
            {
                Debug.LogWarning("[VatRenderHook] OnPostRender: cachedCamera is NULL");
                return;
            }
            RenderSubmitSystem.RenderForCamera(cachedCamera);
        }
    }
}

