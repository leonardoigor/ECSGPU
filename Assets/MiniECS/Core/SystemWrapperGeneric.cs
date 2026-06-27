
using Unity.Jobs;
using Unity.Burst;

public sealed class SystemWrapper<T> : SystemWrapper
    where T : struct, ISystem {

    T system;

    public override bool IsServer => typeof(IServerSystem).IsAssignableFrom(typeof(T));
    public override bool IsClient => typeof(IClientSystem).IsAssignableFrom(typeof(T));
    public override string DisplayName => typeof(T).Name;

    public override void Create(ref SystemContext ctx) {
        system.OnCreate(ref ctx);
    }

    public override JobHandle Update(ref SystemContext ctx, JobHandle dep) {
        if (typeof(IMainThreadSystem).IsAssignableFrom(typeof(T)))
        {
            dep.Complete();
            system.OnUpdate(ref ctx);
            return default;
        }

        if (typeof(IParallelBatchSystem).IsAssignableFrom(typeof(T)))
        {
            dep.Complete();
            var batchSystem = (IParallelBatchSystem)(object)system;
            batchSystem.OnPrepare(ref ctx, out int total, out int batchSize);
            if (total <= 0) return default;
            return batchSystem.ScheduleBatches(ref ctx, total, batchSize, dep);
        }

        var singleJob = new Job { System = system, Context = ctx };
        return singleJob.Schedule(dep);
    }

    public override void Destroy(ref SystemContext ctx) {
        system.OnDestroy(ref ctx);
    }

    [BurstCompile]
    struct Job : IJob {
        public T System;
        public SystemContext Context;
        public void Execute() {
            System.OnUpdate(ref Context);
        }
    }
}
