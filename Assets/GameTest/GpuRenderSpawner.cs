using UnityEngine;
using MiniECS;
using MiniECS.Rendering;

public class GpuRenderSpawner : MonoBehaviour
{
    [Header("Model")]
    public Mesh mesh;
    public Material material;

    [Header("Spawn")]
    public int count = 10000;
    public float radius = 50f;
    public Vector3 center = Vector3.zero;

    void Start()
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

        int modelId = ModelRegistry.Register(mesh, material);

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
        }

        Debug.Log($"[GpuRenderSpawner] Spawned {count} entities (modelId={modelId})");
        Destroy(this);
    }
}
