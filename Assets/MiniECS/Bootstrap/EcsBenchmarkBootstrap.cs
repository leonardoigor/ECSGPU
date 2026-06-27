using UnityEngine;

public class EcsBenchmarkBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(true, true);
    }

    void Update()
    {
        if (scheduler == null) return;
        scheduler.Update(Time.deltaTime);
    }

    void OnDestroy()
    {
        var ctx = new SystemContext();
        foreach (var s in SystemRegistry.Systems)
            s.Destroy(ref ctx);
    }
}
