using UnityEngine;
using System.Collections.Generic;
using MiniECS;
using MiniECS.Rendering;

namespace MiniECS.Rendering.Authoring
{
    /// <summary>
    /// "SubScene for VAT": a container GameObject that acts like a Unity DOTS
    /// SubScene. Drop prefab instances inside it (as children in the hierarchy);
    /// on Play each child is converted to a VAT ECS entity, preserving its world
    /// transform, and the original child GameObjects are destroyed.
    ///
    /// Baking is an Editor-only operation. The editor "Bake SubScene" button bakes
    /// each unique prefab to a cached VatAnimationData asset and stores it on a
    /// <see cref="VatSubSceneChild"/> component on every child. In a built player
    /// the conversion reads those cached assets — it never bakes.
    /// </summary>
    [AddComponentMenu("MiniECS/VAT SubScene")]
    public class VatSubScene : MonoBehaviour
    {
        [Header("Bake Settings")]
        public float frameRate = 30f;
        public bool normalHalfPrecision;
        public float lod2Distance = 20f;
        public float lod3Distance = 50f;
        public float cullDistance = 100f;

        [Header("Conversion")]
        public bool convertOnPlay = true;
        public bool useCollider = true;
        public bool randomColors = false;

        [System.NonSerialized] bool _converting;
        [System.NonSerialized] string _status = "";
        [System.NonSerialized] float _progress;

        void Start()
        {
            if (convertOnPlay)
                StartCoroutine(ConvertSubScene());
        }

        System.Collections.IEnumerator ConvertSubScene()
        {
            _converting = true;
            _progress = 0f;

            var renderers = new List<Renderer>();
            GetComponentsInChildren<Renderer>(true, renderers);

            var sessionCache = new Dictionary<GameObject, VatAnimationData>();
            var roots = new HashSet<Transform>();
            int total = 0;
            int processed = 0;

            foreach (var r in renderers)
            {
                if (r.transform == transform) continue;
                var root = GetDirectChild(r.transform);
                if (root == null) continue;
                roots.Add(root);
            }
            total = roots.Count;

            var outVat = new VatAnimationData[1];

            foreach (var root in roots)
            {
                outVat[0] = null;
                var r = ResolveVat(root, sessionCache, outVat);
                while (r.MoveNext())
                    yield return r.Current;

                var vat = outVat[0];
                if (vat == null)
                {
                    processed++;
                    continue;
                }

                vat.ComputeSqrDistances();
                int modelId = ModelRegistry.Register(vat.originalMesh, vat.animationMaterial, vat);

                int initialFrameCount = vat.clips != null && vat.clips.Length > 0 ? vat.clips[0].frameCount : 1;
                float initialFrameRate = vat.clips != null && vat.clips.Length > 0 ? vat.clips[0].frameRate : 30f;

                var entity = EntityManager.CreateEntity();
                EntityManager.Add(entity, new PositionComponent { Value = root.position });
                EntityManager.Add(entity, new RotationComponent { Value = root.rotation });
                EntityManager.Add(entity, new ModelIdComponent { Value = modelId });
                EntityManager.Add(entity, new LodComponent { LodModelId = modelId, LodLevel = 1 });
                EntityManager.Add(entity, new AnimationComponent
                {
                    AnimIndex = 0,
                    CurrentFrame = 0f,
                    Speed = 1f,
                    FrameCount = initialFrameCount,
                    FrameRate = initialFrameRate
                });

                if (randomColors)
                    EntityManager.Add(entity, new ColorComponent { Value = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.5f, 1f) });

                if (vat.HasCollider && useCollider)
                    EntityManager.Add(entity, vat.BuildColliderComponent(vat.originalMesh));

                processed++;
                _progress = total > 0 ? (float)processed / total : 1f;
                _status = $"Converted {processed}/{total}: {root.name}";
                yield return null;
            }

            foreach (var root in roots)
            {
                if (root != null && root.gameObject != null)
                    Destroy(root.gameObject);
            }

            _converting = false;
        }

        System.Collections.IEnumerator ResolveVat(Transform root, Dictionary<GameObject, VatAnimationData> sessionCache, VatAnimationData[] outVat)
        {
            outVat[0] = null;

            var childComp = root.GetComponent<VatSubSceneChild>();
            if (childComp != null && childComp.overrideVatData != null)
            {
                outVat[0] = childComp.overrideVatData;
                yield break;
            }

#if UNITY_EDITOR
            GameObject source = GetPrefabSource(root.gameObject) ?? root.gameObject;
            if (sessionCache.TryGetValue(source, out var cached))
            {
                outVat[0] = cached;
                yield break;
            }

            var handle = new VatRuntimeBaker.BakeHandle();
            var settings = new VatRuntimeBaker.VatBakeSettings
            {
                frameRate = frameRate,
                normalHalfPrecision = normalHalfPrecision,
                lod2Distance = lod2Distance,
                lod3Distance = lod3Distance,
                cullDistance = cullDistance
            };

            var routine = VatRuntimeBaker.BakePrefabCoroutine(source, settings, handle);
            while (routine.MoveNext())
            {
                _status = handle.status;
                yield return routine.Current;
            }

            if (handle.failed || handle.result == null)
            {
                Debug.LogError($"[VatSubScene] Bake failed for {root.name}: {handle.status}", this);
                sessionCache[source] = null;
                yield break;
            }

            sessionCache[source] = handle.result;
            if (childComp == null) childComp = root.gameObject.AddComponent<VatSubSceneChild>();
            childComp.overrideVatData = handle.result;
            outVat[0] = handle.result;
            yield break;
#else
            Debug.LogError($"[VatSubScene] Child '{root.name}' has no cached VatAnimationData. " +
                "Bake once in the editor (Bake SubScene button) before building.", this);
            yield break;
#endif
        }

        static Transform GetDirectChild(Transform t)
        {
            while (t.parent != null && t.parent.parent != null)
                t = t.parent;
            return t;
        }

#if UNITY_EDITOR
        static GameObject GetPrefabSource(GameObject go)
        {
            return UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(go);
        }
#endif

        void OnGUI()
        {
            if (!_converting)
                return;

            float w = 460f;
            float h = 90f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.Box(new Rect(x - 10, y - 10, w + 20, h + 20), "");
            GUI.Label(new Rect(x, y, w, 24), "VAT SubScene — converting, please wait");
            GUI.Label(new Rect(x, y + 28, w, 20), _status);

            float barW = w;
            GUI.Box(new Rect(x, y + 54, barW, 18), "");
            float fill = Mathf.Clamp01(_progress);
            GUI.Box(new Rect(x, y + 54, barW * fill, 18), "");
            GUI.Label(new Rect(x, y + 54, barW, 18), $"{Mathf.RoundToInt(fill * 100f)}%");
        }
    }
}
