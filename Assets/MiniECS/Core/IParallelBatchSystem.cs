
using Unity.Jobs;

public interface IParallelBatchSystem : ISystem
{
    void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize);
    JobHandle ScheduleBatches(ref SystemContext ctx, int totalWork, int batchSize, JobHandle dep);
}
