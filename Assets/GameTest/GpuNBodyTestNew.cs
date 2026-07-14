using UnityEngine;
using Unity.Mathematics;
using ECSGPU.GpuEcs;
using ECSGPU.GpuEcs.Examples;

public class GpuNBodyTestNew : MonoBehaviour
{
    public ComputeShader gravityShader;
    public ComputeShader integrateShader;
    public int entityCount = 10000;
    public float spawnRadius = 50f;
    public float initialVelocity = 0.5f;
    public float minMass = 1f;
    public float maxMass = 25f;
    public float gravitationalConstant = 1f;
    public float softening = 0.25f;
    public bool planarMotion = true;
    public int threadX = 256;

    GpuWorld _world;
    GpuNBodyGravityKernel _gravityKernel;
    GpuNBodyIntegrateKernel _integrateKernel;
    GpuWorldDebug _worldDebug;

    void Start()
    {
        ResolveShaders();
        if (gravityShader == null || integrateShader == null)
        {
            Debug.LogError("[GpuNBody] Missing generated compute shader.", this);
            enabled = false;
            return;
        }

        _world = new GpuWorld();
        _gravityKernel = new GpuNBodyGravityKernel();
        _integrateKernel = new GpuNBodyIntegrateKernel();
        _worldDebug = GetComponent<GpuWorldDebug>();
        if (_worldDebug == null)
            _worldDebug = gameObject.AddComponent<GpuWorldDebug>();
        _worldDebug.World = _world;

        SpawnEntities();
        UploadToGPU();
    }

    void SpawnEntities()
    {
        var positions = new GpuPositionComponent[entityCount];
        var velocities = new GpuVelocityComponent[entityCount];
        var masses = new GpuMassComponent[entityCount];
        var forces = new GpuForceComponent[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            float3 pos = (float3)(UnityEngine.Random.insideUnitSphere * spawnRadius);
            if (planarMotion) pos.y = 0;

            float3 tangent = math.normalizesafe(
                math.cross(
                    math.lengthsq(pos) > 0.0001f ? math.normalize(pos) : new float3(1, 0, 0),
                    new float3(0, 1, 0)
                )
            );
            if (math.lengthsq(tangent) < 0.0001f)
                tangent = new float3(1, 0, 0);

            float3 vel = tangent * initialVelocity;
            vel += (float3)(UnityEngine.Random.insideUnitSphere * (initialVelocity * 0.15f));
            if (planarMotion) vel.y = 0;

            float mass = UnityEngine.Random.Range(minMass, maxMass);

            positions[i] = new GpuPositionComponent { Value = pos };
            velocities[i] = new GpuVelocityComponent { Value = vel };
            masses[i] = new GpuMassComponent { Value = mass };
            forces[i] = new GpuForceComponent { Value = float3.zero };
        }

        _world.SetComponentData(positions);
        _world.SetComponentData(velocities);
        _world.SetComponentData(masses);
        _world.SetComponentData(forces);

        _gravityKernel._positions = _world.GetBuffer<GpuPositionComponent>();
        _gravityKernel._velocities = _world.GetBuffer<GpuVelocityComponent>();
        _gravityKernel._masses = _world.GetBuffer<GpuMassComponent>();
        _gravityKernel._forces = _world.GetBuffer<GpuForceComponent>();

        _integrateKernel._positions = _world.GetBuffer<GpuPositionComponent>();
        _integrateKernel._velocities = _world.GetBuffer<GpuVelocityComponent>();
        _integrateKernel._masses = _world.GetBuffer<GpuMassComponent>();
        _integrateKernel._forces = _world.GetBuffer<GpuForceComponent>();
    }

    void UploadToGPU()
    {
        long totalBytes = (long)entityCount * (12 + 12 + 4 + 12);
        Debug.Log($"[GpuNBody] Uploaded {entityCount} entities to GPU.");
        Debug.Log($"[GpuNBody] GPU memory: ~{FormatBytes(totalBytes)}");

        _gravityKernel._deltaTime = Time.deltaTime;
        _gravityKernel._gravitationalConstant = gravitationalConstant;
        _gravityKernel._softening = softening;
        _gravityKernel._entityCount = (uint)entityCount;
        _gravityKernel._planarMotion = planarMotion;

        _gravityKernel.AutoBind(_world);

        _integrateKernel._deltaTime = Time.deltaTime;
        _integrateKernel._entityCount = (uint)entityCount;
        _integrateKernel._planarMotion = planarMotion;
        _integrateKernel.AutoBind(_world);
    }

    void Update()
    {
        if (_world == null) return;

        UploadToGPU();
        _gravityKernel.Execute(gravityShader, _world, entityCount);
        _integrateKernel.Execute(integrateShader, _world, entityCount);

        Debug.Log($"[GPU] Dispatched compute shaders successfully.");
    }

    void ResolveShaders()
    {
        gravityShader = GpuGeneratedComputeShaderLoader.Load<GpuNBodyGravityKernel>() ?? gravityShader;
        integrateShader = GpuGeneratedComputeShaderLoader.Load<GpuNBodyIntegrateKernel>() ?? integrateShader ?? gravityShader;
    }

    void OnDestroy()
    {
        if (_worldDebug != null)
            _worldDebug.World = null;
        _world?.Dispose();
    }

    static string FormatBytes(long bytes)
    {
        if (bytes < 1024L) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
