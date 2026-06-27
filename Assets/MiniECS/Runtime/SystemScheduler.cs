
using Unity.Jobs;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class SystemScheduler {
    List<SystemWrapper> active = new();
    SystemContext ctx;

    public static SystemScheduler Main { get; private set; }

    public readonly Dictionary<string, double> LastFrameTimeMs = new();

    public SystemScheduler(bool server, bool client) {
        Main = this;
        ctx = new SystemContext { IsServer = server, IsClient = client };
        foreach (var s in SystemRegistry.Systems) {
            if (server && s.IsServer) active.Add(s);
            if (client && s.IsClient) active.Add(s);
            LastFrameTimeMs[s.DisplayName] = 0;
        }
        foreach (var s in active) s.Create(ref ctx);
    }

    public void Update(float dt) {
        ctx.DeltaTime = dt;
        JobHandle h = default;
        var sw = new Stopwatch();
        foreach (var s in active) {
            sw.Restart();
            h = s.Update(ref ctx, h);
            sw.Stop();
            LastFrameTimeMs[s.DisplayName] = sw.Elapsed.TotalMilliseconds;
        }
        h.Complete();
    }
}
