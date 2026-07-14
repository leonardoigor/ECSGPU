using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ECSGPU.GpuEcs.Editor
{
    public class ComputeShaderGeneratorWindow : EditorWindow
    {
        Vector2 _scrollPos;
        string _generatedHlsl = "";
        Type[] _gpuComponentTypes;
        Type[] _gpuSystemTypes;

        [MenuItem("Tools/GPU ECS/Generator Window")]
        public static void ShowWindow()
        {
            GetWindow<ComputeShaderGeneratorWindow>("GPU ECS Generator");
        }

        void OnEnable()
        {
            ScanComponentTypes();
            _generatedHlsl = GpuComponentHlslExporter.BuildHlsl();
        }

        void ScanComponentTypes()
        {
            _gpuComponentTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => t.IsValueType && !t.IsAbstract &&
                           t.GetCustomAttribute<GpuComponentAttribute>() != null)
                .ToArray();

            _gpuSystemTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => t.IsValueType && !t.IsAbstract &&
                           t.GetCustomAttribute<GpuSystemAttribute>() != null)
                .ToArray();
        }

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                ScanComponentTypes();
                _generatedHlsl = GpuComponentHlslExporter.BuildHlsl();
            }
            if (GUILayout.Button("Export All", EditorStyles.toolbarButton, GUILayout.Width(80)))
                GenerateAll();
            if (GUILayout.Button("Export Kernels", EditorStyles.toolbarButton, GUILayout.Width(95)))
                GpuComputeShaderExporter.ExportAll(logResult: true);
            if (GUILayout.Button("Copy HLSL", EditorStyles.toolbarButton, GUILayout.Width(80)))
                EditorGUIUtility.systemCopyBuffer = _generatedHlsl;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Found {_gpuComponentTypes?.Length ?? 0} GPU component types", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Found {_gpuSystemTypes?.Length ?? 0} GPU system types", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            if (_gpuComponentTypes != null)
            {
                foreach (var type in _gpuComponentTypes)
                {
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField(type.Name, EditorStyles.boldLabel);

                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        string hlslType = GpuTypeMapper.GetHlslType(field.FieldType) ?? "???";
                        EditorGUILayout.LabelField($"  {hlslType} {field.Name}", EditorStyles.miniLabel);
                    }

                    if (GUILayout.Button("Copy Struct HLSL"))
                    {
                        EditorGUIUtility.systemCopyBuffer = GpuTypeMapper.GenerateHlslStruct(type);
                    }

                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated HLSL:", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(_generatedHlsl, GUILayout.MinHeight(200));

            EditorGUILayout.EndScrollView();
        }

        void GenerateAll()
        {
            ScanComponentTypes();
            _generatedHlsl = GpuComponentHlslExporter.BuildHlsl();
            GpuComponentHlslExporter.ExportNow(logResult: true);
            GpuComputeShaderExporter.ExportAll(logResult: true);
        }
    }
}
