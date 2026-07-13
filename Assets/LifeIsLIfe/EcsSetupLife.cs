using MiniECS;
using MiniECS.Rendering;
using UnityEngine;

public class EcsSetupLife : MonoBehaviour
{
    SystemScheduler scheduler;


    // Start is called before the first frame update
    void Awake()
    {
        EntityManager.Reset();
        ModelRegistry.Clear();
        SpatialHashManager.Dispose();
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(true, true);
    }

    // Update is called once per frame
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
        SpatialHashManager.Dispose();
    }
}
