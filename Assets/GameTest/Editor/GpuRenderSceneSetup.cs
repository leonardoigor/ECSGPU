using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MiniECS.Rendering;
using System.Collections.Generic;

public static class GpuRenderSceneSetup
{
    [MenuItem("GameTest/Setup GPU Render Test Scene")]
    static void Setup()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var bootstrap = new GameObject("_Bootstrap");
        bootstrap.AddComponent<EcsBenchmarkBootstrap>();

        var spawner = new GameObject("_Spawner");
        var gpuSpawner = spawner.AddComponent<GpuRenderSpawner>();
        gpuSpawner.count = 10000;
        gpuSpawner.radius = 50f;

        string path = "Assets/Scenes/GpuRenderTest.unity";
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[SceneSetup] Scene saved to {path}");
    }

    [MenuItem("GameTest/Setup VAT GPU Render Test Scene")]
    static void SetupVat()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var bootstrap = new GameObject("_Bootstrap");
        bootstrap.AddComponent<EcsBenchmarkBootstrap>();

        var spawner = new GameObject("_Spawner");
        var vatSpawner = spawner.AddComponent<VatGpuRenderSpawner>();
        vatSpawner.count = 5000;
        vatSpawner.radius = 50f;
        vatSpawner.flipFlop = true;
        vatSpawner.enableRandomizer = true;
        vatSpawner.minInterval = 1f;
        vatSpawner.maxInterval = 10f;

        var vats = new List<VatAnimationData>();
        var guids = AssetDatabase.FindAssets("t:VatAnimationData");
        foreach (var guid in guids)
        {
            var vat = AssetDatabase.LoadAssetAtPath<VatAnimationData>(AssetDatabase.GUIDToAssetPath(guid));
            if (vat != null)
                vats.Add(vat);
        }

        if (vats.Count == 0)
        {
            Debug.LogWarning("[SceneSetup] No VatAnimationData assets found. Assign manually in the inspector.");
        }
        else
        {
            vatSpawner.vatModels = vats.ToArray();
            Debug.Log($"[SceneSetup] Found {vats.Count} VatAnimationData asset(s).");
        }

        string path = "Assets/Scenes/GpuRenderVatTest.unity";
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[SceneSetup] VAT scene saved to {path}");
    }
}
