#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using MiniECS.Rendering;
using MiniECS.Rendering.Authoring;

namespace MiniECS.Rendering.Authoring.Editor
{
    [CustomEditor(typeof(VatSubScene))]
    public class VatSubSceneEditor : UnityEditor.Editor
    {
        List<GameObject> _queue;
        Dictionary<GameObject, List<Transform>> _groups;
        int _idx;
        IEnumerator _current;
        VatRuntimeBaker.BakeHandle _handle;
        VatRuntimeBaker.VatBakeSettings _settings;
        bool _baking;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUI.BeginDisabledGroup(_baking);
            if (GUILayout.Button("Bake SubScene (Editor)", GUILayout.Height(28)))
                StartBake();
            EditorGUI.EndDisabledGroup();

            if (_baking)
                EditorGUILayout.HelpBox("Baking in progress...", MessageType.Info);

            EditorGUILayout.Space();
            if (GUILayout.Button("Clear cached assets"))
                ClearCache();
        }

        void StartBake()
        {
            var t = (VatSubScene)target;

            var renderers = new List<Renderer>();
            t.GetComponentsInChildren<Renderer>(true, renderers);

            _groups = new Dictionary<GameObject, List<Transform>>();
            foreach (var r in renderers)
            {
                if (r.transform == t.transform) continue;
                var root = DirectChild(r.transform, t.transform);
                if (root == null) continue;
                var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(root.gameObject) ?? root.gameObject;
                if (!_groups.ContainsKey(src)) _groups[src] = new List<Transform>();
                if (!_groups[src].Contains(root)) _groups[src].Add(root);
            }

            if (_groups.Count == 0)
            {
                EditorUtility.DisplayDialog("VAT SubScene", "No prefab children found to bake.", "OK");
                return;
            }

            _queue = new List<GameObject>(_groups.Keys);
            _idx = 0;
            _current = null;
            _baking = true;

            _settings = new VatRuntimeBaker.VatBakeSettings
            {
                frameRate = t.frameRate,
                normalHalfPrecision = t.normalHalfPrecision,
                lod2Distance = t.lod2Distance,
                lod3Distance = t.lod3Distance,
                cullDistance = t.cullDistance
            };

            EditorApplication.update += Pump;
        }

        void Pump()
        {
            if (_idx >= _queue.Count)
            {
                Finish();
                return;
            }

            if (_current == null)
            {
                var src = _queue[_idx];
                _handle = new VatRuntimeBaker.BakeHandle();
                _current = VatRuntimeBaker.BakePrefabCoroutine(src, _settings, _handle);
            }

            bool more = _current.MoveNext();
            EditorUtility.DisplayProgressBar("VAT SubScene Bake",
                _handle.status, (_idx + _handle.progress) / _queue.Count);

            if (!more)
            {
                if (!_handle.failed && _handle.result != null)
                {
                    string path = VatRuntimeBaker.SaveAsset(_handle.result);
                    var asset = !string.IsNullOrEmpty(path)
                        ? AssetDatabase.LoadAssetAtPath<VatAnimationData>(path)
                        : _handle.result;

                    foreach (var root in _groups[_queue[_idx]])
                    {
                        var c = root.GetComponent<VatSubSceneChild>() ?? root.gameObject.AddComponent<VatSubSceneChild>();
                        c.overrideVatData = asset;
                        EditorUtility.SetDirty(root.gameObject);
                    }
                    EditorUtility.SetDirty(target);
                }
                else if (_handle.failed)
                {
                    Debug.LogError($"[VatSubSceneEditor] Bake failed: {_handle.status}");
                }

                _idx++;
                _current = null;
            }
        }

        void Finish()
        {
            EditorApplication.update -= Pump;
            EditorUtility.ClearProgressBar();
            _baking = false;
            _current = null;
            _groups = null;
            _queue = null;
        }

        void ClearCache()
        {
            var t = (VatSubScene)target;
            var renderers = new List<Renderer>();
            t.GetComponentsInChildren<Renderer>(true, renderers);
            foreach (var r in renderers)
            {
                if (r.transform == t.transform) continue;
                var root = DirectChild(r.transform, t.transform);
                if (root == null) continue;
                var c = root.GetComponent<VatSubSceneChild>();
                if (c != null) { c.overrideVatData = null; EditorUtility.SetDirty(root.gameObject); }
            }
            EditorUtility.SetDirty(target);
        }

        static Transform DirectChild(Transform t, Transform container)
        {
            while (t.parent != null && t.parent.parent != null)
                t = t.parent;
            return t;
        }

        void OnDisable()
        {
            if (_baking)
            {
                EditorApplication.update -= Pump;
                EditorUtility.ClearProgressBar();
                _baking = false;
            }
        }
    }
}
#endif
