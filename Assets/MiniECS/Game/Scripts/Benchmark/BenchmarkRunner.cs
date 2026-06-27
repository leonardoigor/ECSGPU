using System;
using System.Collections.Generic;
using System.Diagnostics;
using MiniECS;
using UnityEngine;

namespace Game.Benchmark.Components
{
    public struct BenchmarkEntityData : IComponentData { public float Value; }
    public struct BenchmarkPosition : IComponentData { public Vector3 Value; }
    public struct BenchmarkTag : IComponentData { }
    public struct BenchmarkDynamicInt : IComponentData { public int Value; }
}

namespace Game.Benchmark
{
    using Components;

    public static class BenchmarkRunner
    {
        static readonly List<int> entityIds = new();
        static int entityCount;

        public static int EntityCount => entityCount;

        public static BenchmarkResult SpawnEntities(int count)
        {
            CleanupEntities();
            var sw = Stopwatch.StartNew();
            long memBefore = GC.GetTotalMemory(false);

            for (int i = 0; i < count; i++)
            {
                var e = EntityManager.CreateEntity();
                EntityManager.Add(e, new BenchmarkEntityData { Value = (float)i / count });
                EntityManager.Add(e, new BenchmarkPosition { Value = new Vector3(i, i * 2, i * 3) });
                EntityManager.Add(e, new BenchmarkTag());
                entityIds.Add(e.Id);
            }

            sw.Stop();
            entityCount = count;
            long memAfter = GC.GetTotalMemory(false);

            return new BenchmarkResult
            {
                Name = "Spawn",
                EntityCount = count,
                Iterations = 1,
                TotalTimeMs = sw.Elapsed.TotalMilliseconds,
                AvgTimeMs = sw.Elapsed.TotalMilliseconds,
                MinTimeMs = sw.Elapsed.TotalMilliseconds,
                MaxTimeMs = sw.Elapsed.TotalMilliseconds,
                EntitiesPerMs = count / sw.Elapsed.TotalMilliseconds,
                MemoryBytes = memAfter - memBefore
            };
        }

        public static BenchmarkResult RunQuery1(int iterations)
        {
            int count = entityCount;
            var times = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                float acc = 0;
                var sw = Stopwatch.StartNew();
                foreach (var (e, data) in EntityManager.Query<BenchmarkEntityData>().ForEach())
                {
                    acc += data.Value;
                }
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
                if (acc < 0) UnityEngine.Debug.Log("unreachable");
            }

            return ComputeResult("Query 1-Comp", count, iterations, times);
        }

        public static BenchmarkResult RunQuery2(int iterations)
        {
            int count = entityCount;
            var times = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                float acc = 0;
                var sw = Stopwatch.StartNew();
                foreach (var (e, data, pos) in EntityManager.Query<BenchmarkEntityData, BenchmarkPosition>().ForEach())
                {
                    acc += data.Value + pos.Value.x;
                }
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
                if (acc < 0) UnityEngine.Debug.Log("unreachable");
            }

            return ComputeResult("Query 2-Comp", count, iterations, times);
        }

        public static BenchmarkResult RunQuery3(int iterations)
        {
            int count = entityCount;
            var times = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                float acc = 0;
                var sw = Stopwatch.StartNew();
                foreach (var (e, data, pos, tag) in EntityManager.Query<BenchmarkEntityData, BenchmarkPosition, BenchmarkTag>().ForEach())
                {
                    acc += data.Value + pos.Value.x;
                }
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
                if (acc < 0) UnityEngine.Debug.Log("unreachable");
            }

            return ComputeResult("Query 3-Comp", count, iterations, times);
        }

        public static BenchmarkResult RunValueRW(int iterations)
        {
            int count = entityCount;
            var times = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                foreach (var (e, data) in EntityManager.Query<ValueRW<BenchmarkEntityData>>().ForEach())
                {
                    data.Value.Value *= 1.0001f;
                }
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            return ComputeResult("Query ValueRW", count, iterations, times);
        }

        public static BenchmarkResult RunPoolDirectAccess(int iterations)
        {
            int count = entityCount;
            var times = new double[iterations];
            var pool = EntityManager.Pool<BenchmarkEntityData>();

            for (int i = 0; i < iterations; i++)
            {
                float acc = 0;
                var sw = Stopwatch.StartNew();
                for (int j = 0; j < entityIds.Count; j++)
                {
                    int id = entityIds[j];
                    if (pool.Has(id))
                        acc += pool.Get(id).Value;
                }
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
                if (acc < 0) UnityEngine.Debug.Log("unreachable");
            }

            return ComputeResult("Pool Direct Access", count, iterations, times);
        }

        public static BenchmarkResult RunAddRemove(int iterations)
        {
            int count = entityCount;
            var times = new double[iterations];
            var pool = EntityManager.Pool<BenchmarkDynamicInt>();

            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();

                for (int j = 0; j < entityIds.Count; j++)
                {
                    int id = entityIds[j];
                    if (!pool.Has(id))
                        pool.Add(id, new BenchmarkDynamicInt { Value = j });
                }

                for (int j = 0; j < entityIds.Count; j++)
                {
                    int id = entityIds[j];
                    if (pool.Has(id))
                        pool.Remove(id);
                }

                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            return ComputeResult("Add/Remove", count, iterations, times);
        }

        public static BenchmarkResult RunFullPipeline(int count, int iterations)
        {
            var sw = Stopwatch.StartNew();

            SpawnEntities(count);
            RunQuery1(iterations);
            RunQuery2(iterations);
            RunQuery3(iterations);
            RunValueRW(iterations);
            RunPoolDirectAccess(iterations);
            RunAddRemove(iterations);
            CleanupEntities();

            sw.Stop();

            return new BenchmarkResult
            {
                Name = "Full Pipeline",
                EntityCount = count,
                Iterations = iterations,
                TotalTimeMs = sw.Elapsed.TotalMilliseconds,
                AvgTimeMs = sw.Elapsed.TotalMilliseconds,
                MinTimeMs = sw.Elapsed.TotalMilliseconds,
                MaxTimeMs = sw.Elapsed.TotalMilliseconds,
                EntitiesPerMs = count / sw.Elapsed.TotalMilliseconds,
                MemoryBytes = 0
            };
        }

        public static void CleanupEntities()
        {
            var p1 = EntityManager.Pool<BenchmarkEntityData>();
            var p2 = EntityManager.Pool<BenchmarkPosition>();
            var p3 = EntityManager.Pool<BenchmarkTag>();
            var p4 = EntityManager.Pool<BenchmarkDynamicInt>();

            foreach (int id in entityIds)
            {
                if (p1.Has(id)) p1.Remove(id);
                if (p2.Has(id)) p2.Remove(id);
                if (p3.Has(id)) p3.Remove(id);
                if (p4.Has(id)) p4.Remove(id);
            }
            entityIds.Clear();
            entityCount = 0;
        }

        static BenchmarkResult ComputeResult(string name, int count, int iterations, double[] times)
        {
            double total = 0, min = double.MaxValue, max = double.MinValue;
            for (int i = 0; i < times.Length; i++)
            {
                total += times[i];
                if (times[i] < min) min = times[i];
                if (times[i] > max) max = times[i];
            }
            double avg = total / iterations;
            long mem = GC.GetTotalMemory(false);

            return new BenchmarkResult
            {
                Name = name,
                EntityCount = count,
                Iterations = iterations,
                TotalTimeMs = total,
                AvgTimeMs = avg,
                MinTimeMs = min,
                MaxTimeMs = max,
                EntitiesPerMs = count / avg,
                MemoryBytes = mem
            };
        }
    }
}
