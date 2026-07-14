using UnityEditor;
using UnityEngine;

namespace ECSGPU.GpuEcs.Editor
{
    public class GpuWorldInspectorWindow : EditorWindow
    {
        GpuWorld _world;

        [MenuItem("Tools/GPU ECS/World Inspector")]
        public static void ShowWindow()
        {
            GetWindow<GpuWorldInspectorWindow>("GPU World Inspector");
        }

        public void SetWorld(GpuWorld world)
        {
            _world = world;
        }

        void OnGUI()
        {
            if (_world == null)
            {
                EditorGUILayout.HelpBox("No GpuWorld assigned. Use GpuWorldDebug component in scene.", MessageType.Info);
                if (GUILayout.Button("Find GpuWorld in Scene"))
                {
                    var debug = FindObjectOfType<GpuWorldDebug>();
                    if (debug != null)
                        _world = debug.World;
                }
                return;
            }

            DrawWorldInspector(_world);
        }

        void Update()
        {
            if (_world == null)
            {
                var debug = FindObjectOfType<GpuWorldDebug>();
                if (debug != null)
                    _world = debug.World;
            }

            Repaint();
        }

        public static void DrawWorldInspector(GpuWorld world)
        {
            EditorGUILayout.LabelField("GPU ECS World", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Registered Buffers: {world.BufferCount}");
            EditorGUILayout.Space();

            foreach (var kvp in world.Buffers)
            {
                var type = kvp.Key;
                var buffer = kvp.Value;

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(type.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"  Count: {buffer.Count}");
                EditorGUILayout.LabelField($"  Stride: {buffer.Stride} bytes");
                EditorGUILayout.LabelField($"  Capacity: {buffer.Buffer.count}");
                long memBytes = (long)buffer.Buffer.count * buffer.Stride;
                EditorGUILayout.LabelField($"  Memory: {FormatBytes(memBytes)}");

                if (buffer.Buffer != null && buffer.Count > 0 && GUILayout.Button("Readback Data"))
                {
                    var array = System.Array.CreateInstance(type, buffer.Count);
                    buffer.GetDataRaw(array);
                    Debug.Log($"[GPU ECS] {type.Name} first entry: {array.GetValue(0)}");
                }

                EditorGUILayout.EndVertical();
            }

            long totalMem = 0;
            foreach (var buffer in world.Buffers.Values)
                totalMem += (long)buffer.Buffer.count * buffer.Stride;
            EditorGUILayout.LabelField($"Total GPU Memory: {FormatBytes(totalMem)}", EditorStyles.boldLabel);
        }

        static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}
