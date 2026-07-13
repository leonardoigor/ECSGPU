using MiniECS;
using MiniECS.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public struct MassComponent : IComponentData
{
    public float Value;
}

public struct VelocityComponent : IComponentData
{
    public Vector3 Value;
}

public class LifeSimpleSpawn : MonoBehaviour
{
    [Header("Model")]
    public VatAnimationData VarModel;

    [Header("Spawn")]
    public int count = 16;
    public float spawnRadius = 12f;
    public float minMass = 1f;
    public float maxMass = 25f;
    public float initialVelocity = 0.5f;

    [Header("Gravity")]
    public float gravitationalConstant = 1f;
    public float softening = 0.25f;
    public bool planarMotion = true;

    public int modelId;

    void Start()
    {
        if (VarModel == null || VarModel.originalMesh == null || VarModel.animationMaterial == null)
        {
            Debug.LogError("[LifeSimpleSpawn] VarModel is missing VAT data.");
            return;
        }

        VarModel.ComputeSqrDistances();
        modelId = ModelRegistry.Register(VarModel.originalMesh, VarModel.animationMaterial, VarModel);

        FMASystem.GravitationalConstant = gravitationalConstant;
        FMASystem.Softening = softening;
        FMASystem.PlanarMotion = planarMotion;

        int animIndex = VarModel.GetValidAnimationIndex(0);
        int frameCount = VarModel.GetFrameCount(animIndex);
        float frameRate = VarModel.GetFrameRate(animIndex);

        for (int i = 0; i < Mathf.Max(1, count); i++)
        {
            Vector3 pos = UnityEngine.Random.insideUnitSphere * spawnRadius;
            if (planarMotion)
                pos.y = 0f;

            Vector3 tangent = Vector3.Cross(pos.sqrMagnitude > 0.0001f ? pos.normalized : Vector3.right, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.right;

            Vector3 velocity = tangent.normalized * initialVelocity;
            velocity += UnityEngine.Random.insideUnitSphere * (initialVelocity * 0.15f);
            if (planarMotion)
                velocity.y = 0f;

            float mass = UnityEngine.Random.Range(minMass, maxMass);

            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent { Value = pos });
            EntityManager.Add(entity, new RotationComponent { Value = UnityEngine.Random.rotation });
            EntityManager.Add(entity, new ModelIdComponent { Value = modelId });
            EntityManager.Add(entity, new LodComponent { LodModelId = modelId, LodLevel = 1 });
            EntityManager.Add(entity, new MassComponent { Value = mass });
            EntityManager.Add(entity, new VelocityComponent { Value = velocity });
            EntityManager.Add(entity, new AnimationComponent
            {
                AnimIndex = animIndex,
                CurrentFrame = 0f,
                Speed = 1f,
                FrameCount = frameCount,
                FrameRate = frameRate
            });
        }
    }

    public struct FMASystem : ISystem, IServerSystem, IParallelBatchSystem
    {
        static NativeArray<int> _entityIds;
        static NativeArray<Vector3> _positions;
        static NativeArray<Vector3> _velocities;
        static NativeArray<float> _masses;
        static NativeArray<Vector3> _nextPositions;
        static NativeArray<Vector3> _nextVelocities;

        public static float GravitationalConstant = 1f;
        public static float Softening = 0.25f;
        public static bool PlanarMotion = true;

        public void OnCreate(ref SystemContext ctx)
        {
        }

        public void OnDestroy(ref SystemContext ctx)
        {
            DisposeBuffers();
        }

