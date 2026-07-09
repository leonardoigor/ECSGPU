using UnityEngine;
using MiniECS;
using MiniECS.Rendering;

public class GpuRenderSpawner : MonoBehaviour
{
    [Header("Model")]
    public Mesh mesh;
    public Material material;
    public bool useVat;
    public VatAnimationData vatModel;

    [Header("Spawn")]
    public int count = 10000;
    public float radius = 50f;
    public Vector3 center = Vector3.zero;

    void Start()
    {
        int modelId;

        if (useVat)
        {
            if (vatModel == null || vatModel.originalMesh == null || vatModel.animationMaterial == null)
            {
                Debug.LogError("[GpuRenderSpawner] VAT model is missing (VatAnimationData/originalMesh/animationMaterial).");
                return;
            }

            vatModel.ComputeSqrDistances();
            modelId = ModelRegistry.Register(vatModel.originalMesh, vatModel.animationMaterial, vatModel);
        }
        else
        {
            if (mesh == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh = cube.GetComponent<MeshFilter>().sharedMesh;
                Destroy(cube);
            }

            if (material == null)
            {
                var shader = Shader.Find("Custom/IndirectInstancing");
                if (shader == null)
                {
                    Debug.LogError("[GpuRenderSpawner] Custom/IndirectInstancing shader not found.");
                    return;
                }
                material = new Material(shader);
            }

            modelId = ModelRegistry.Register(mesh, material);
        }

        for (int i = 0; i < count; i++)
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent
            {
                Value = center + Random.insideUnitSphere * radius
            });
            EntityManager.Add(entity, new RotationComponent
            {
                Value = Random.rotation
            });
            EntityManager.Add(entity, new ModelIdComponent { Value = modelId });
            EntityManager.Add(entity, new LodComponent { LodModelId = modelId, LodLevel = 1 });

            if (useVat)
            {
                int animIndex = vatModel.GetValidAnimationIndex(0);
                int frameCount = vatModel.GetFrameCount(animIndex);
                float fr = vatModel.GetFrameRate(animIndex);

                EntityManager.Add(entity, new AnimationComponent
                {
                    AnimIndex = animIndex,
                    CurrentFrame = 0f,
                    Speed = 1f,
                    FrameCount = frameCount,
                    FrameRate = fr
                });
            }
        }

        Debug.Log($"[GpuRenderSpawner] Spawned {count} entities (modelId={modelId})");
        Destroy(this);
    }
}
