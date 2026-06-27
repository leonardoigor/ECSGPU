using UnityEngine;
using MiniECS;

namespace MiniECS.Rendering.Authoring
{
    public class RenderAuthoring : MonoBehaviour
    {
        public Mesh mesh;
        public Material material;

        void Awake()
        {
            if (mesh == null)
            {
                var mf = GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }

            if (material == null)
            {
                var mr = GetComponent<MeshRenderer>();
                if (mr != null) material = mr.sharedMaterial;
            }

            if (mesh == null || material == null)
            {
                Debug.LogError("[RenderAuthoring] Mesh and Material are required.", this);
                return;
            }

            int modelId = ModelRegistry.Register(mesh, material);
            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent { Value = transform.position });
            EntityManager.Add(entity, new RotationComponent { Value = transform.rotation });
            EntityManager.Add(entity, new ModelIdComponent { Value = modelId });

            var mr2 = GetComponent<MeshRenderer>();
            if (mr2 != null) Destroy(mr2);
            var mf2 = GetComponent<MeshFilter>();
            if (mf2 != null) Destroy(mf2);
        }
    }
}