        public void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize)
        {
            DisposeBuffers();

            totalWork = 0;
            batchSize = 0;

            var massPool = EntityManager.Pool<MassComponent>();
            var posPool = EntityManager.Pool<PositionComponent>();
            var velPool = EntityManager.Pool<VelocityComponent>();

            int validCount = 0;
            for (int i = 0; i < massPool.count; i++)
            {
                int id = massPool.entities[i];
                if (posPool.Has(id) && velPool.Has(id))
                    validCount++;
            }

            if (validCount <= 1)
                return;

            _entityIds = new NativeArray<int>(validCount, Allocator.TempJob);
            _positions = new NativeArray<Vector3>(validCount, Allocator.TempJob);
            _velocities = new NativeArray<Vector3>(validCount, Allocator.TempJob);
            _masses = new NativeArray<float>(validCount, Allocator.TempJob);
            _nextPositions = new NativeArray<Vector3>(validCount, Allocator.TempJob);
            _nextVelocities = new NativeArray<Vector3>(validCount, Allocator.TempJob);

            int writeIndex = 0;
            for (int i = 0; i < massPool.count; i++)
            {
                int id = massPool.entities[i];
                if (!posPool.Has(id) || !velPool.Has(id))
                    continue;

                int posIndex = posPool.GetIndex(id);
                int velIndex = velPool.GetIndex(id);
                int massIndex = massPool.GetIndex(id);

                _entityIds[writeIndex] = id;
                _positions[writeIndex] = posPool.components[posIndex].Value;
                _velocities[writeIndex] = velPool.components[velIndex].Value;
                _masses[writeIndex] = Mathf.Max(0.0001f, massPool.components[massIndex].Value);
                writeIndex++;
            }

            totalWork = validCount;
            batchSize = 32;
        }

        public void OnUpdate(ref SystemContext ctx)
        {
        }

        public JobHandle ScheduleBatches(ref SystemContext ctx, int totalWork, int batchSize, JobHandle dep)
        {
            var job = new FMABatchJob
            {
                Positions = _positions,
                Velocities = _velocities,
                Masses = _masses,
                NextPositions = _nextPositions,
                NextVelocities = _nextVelocities,
                DeltaTime = ctx.DeltaTime,
                GravitationalConstant = GravitationalConstant,
                SofteningSqr = Softening * Softening,
                PlanarMotion = PlanarMotion
            };

            var handle = job.Schedule(totalWork, batchSize, dep);
            handle.Complete();

            var posPool = EntityManager.Pool<PositionComponent>();
            var velPool = EntityManager.Pool<VelocityComponent>();

            for (int i = 0; i < _entityIds.Length; i++)
            {
                int id = _entityIds[i];
                int posIndex = posPool.GetIndex(id);
                int velIndex = velPool.GetIndex(id);

                posPool.components[posIndex] = new PositionComponent { Value = _nextPositions[i] };
                velPool.components[velIndex] = new VelocityComponent { Value = _nextVelocities[i] };
            }

            DisposeBuffers();
            return default;
        }

        static void DisposeBuffers()
        {
            if (_entityIds.IsCreated) _entityIds.Dispose();
            if (_positions.IsCreated) _positions.Dispose();
            if (_velocities.IsCreated) _velocities.Dispose();
            if (_masses.IsCreated) _masses.Dispose();
            if (_nextPositions.IsCreated) _nextPositions.Dispose();
            if (_nextVelocities.IsCreated) _nextVelocities.Dispose();
        }
    }

    [BurstCompile]
    struct FMABatchJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<Vector3> Velocities;
        [ReadOnly] public NativeArray<float> Masses;
        [NativeDisableParallelForRestriction] public NativeArray<Vector3> NextPositions;
        [NativeDisableParallelForRestriction] public NativeArray<Vector3> NextVelocities;
        public float DeltaTime;
        public float GravitationalConstant;
        public float SofteningSqr;
        public bool PlanarMotion;

        public void Execute(int index)
        {
            float3 position = Positions[index];
            float3 velocity = Velocities[index];
            float3 acceleration = float3.zero;

            for (int other = 0; other < Positions.Length; other++)
            {
                if (other == index)
                    continue;

                float3 delta = (float3)Positions[other] - position;
                if (PlanarMotion)
                    delta.y = 0f;

                float distanceSqr = math.lengthsq(delta) + SofteningSqr;
                float invDistance = math.rsqrt(distanceSqr);
                float invDistance3 = invDistance * invDistance * invDistance;

                acceleration += delta * (GravitationalConstant * Masses[other] * invDistance3);
            }

            velocity += acceleration * DeltaTime;
            position += velocity * DeltaTime;

            if (PlanarMotion)
            {
                position.y = 0f;
                velocity.y = 0f;
            }

            NextPositions[index] = position;
            NextVelocities[index] = velocity;
        }
    }
}
