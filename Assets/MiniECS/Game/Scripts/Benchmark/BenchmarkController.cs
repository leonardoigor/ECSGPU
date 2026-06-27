using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Benchmark
{
    public class BenchmarkController : MonoBehaviour
    {
        [SerializeField] int[] entityCountOptions = { 1000, 5000, 10000, 50000, 100000, 500000 };
        [SerializeField] int[] iterationOptions = { 1, 10, 30, 100 };

        int selectedCountIndex = 2;
        int selectedIterationIndex = 2;
        bool isRunning;
        Vector2 scrollPos;
        readonly List<BenchmarkResult> results = new();
        string statusText = "Ready";

        int SelectedCount => entityCountOptions[selectedCountIndex];
        int SelectedIterations => iterationOptions[selectedIterationIndex];

        void Awake()
        {
            Application.targetFrameRate = -1;
        }

        void OnGUI()
        {
            float x = 10, y = 10, w = Screen.width - 20, h = Screen.height - 20;
            GUI.Box(new Rect(x, y, w, h), "MiniECS Benchmark");

            float lx = 20, ly = 40, lw = 150, cw = 100, gap = 5;

            GUI.Label(new Rect(lx, ly, lw, 20), "Entity Count:");
            string[] countLabels = Array.ConvertAll(entityCountOptions, v => v.ToString("N0"));
            int newCount = GUI.SelectionGrid(new Rect(lx + lw, ly, cw * 3, 20), selectedCountIndex, countLabels, 6);
            if (newCount != selectedCountIndex) { selectedCountIndex = newCount; }
            ly += 30;

            GUI.Label(new Rect(lx, ly, lw, 20), "Iterations:");
            string[] iterLabels = Array.ConvertAll(iterationOptions, v => v.ToString());
            int newIter = GUI.SelectionGrid(new Rect(lx + lw, ly, cw * 2, 20), selectedIterationIndex, iterLabels, 4);
            if (newIter != selectedIterationIndex) { selectedIterationIndex = newIter; }
            ly += 35;

            float bx = lx, by = ly, bw = 100, bh = 25;

            if (GUI.Button(new Rect(bx, by, bw, bh), "Spawn") && !isRunning)
                RunSpawn();
            if (GUI.Button(new Rect(bx + bw + gap, by, bw, bh), "Query 1") && !isRunning)
                RunQuery1();
            if (GUI.Button(new Rect(bx + (bw + gap) * 2, by, bw, bh), "Query 2") && !isRunning)
                RunQuery2();
            if (GUI.Button(new Rect(bx + (bw + gap) * 3, by, bw, bh), "Query 3") && !isRunning)
                RunQuery3();
            by += bh + gap;

            if (GUI.Button(new Rect(bx, by, bw, bh), "ValueRW") && !isRunning)
                RunValueRW();
            if (GUI.Button(new Rect(bx + bw + gap, by, bw, bh), "Pool Direct") && !isRunning)
                RunPoolDirect();
            if (GUI.Button(new Rect(bx + (bw + gap) * 2, by, bw, bh), "Add/Remove") && !isRunning)
                RunAddRemove();
            if (GUI.Button(new Rect(bx + (bw + gap) * 3, by, bw, bh), "Cleanup") && !isRunning)
                { BenchmarkRunner.CleanupEntities(); statusText = "Cleaned up"; }
            by += bh + gap;

            if (GUI.Button(new Rect(bx, by, bw, bh), "Full Pipeline") && !isRunning)
                RunPipeline();
            if (GUI.Button(new Rect(bx + bw + gap, by, bw, bh), "Run All") && !isRunning)
                RunAll();
            if (GUI.Button(new Rect(bx + (bw + gap) * 2, by, bw, bh), "Export CSV") && results.Count > 0 && !isRunning)
                ExportCsv();
            if (GUI.Button(new Rect(bx + (bw + gap) * 3, by, bw, bh), "Clear"))
                { results.Clear(); statusText = "Results cleared"; }
            by += bh + 10;

            GUI.Label(new Rect(lx, by, 400, 20), $"Status: {statusText}");
            by += 25;

            float ry = by + 15, rh = Screen.height - ry - 30;
            GUI.Box(new Rect(lx, by, w - 20, rh), "Results");

            float innerW = w - 40;
            Rect viewRect = new Rect(0, 0, innerW - 20, results.Count * 160 + 20);
            scrollPos = GUI.BeginScrollView(new Rect(lx + 5, ry, w - 30, rh - 10), scrollPos, viewRect);

            float itemY = 5;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                GUI.Box(new Rect(5, itemY, viewRect.width - 15, 150), r.ToString());
                itemY += 155;
            }

            GUI.EndScrollView();
        }

        void RunSpawn()
        {
            isRunning = true;
            statusText = $"Spawning {SelectedCount:N0} entities...";
            var r = BenchmarkRunner.SpawnEntities(SelectedCount);
            results.Add(r);
            UnityEngine.Debug.Log(r.ToString());
            statusText = $"Spawned {SelectedCount:N0} in {r.TotalTimeMs:F2} ms";
            isRunning = false;
        }

        void RunQuery1()
        {
            if (BenchmarkRunner.EntityCount == 0) { statusText = "Spawn entities first!"; return; }
            isRunning = true;
            statusText = "Running Query 1-Comp...";
            var r = BenchmarkRunner.RunQuery1(SelectedIterations);
            results.Add(r);
            statusText = $"Query 1-Comp: {r.AvgTimeMs:F4} ms avg ({r.EntitiesPerSecond:F0}/s)";
            isRunning = false;
        }

        void RunQuery2()
        {
            if (BenchmarkRunner.EntityCount == 0) { statusText = "Spawn entities first!"; return; }
            isRunning = true;
            statusText = "Running Query 2-Comp...";
            var r = BenchmarkRunner.RunQuery2(SelectedIterations);
            results.Add(r);
            statusText = $"Query 2-Comp: {r.AvgTimeMs:F4} ms avg";
            isRunning = false;
        }

        void RunQuery3()
        {
            if (BenchmarkRunner.EntityCount == 0) { statusText = "Spawn entities first!"; return; }
            isRunning = true;
            statusText = "Running Query 3-Comp...";
            var r = BenchmarkRunner.RunQuery3(SelectedIterations);
            results.Add(r);
            statusText = $"Query 3-Comp: {r.AvgTimeMs:F4} ms avg";
            isRunning = false;
        }

        void RunValueRW()
        {
            if (BenchmarkRunner.EntityCount == 0) { statusText = "Spawn entities first!"; return; }
            isRunning = true;
            statusText = "Running ValueRW...";
            var r = BenchmarkRunner.RunValueRW(SelectedIterations);
            results.Add(r);
            statusText = $"ValueRW: {r.AvgTimeMs:F4} ms avg";
            isRunning = false;
        }

        void RunPoolDirect()
        {
            if (BenchmarkRunner.EntityCount == 0) { statusText = "Spawn entities first!"; return; }
            isRunning = true;
            statusText = "Running Pool Direct...";
            var r = BenchmarkRunner.RunPoolDirectAccess(SelectedIterations);
            results.Add(r);
            statusText = $"Pool Direct: {r.AvgTimeMs:F4} ms avg";
            isRunning = false;
        }

        void RunAddRemove()
        {
            if (BenchmarkRunner.EntityCount == 0) { statusText = "Spawn entities first!"; return; }
            isRunning = true;
            statusText = "Running Add/Remove...";
            var r = BenchmarkRunner.RunAddRemove(SelectedIterations);
            results.Add(r);
            statusText = $"Add/Remove: {r.AvgTimeMs:F4} ms avg";
            isRunning = false;
        }

        void RunPipeline()
        {
            isRunning = true;
            statusText = "Running Full Pipeline...";
            var r = BenchmarkRunner.RunFullPipeline(SelectedCount, SelectedIterations);
            results.Add(r);
            statusText = $"Full Pipeline: {r.TotalTimeMs:F2} ms total";
            isRunning = false;
        }

        void RunAll()
        {
            if (BenchmarkRunner.EntityCount == 0)
            {
                results.Add(BenchmarkRunner.SpawnEntities(SelectedCount));
            }
            isRunning = true;
            statusText = "Running all benchmarks...";
            results.Add(BenchmarkRunner.RunQuery1(SelectedIterations));
            results.Add(BenchmarkRunner.RunQuery2(SelectedIterations));
            results.Add(BenchmarkRunner.RunQuery3(SelectedIterations));
            results.Add(BenchmarkRunner.RunValueRW(SelectedIterations));
            results.Add(BenchmarkRunner.RunPoolDirectAccess(SelectedIterations));
            results.Add(BenchmarkRunner.RunAddRemove(SelectedIterations));
            statusText = "All benchmarks completed";
            isRunning = false;
        }

        void ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(BenchmarkResult.CsvHeader);
            for (int i = 0; i < results.Count; i++)
                sb.AppendLine(results[i].ToCsvLine());

            string path = Path.Combine(Application.dataPath, $"../BenchmarkResults_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, sb.ToString());
            UnityEngine.Debug.Log($"Results exported to: {path}");
            statusText = $"Exported to {path}";
        }
    }
}
