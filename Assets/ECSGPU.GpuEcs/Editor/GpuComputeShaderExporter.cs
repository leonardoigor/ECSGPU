using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ECSGPU.GpuEcs.Editor
{
    public static class GpuComputeShaderExporter
    {
        public const string OutputFolder = "Assets/Resources/ECSGPU.GpuEcs/Generated/ComputeShaders";

        static bool _exportQueued;

        [MenuItem("Tools/GPU ECS/Export GPU Kernels to ComputeShaders")]
        public static void ExportFromMenu()
        {
            ExportAll(logResult: true);
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            QueueExport();
        }

        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            QueueExport();
        }

        public static void QueueExport()
        {
            if (_exportQueued)
                return;

            _exportQueued = true;
            EditorApplication.delayCall += ExportWhenReady;
        }

        public static void ExportAll(bool logResult)
        {
            string outputFullPath = GetOutputFullPath();
            Directory.CreateDirectory(outputFullPath);

            var exportedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int exportedCount = 0;

            foreach (var systemType in TypeCache.GetTypesWithAttribute<GpuSystemAttribute>().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (!TryGetGeneratedHlsl(systemType, out string hlsl))
                    continue;

                string assetPath = $"{OutputFolder}/{systemType.Name}.compute";
                string fullPath = ToFullPath(assetPath);
                exportedFiles.Add(fullPath);

                string previous = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                if (!string.Equals(previous, hlsl, StringComparison.Ordinal))
                {
                    File.WriteAllText(fullPath, hlsl, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    exportedCount++;
                }
            }

            foreach (string file in Directory.EnumerateFiles(outputFullPath, "*.compute", SearchOption.TopDirectoryOnly))
            {
                if (exportedFiles.Contains(file))
                    continue;

                string assetPath = ToAssetPath(file);
                AssetDatabase.DeleteAsset(assetPath);
            }

            if (logResult)
                Debug.Log($"[GPU ECS] Exported generated compute shaders to {OutputFolder}. Updated {exportedCount} file(s).");
        }

        static bool TryGetGeneratedHlsl(Type systemType, out string hlsl)
        {
            hlsl = null;
            string generatedTypeName = $"ECSGPU.GpuEcs.Generated.{systemType.Name}_ComputeShader";

            Type generatedType = systemType.Assembly.GetType(generatedTypeName, throwOnError: false);
            if (generatedType == null)
            {
                generatedType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(generatedTypeName, throwOnError: false))
                    .FirstOrDefault(t => t != null);
            }

            if (generatedType == null)
                return false;

            FieldInfo hlslField = generatedType.GetField("HLSL", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (hlslField == null || hlslField.FieldType != typeof(string))
                return false;

            hlsl = hlslField.GetRawConstantValue() as string;
            return !string.IsNullOrWhiteSpace(hlsl);
        }

        static void ExportWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ExportWhenReady;
                return;
            }

            _exportQueued = false;
            ExportAll(logResult: false);
        }

        static string GetOutputFullPath()
        {
            return ToFullPath(OutputFolder);
        }

        static string ToFullPath(string assetPath)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        static string ToAssetPath(string fullPath)
        {
            string projectRoot = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(projectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/')
                : normalized.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
