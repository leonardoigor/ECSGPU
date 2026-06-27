using MiniECS;
using UnityEngine;
using System.Threading.Tasks;

namespace Game.ParticleLife
{
    public struct ParticleLifeSimSystem : ISystem, IClientSystem, IParallelSystem
    {
        public const int MaxParticles = 10000;
        public const int MaxSpecies = 8;

        public static int ParticleCount;
        public static int MaxPopulation = 3000;
        public static Vector3[] Positions = new Vector3[MaxParticles];
        public static Vector3[] Velocities = new Vector3[MaxParticles];
        public static int[] Species = new int[MaxParticles];
        public static int[] SpeciesPop = new int[MaxSpecies];

        public static int SpeciesCount = 2;
        public static float[] Interactions = new float[MaxSpecies * MaxSpecies];

        public static float InteractionRadius = 4f;
        public static float MaxSpeed = 6f;
        public static float Damping = 0.97f;
        public static float NoiseStrength = 0.6f;
        public static float SpawnInterval = 0.25f;
        public static int SpawnCount = 4;
        public static float SpawnRadius = 2.5f;
        public static Vector3 SpawnCenter = new Vector3(15, 0, 0);
        public static float SpawnSpeed = 3f;
        public static bool SpawnEnabled = false;

        static Vector3[] noiseVectors = new Vector3[MaxParticles];
        static float simDt;

        float spawnTimer;

        public static void GetRule(int a, int b, out float val)
            => val = Interactions[a * MaxSpecies + b];
        public static void SetRule(int a, int b, float val)
            => Interactions[a * MaxSpecies + b] = val;

        public void OnCreate(ref SystemContext ctx) { }

        public void OnUpdate(ref SystemContext ctx)
        {
            simDt = ctx.DeltaTime;
            int count = ParticleCount;

            if (SpawnEnabled)
            {
                spawnTimer += ctx.DeltaTime;
                if (spawnTimer >= SpawnInterval && count < MaxPopulation)
                {
                    spawnTimer = 0f;
                    int toSpawn = Mathf.Min(SpawnCount, MaxPopulation - count);
                    for (int i = 0; i < toSpawn; i++)
                        SpawnParticle(count + i);
                    ParticleCount += toSpawn;
                    count = ParticleCount;
                }
            }

            if (count <= 0) return;

            for (int i = 0; i < count; i++)
                noiseVectors[i] = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1)
                ).normalized;

            Parallel.For(0, count, ComputeParticle);
        }

        static readonly System.Random rng = new System.Random();

        void SpawnParticle(int idx)
        {
            int sp = rng.Next(0, SpeciesCount);
            Vector3 dir = new Vector3(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1)
            ).normalized;
            float dist = (float)(rng.NextDouble()) * SpawnRadius;
            Positions[idx] = SpawnCenter + dir * dist;
            Velocities[idx] = dir * SpawnSpeed;
            Species[idx] = sp;
        }

        static void ComputeParticle(int i)
        {
            int count = ParticleCount;
            if (i >= count) return;

            Vector3 pos = Positions[i];
            int sp = Species[i];
            Vector3 force = Vector3.zero;

            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;
                Vector3 delta = Positions[j] - pos;
                float dist = delta.magnitude;
                if (dist < 0.01f || dist > InteractionRadius) continue;

                int sp_j = Species[j];
                float rule = Interactions[sp * MaxSpecies + sp_j];
                if (rule == 0f) continue;

                float t = 1f - dist / InteractionRadius;
                force += delta.normalized * (rule * t);
            }

            force += noiseVectors[i] * NoiseStrength;

            float distFromCenter = pos.magnitude;
            if (distFromCenter > 15f)
                force -= pos.normalized * ((distFromCenter - 15f) * 0.5f);

            Velocities[i] = (Velocities[i] + force * simDt) * Damping;

            float speed = Velocities[i].magnitude;
            if (speed > MaxSpeed)
                Velocities[i] = Velocities[i] / speed * MaxSpeed;

            Positions[i] += Velocities[i] * simDt;
        }

        public static void RecalcPopulation()
        {
            for (int s = 0; s < MaxSpecies; s++)
                SpeciesPop[s] = 0;
            for (int i = 0; i < ParticleCount; i++)
            {
                int s = Species[i];
                if (s >= 0 && s < MaxSpecies)
                    SpeciesPop[s]++;
            }
        }

        public static void Reset()
        {
            ParticleCount = 0;
            for (int s = 0; s < MaxSpecies; s++)
                SpeciesPop[s] = 0;
        }

        public void OnDestroy(ref SystemContext ctx) { }
    }
}
