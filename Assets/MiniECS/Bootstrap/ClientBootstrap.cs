using MiniECS.Netcode;
using UnityEngine;

public class ClientBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(false, true);
        TryInitEcsSync();
    }

    void Update()
    {
        scheduler.Update(Time.deltaTime);
    }

    public static void TryInitEcsSync()
    {
        var mgr = FindAnyObjectByType<EcsNetworkManager>();
        if (mgr != null)
            mgr.TryInitialize();
        else
            Debug.LogWarning("[ClientBootstrap] EcsNetworkManager not found in scene.");
    }
}
