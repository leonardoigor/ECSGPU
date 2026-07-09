using UnityEngine;

namespace MiniECS.Rendering.Authoring
{
    public class VatAuthoring : MonoBehaviour
    {
        public VatAnimationData vatData;

        void Awake()
        {
            if (vatData == null)
            {
                Debug.LogError("[VatAuthoring] VatAnimationData is required.", this);
                return;
            }

            var mesh = vatData.originalMesh;
            var material = vatData.animationMaterial;

            if (mesh == null || material == null)
            {
                Debug.LogError("[VatAuthoring] VatAnimationData has no mesh or material assigned.", this);
                return;
            }

            vatData.ComputeSqrDistances();

            int baseModelId = ModelRegistry.Register(mesh, material, vatData);
            int initialFrameCount = vatData.clips != null && vatData.clips.Length > 0 ? vatData.clips[0].frameCount : 1;
            float initialFrameRate = vatData.clips != null && vatData.clips.Length > 0 ? vatData.clips[0].frameRate : 30f;
            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent { Value = transform.position });
            EntityManager.Add(entity, new RotationComponent { Value = transform.rotation });
            EntityManager.Add(entity, new ModelIdComponent { Value = baseModelId });
            EntityManager.Add(entity, new LodComponent { LodModelId = baseModelId, LodLevel = 1 });
            EntityManager.Add(entity, new AnimationComponent
            {
                AnimIndex = 0,
                CurrentFrame = 0f,
                Speed = 1f,
                FrameCount = initialFrameCount,
                FrameRate = initialFrameRate
            });

            var mr = GetComponent<MeshRenderer>();
            if (mr != null) Destroy(mr);
            var mf = GetComponent<MeshFilter>();
            if (mf != null) Destroy(mf);
        }
    }
}

