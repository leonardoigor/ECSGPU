using UnityEngine;
using MiniECS;
using MiniECS.Rendering;

public class VatGpuRenderSpawner : MonoBehaviour
{
    [Header("VAT Models")]
    public VatAnimationData[] vatModels;

    [Header("Grid Spawn")]
    public int count = 10000;
    public Vector3 center = Vector3.zero;
    public float spacing = 2f;
    public int firstColWidth = 50;
    public int spawnsPerFrame = 100;

    [Header("Spatial Hash")]
    public float chunkSize = 20f;
    public float aoiRadius = 80f;

    [Header("Randomizer")]
    public bool enableRandomizer = true;
    public float minInterval = 1f;
    public float maxInterval = 10f;

    [Header("Mode")]
    public bool flipFlop = true;

    [Header("Color")]
    public bool randomColors = true;
    public Color[] modelColors = new Color[] { Color.white };

    int[] modelIds;
    int spawnedCount;
    int[] entities;
    int[] animCounts;
    float[] timers;
    int frameIndex;
    bool spawningDone;

    void Start()
    {
        if (vatModels == null || vatModels.Length == 0)
        {
            Debug.LogError("[VatGpuRenderSpawner] No VatAnimationData assigned.");
            return;
        }

        modelIds = new int[vatModels.Length];
        for (int i = 0; i < vatModels.Length; i++)
        {
            var vat = vatModels[i];
            if (vat == null || vat.originalMesh == null || vat.animationMaterial == null)
            {
                Debug.LogError($"[VatGpuRenderSpawner] VatAnimationData at index {i} is incomplete.");
                return;
            }
            vat.ComputeSqrDistances();
            modelIds[i] = ModelRegistry.Register(vat.originalMesh, vat.animationMaterial, vat);
        }

        entities = new int[count];
        animCounts = new int[count];
        timers = new float[count];
        spawnedCount = 0;
        spawningDone = false;
    }

    Vector3 GridPosition(int index)
    {
        int col = index % firstColWidth;
        int row = index / firstColWidth;
        return center + new Vector3(col * spacing, 0f, row * spacing);
    }

    void SpawnEntity(int index)
    {
        int modelIndex = flipFlop ? (index % vatModels.Length) : Random.Range(0, vatModels.Length);
        int modelId = modelIds[modelIndex];
        var clips = vatModels[modelIndex].clips;
        int clipCount = clips.Length;
        int startAnim = clipCount > 0 ? Random.Range(0, clipCount) : 0;
        int startFrames = clipCount > 0 ? clips[startAnim].frameCount : 30;
        float startFrameRate = clipCount > 0 ? clips[startAnim].frameRate : 30f;
        Vector3 pos = GridPosition(index);

        var entity = EntityManager.CreateEntity();
        EntityManager.Add(entity, new PositionComponent
        {
            Value = pos
        });
        EntityManager.Add(entity, new RotationComponent
        {
            Value = Random.rotationUniform
        });
        EntityManager.Add(entity, new ModelIdComponent { Value = modelId });
        EntityManager.Add(entity, new LodComponent { LodModelId = modelId, LodLevel = 1 });
        EntityManager.Add(entity, new AnimationComponent
        {
            AnimIndex = startAnim,
            CurrentFrame = Random.Range(0f, startFrames),
            Speed = Random.Range(0.8f, 1.2f),
            FrameCount = startFrames,
            FrameRate = startFrameRate
        });

        Color agentColor = Color.white;
        if (randomColors)
            agentColor = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.5f, 1f);
        else if (modelColors != null && modelIndex < modelColors.Length)
            agentColor = modelColors[modelIndex];

        EntityManager.Add(entity, new ColorComponent { Value = agentColor });

        entities[index] = entity.Id;
        animCounts[index] = clipCount;
        timers[index] = Random.Range(minInterval, maxInterval);
    }

    void Update()
    {
        if (spawnedCount < count)
        {
            int end = Mathf.Min(spawnedCount + spawnsPerFrame, count);
            for (int i = spawnedCount; i < end; i++)
                SpawnEntity(i);
            spawnedCount = end;

            if (spawnedCount >= count)
            {
                spawningDone = true;
                Debug.Log($"[VatGpuRenderSpawner] Spawned {count} VAT entities across {vatModels.Length} model(s). flipFlop={flipFlop} randomizer={enableRandomizer}");

                if (!enableRandomizer)
                    Destroy(this);
            }
            return;
        }

        if (!enableRandomizer || !spawningDone)
            return;

        float avgInterval = (minInterval + maxInterval) * 0.5f;
        int perFrame = Mathf.Max(1, (int)(count / avgInterval));
        int start = (frameIndex * perFrame) % count;

        for (int i = 0; i < perFrame; i++)
        {
            int idx = (start + i) % count;
            timers[idx] -= Time.deltaTime;
            if (timers[idx] <= 0f)
            {
                int c = animCounts[idx];
                if (c > 1)
                {
                    var modelPool = EntityManager.Pool<ModelIdComponent>();
                    var pool = EntityManager.Pool<AnimationComponent>();
                    int entId = entities[idx];
                    if (!pool.Has(entId) || !modelPool.Has(entId))
                        continue;

                    int animIdx = pool.GetIndex(entId);
                    int modelIdx = modelPool.GetIndex(entId);
                    int modelId = modelPool.components[modelIdx].Value;
                    var vatData = ModelRegistry.GetVatData(modelId);
                    ref var anim = ref pool.components[animIdx];
                    int oldIdx = anim.AnimIndex;
                    int newIdx = (oldIdx + 1 + Random.Range(0, c - 1)) % c;
                    if (vatData == null || vatData.clips == null || vatData.clips.Length <= newIdx)
                        continue;

                    anim.AnimIndex = newIdx;
                    anim.CurrentFrame = 0f;
                    anim.FrameCount = vatData.clips[newIdx].frameCount;
                    anim.FrameRate = vatData.clips[newIdx].frameRate;
                }
                timers[idx] = Random.Range(minInterval, maxInterval);
            }
        }

        frameIndex++;
    }
}
