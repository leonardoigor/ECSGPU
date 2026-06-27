using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
}
