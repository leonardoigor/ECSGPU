using System.Text;

namespace Game.Benchmark
{
    public struct BenchmarkResult
    {
        public string Name;
        public int EntityCount;
        public int Iterations;
        public double TotalTimeMs;
        public double AvgTimeMs;
        public double MinTimeMs;
        public double MaxTimeMs;
        public double EntitiesPerMs;
        public long MemoryBytes;

        public double MemoryMB => MemoryBytes / (1024.0 * 1024.0);
        public double EntitiesPerSecond => EntitiesPerMs * 1000.0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {Name} ===");
            sb.AppendLine($"  Entities: {EntityCount:N0}");
            sb.AppendLine($"  Iterations: {Iterations}");
            sb.AppendLine($"  Total: {TotalTimeMs:F2} ms");
            sb.AppendLine($"  Avg: {AvgTimeMs:F4} ms");
            sb.AppendLine($"  Min: {MinTimeMs:F4} ms");
            sb.AppendLine($"  Max: {MaxTimeMs:F4} ms");
            sb.AppendLine($"  Entities/ms: {EntitiesPerMs:F1}");
            sb.AppendLine($"  Entities/s: {EntitiesPerSecond:F0}");
            if (MemoryBytes > 0)
                sb.AppendLine($"  Memory: {MemoryMB:F2} MB");
            return sb.ToString();
        }

        public string ToCsvLine()
        {
            return $"{Name},{EntityCount},{Iterations},{TotalTimeMs:F4},{AvgTimeMs:F6},{MinTimeMs:F6},{MaxTimeMs:F6},{EntitiesPerMs:F2},{MemoryMB:F4}";
        }

        public static string CsvHeader => "Name,Entities,Iterations,TotalMs,AvgMs,MinMs,MaxMs,EntitiesPerMs,MemoryMB";
    }
}
