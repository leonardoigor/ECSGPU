using MiniECS.Netcode;
using UnityEngine;

public class ServerBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
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
