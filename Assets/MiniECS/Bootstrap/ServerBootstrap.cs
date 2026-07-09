using MiniECS;
using MiniECS.Netcode;
using MiniECS.Rendering;
using UnityEngine;

public class ServerBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
        EntityManager.Reset();
        ModelRegistry.Clear();
        SpatialHashManager.Dispose();
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(true, false);
        TryInitEcsSync();
    }

    void FixedUpdate()
    {
        scheduler.Update(Time.fixedDeltaTime);
    }

    public static void TryInitEcsSync()
    {
        var mgr = FindAnyObjectByType<EcsNetworkManager>();
        if (mgr != null)
            mgr.TryInitialize();
        else
            Debug.LogWarning("[ServerBootstrap] EcsNetworkManager not found in scene.");
    }
}
