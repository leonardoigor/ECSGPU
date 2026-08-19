using UnityEngine;
using Unity.Mathematics;
using MiniECS.Physics;
using System.Collections;
using System.Collections.Generic;

namespace MiniECS.Rendering.Authoring
{
    /// <summary>
    /// Runtime-safe VAT baker. Contains the pure conversion math (no UnityEditor
    /// dependency except for the guarded <see cref="SaveAsset"/>). The conversion
    /// is exposed as coroutines so the animation sampling can be spread across many
    /// frames with a loading screen instead of freezing the main thread.
    ///
    /// Baking happens in the Editor only; in a built player the produced
    /// VatAnimationData asset is loaded directly (see VatPrefabAuthoring).
    /// </summary>
    public static class VatRuntimeBaker
    {
        public class VatBakeSettings
        {
            public float frameRate = 30f;
            public bool normalHalfPrecision;
            public float lod2Distance = 20f;
            public float lod3Distance = 50f;
            public float cullDistance = 100f;
        }

        /// <summary>
        /// Mutable progress reported by the bake coroutines so a loading screen
        /// can show what is currently being baked.
        /// </summary>
        public class BakeHandle
        {
            public VatAnimationData result;
            public string status = "Idle";
            public float progress; // 0..1
            public bool done;
            public bool failed;
        }

        // ───────────────────────── Public coroutine API ─────────────────────────

        /// <summary>
        /// Bakes a prefab instance into a VatAnimationData across multiple frames.
        /// Instantiates the prefab, collects its animation clips and drives the
        /// conversion, yielding once per sampled frame so the main thread stays
        /// responsive. Reports progress through <paramref name="handle"/>.
        ///
        /// Implemented as a single (non-nested) coroutine so it works both with
        /// Unity's StartCoroutine and with a manual EditorApplication.update pump.
        /// </summary>
        public static IEnumerator BakePrefabCoroutine(GameObject prefab, VatBakeSettings settings, BakeHandle handle)
        {
            if (prefab == null)
            {
                handle.failed = true;
                handle.status = "No prefab assigned.";
                handle.done = true;
                yield break;
            }

            handle.status = $"Instantiating {prefab.name}...";
            handle.progress = 0f;
            yield return null;

            var source = Object.Instantiate(prefab);
            source.name = prefab.name + "_BakingTemp";

            var clips = CollectClips(source);
            var body = BakeBody(source, prefab.name, clips, settings, handle);
            while (body.MoveNext())
                yield return body.Current;

            Object.DestroyImmediate(source);
            handle.done = true;
        }

        /// <summary>
        /// Same as <see cref="BakePrefabCoroutine"/> but operates on an
        /// already-instantiated source GameObject (caller owns its lifetime).
        /// </summary>
        public static IEnumerator BakeCoroutine(GameObject source, List<AnimationClip> clips, VatBakeSettings settings, BakeHandle handle)
        {
            var body = BakeBody(source, source.name, clips, settings, handle);
            while (body.MoveNext())
                yield return body.Current;
        }

        static IEnumerator BakeBody(GameObject source, string modelName, List<AnimationClip> clips, VatBakeSettings settings, BakeHandle handle)
        {
            var animator = source.GetComponentInChildren<Animator>();
            var skinnedMR = source.GetComponentInChildren<SkinnedMeshRenderer>();
            var meshFilter = source.GetComponentInChildren<MeshFilter>();
            var meshRenderer = source.GetComponentInChildren<MeshRenderer>();

            var colliderInfo = CaptureCollider(source);

            bool isSkinned = skinnedMR != null && skinnedMR.sharedMesh != null;
            bool isStatic = !isSkinned && meshFilter != null && meshRenderer != null && meshFilter.sharedMesh != null;

            if (!isSkinned && !isStatic)
            {
                handle.failed = true;
                handle.status = "Prefab has no SkinnedMeshRenderer or MeshRenderer/MeshFilter.";
                yield break;
            }

            var bakedMesh = new Mesh();
            Mesh bakedReferenceMesh = null;

            int vertexCount;
            int totalFrames;
            AnimationClipInfo[] clipInfos;
            Texture2D posTex;
            Texture2D normTex;
            Material srcMat;

            if (isSkinned)
            {
                if (clips.Count == 0)
                {
                    // Fallback: bake a single static frame from the skinned pose.
                    handle.status = "No animation clips — baking single pose.";
                    clips = new List<AnimationClip>();
                }

                if (animator != null) animator.Update(0f);
                skinnedMR.BakeMesh(bakedMesh);
                vertexCount = bakedMesh.vertexCount;
                if (vertexCount <= 0)
                {
                    handle.failed = true;
                    handle.status = "Baked mesh has no vertices.";
                    Object.DestroyImmediate(bakedMesh);
                    yield break;
                }

                bakedReferenceMesh = Object.Instantiate(bakedMesh);
                bakedReferenceMesh.name = modelName + "_VAT_RenderMesh";

                totalFrames = 0;
                for (int c = 0; c < clips.Count; c++)
                {
                    int frames = Mathf.RoundToInt(clips[c].length * settings.frameRate);
                    if (frames < 1) frames = 1;
                    totalFrames += frames;
                }
                if (totalFrames < 1) totalFrames = 1;

                var posFormat = TextureFormat.RGBAHalf;
                var normFormat = settings.normalHalfPrecision ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;

                posTex = new Texture2D(vertexCount, totalFrames, posFormat, false)
                {
                    name = modelName + "_PositionVAT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                normTex = new Texture2D(vertexCount, totalFrames, normFormat, false)
                {
                    name = modelName + "_NormalVAT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                clipInfos = new AnimationClipInfo[Mathf.Max(1, clips.Count)];
                int currentRow = 0;

                for (int c = 0; c < clips.Count; c++)
                {
                    var clip = clips[c];
                    int frames = Mathf.RoundToInt(clip.length * settings.frameRate);
                    if (frames < 1) frames = 1;

                    clipInfos[c] = new AnimationClipInfo
                    {
                        name = clip.name,
                        startFrame = currentRow,
                        frameCount = frames,
                        frameRate = settings.frameRate
                    };

                    for (int f = 0; f < frames; f++)
                    {
                        float normalizedTime = frames > 1 ? f / (float)(frames - 1) : 0f;
                        clip.SampleAnimation(source, normalizedTime * clip.length);
                        if (animator != null) animator.Update(0f);
                        skinnedMR.BakeMesh(bakedMesh);

                        WriteBakedRow(bakedMesh, vertexCount, posTex, normTex, currentRow);

                        currentRow++;
                        handle.status = $"Baking clip '{clip.name}' ({c + 1}/{clips.Count}) — frame {f + 1}/{frames}";
                        handle.progress = (currentRow / (float)totalFrames) * 0.9f;
                        yield return null;
                    }
                }

                if (clips.Count == 0)
                {
                    clipInfos[0] = new AnimationClipInfo
                    {
                        name = "Static",
                        startFrame = 0,
                        frameCount = 1,
                        frameRate = settings.frameRate
                    };
                }

                handle.status = "Encoding VAT textures...";
                posTex.Apply(false, true);
                normTex.Apply(false, true);
                yield return null;

                srcMat = skinnedMR.sharedMaterial;
            }
            else
            {
                var staticMesh = meshFilter.sharedMesh;
                vertexCount = staticMesh.vertexCount;
                if (vertexCount <= 0)
                {
                    handle.failed = true;
                    handle.status = "Mesh has no vertices.";
                    Object.DestroyImmediate(bakedMesh);
                    yield break;
                }

                var meshCopy = Object.Instantiate(staticMesh);
                var normals = meshCopy.normals;
                if (normals == null || normals.Length != meshCopy.vertexCount)
                    meshCopy.RecalculateNormals();

                bakedReferenceMesh = Object.Instantiate(meshCopy);
                bakedReferenceMesh.name = modelName + "_VAT_RenderMesh";

                totalFrames = 1;
                clipInfos = new AnimationClipInfo[1];
                clipInfos[0] = new AnimationClipInfo
                {
                    name = "Static",
                    startFrame = 0,
                    frameCount = 1,
                    frameRate = settings.frameRate
                };

                var staticPosFormat = TextureFormat.RGBAHalf;
                var staticNormFormat = settings.normalHalfPrecision ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;

                posTex = new Texture2D(vertexCount, totalFrames, staticPosFormat, false)
                {
                    name = modelName + "_PositionVAT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                normTex = new Texture2D(vertexCount, totalFrames, staticNormFormat, false)
                {
                    name = modelName + "_NormalVAT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                WriteBakedRow(meshCopy, vertexCount, posTex, normTex, 0);

                handle.status = "Encoding VAT textures...";
                posTex.Apply(false, true);
                normTex.Apply(false, true);
                yield return null;

                srcMat = meshRenderer.sharedMaterial;
                Object.DestroyImmediate(meshCopy);
            }

            handle.status = "Building VAT materials...";
            handle.progress = 0.95f;
            yield return null;

            var vatData = BuildVatData(modelName, posTex, normTex, bakedReferenceMesh, clipInfos,
                vertexCount, totalFrames, colliderInfo, srcMat, settings);

            handle.result = vatData;
            handle.progress = 1f;
            handle.status = "Bake complete.";

            Object.DestroyImmediate(bakedMesh);
        }

        // ───────────────────────── Build helpers ─────────────────────────

        static void WriteBakedRow(Mesh mesh, int vertexCount, Texture2D posTex, Texture2D normTex, int row)
        {
            var verts = mesh.vertices;
            var norms = mesh.normals;

            var posColors = new Color[vertexCount];
            var normColors = new Color[vertexCount];

            for (int v = 0; v < vertexCount && v < verts.Length; v++)
            {
                var p = verts[v];
                posColors[v] = new Color(p.x, p.y, p.z, 1f);

                var n = norms[v];
                normColors[v] = new Color(
                    n.x * 0.5f + 0.5f,
                    n.y * 0.5f + 0.5f,
                    n.z * 0.5f + 0.5f,
                    1f
                );
            }

            posTex.SetPixels(0, row, vertexCount, 1, posColors);
            normTex.SetPixels(0, row, vertexCount, 1, normColors);
        }

        static VatAnimationData BuildVatData(string name, Texture2D posTex, Texture2D normTex,
            Mesh bakedReferenceMesh, AnimationClipInfo[] clipInfos, int vertexCount, int totalFrames,
            VatColliderInfo colliderInfo, Material srcMat, VatBakeSettings settings)
        {
            var vatData = ScriptableObject.CreateInstance<VatAnimationData>();
            vatData.name = name + "_VAT";
            vatData.positionTexture = posTex;
            vatData.normalTexture = normTex;
            vatData.originalMesh = bakedReferenceMesh;
            vatData.clips = clipInfos;
            vatData.vertexCount = vertexCount;
            vatData.totalFrames = totalFrames;
            vatData.colliderInfo = colliderInfo;

            ExtractMaterialProperties(srcMat, vatData);

            var shader = Shader.Find("Custom/VatPBR");
            if (shader == null)
                shader = Shader.Find("Custom/VatInstancing");

            Material mat;
            if (shader != null)
            {
                mat = new Material(shader);
                mat.name = name + "_VAT_Material";
                mat.enableInstancing = true;
                mat.SetTexture("_PositionVAT", posTex);
                mat.SetTexture("_NormalVAT", normTex);
                mat.SetFloat("_VATWidth", vertexCount);
                mat.SetFloat("_VATHeight", totalFrames);

                if (shader == Shader.Find("Custom/VatPBR"))
                    ApplyMaterialToVatPBR(srcMat, mat, vatData);
                else
                    mat.SetColor("_Color", vatData.albedoTint);
            }
            else
            {
                Debug.LogError("[VatRuntimeBaker] Neither Custom/VatPBR nor Custom/VatInstancing shader found!");
                mat = new Material(Shader.Find("Standard"));
            }

            vatData.animationMaterial = mat;

            var simpleShader = Shader.Find("Custom/VatSimple");
            var unlitShader = Shader.Find("Custom/VatUnlit");

            Material lodLowMat = null;
            if (simpleShader != null)
            {
                lodLowMat = new Material(simpleShader);
                lodLowMat.name = name + "_LOD2_Material";
                lodLowMat.enableInstancing = true;
                lodLowMat.SetTexture("_PositionVAT", posTex);
                lodLowMat.SetTexture("_NormalVAT", normTex);
                lodLowMat.SetFloat("_VATWidth", vertexCount);
                lodLowMat.SetFloat("_VATHeight", totalFrames);
                ApplyMaterialToLodMaterial(srcMat, lodLowMat, vatData, false);
            }

            Material lodUltraMat = null;
            if (unlitShader != null)
            {
                lodUltraMat = new Material(unlitShader);
                lodUltraMat.name = name + "_LOD3_Material";
                lodUltraMat.enableInstancing = true;
                lodUltraMat.SetTexture("_PositionVAT", posTex);
                lodUltraMat.SetFloat("_VATWidth", vertexCount);
                lodUltraMat.SetFloat("_VATHeight", totalFrames);
                ApplyMaterialToLodMaterial(srcMat, lodUltraMat, vatData, true);
            }

            vatData.lodLowMaterial = lodLowMat;
            vatData.lodUltraMaterial = lodUltraMat;

            vatData.lod2Distance = settings.lod2Distance;
            vatData.lod3Distance = settings.lod3Distance;
            vatData.cullDistance = settings.cullDistance;
            vatData.ComputeSqrDistances();

            return vatData;
        }

        static List<AnimationClip> CollectClips(GameObject go)
        {
            var result = new List<AnimationClip>();
            var seen = new HashSet<AnimationClip>();

            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (var c in animator.runtimeAnimatorController.animationClips)
                    if (c != null && seen.Add(c)) result.Add(c);
            }

            var legacy = go.GetComponentInChildren<Animation>();
            if (legacy != null)
            {
                foreach (AnimationState state in legacy)
                    if (state.clip != null && seen.Add(state.clip)) result.Add(state.clip);
            }

            return result;
        }

        // ───────────────────────── Collider capture ─────────────────────────

        public static VatColliderInfo CaptureCollider(GameObject root)
        {
            var info = new VatColliderInfo { hasCollider = false, meshHullIndex = -1 };
            var col = root.GetComponentInChildren<Collider>();
            if (col == null)
                return info;

            info.hasCollider = true;
            info.isTrigger = col.isTrigger;
            info.layer = col.gameObject.layer;

            Transform rootT = root.transform;
            Vector3 WorldCenterOf(Vector3 localCenter) => col.transform.TransformPoint(localCenter);
            float3 RootLocal(Vector3 world) => (float3)rootT.InverseTransformPoint(world);

            if (col is BoxCollider bc)
            {
                info.shape = ColliderShapeType.Box;
                info.centerOffset = RootLocal(WorldCenterOf(bc.center));
                info.halfExtents = (float3)(bc.size * 0.5f);
            }
            else if (col is SphereCollider sc)
            {
                info.shape = ColliderShapeType.Sphere;
                info.centerOffset = RootLocal(WorldCenterOf(sc.center));
                info.radius = sc.radius;
            }
            else if (col is CapsuleCollider cap)
            {
                info.shape = ColliderShapeType.Capsule;
                info.centerOffset = RootLocal(WorldCenterOf(cap.center));
                info.radius = cap.radius;
                info.capsuleHeight = cap.height;
                info.capsuleAxis = (int)cap.direction;
            }
            else if (col is MeshCollider)
            {
                info.shape = ColliderShapeType.ConvexMesh;
                info.meshHullIndex = -1;
            }
            else
            {
                info.shape = ColliderShapeType.Box;
                var b = col.bounds;
                info.centerOffset = RootLocal(b.center);
                info.halfExtents = (float3)b.extents;
            }

            return info;
        }

        // ───────────────────────── Material helpers ─────────────────────────

        static void ExtractMaterialProperties(Material src, VatAnimationData vatData)
        {
            if (src == null) return;

            if (src.HasProperty("_MainTex"))
                vatData.albedoTexture = src.GetTexture("_MainTex") as Texture2D;
            if (src.HasProperty("_BumpMap"))
                vatData.normalMapTexture = src.GetTexture("_BumpMap") as Texture2D;
            if (src.HasProperty("_MetallicGlossMap"))
                vatData.metallicTexture = src.GetTexture("_MetallicGlossMap") as Texture2D;
            if (src.HasProperty("_EmissionMap"))
                vatData.emissionTexture = src.GetTexture("_EmissionMap") as Texture2D;
            if (src.HasProperty("_OcclusionMap"))
                vatData.occlusionTexture = src.GetTexture("_OcclusionMap") as Texture2D;

            if (src.HasProperty("_Color"))
                vatData.albedoTint = src.GetColor("_Color");
            if (src.HasProperty("_EmissionColor"))
                vatData.emissionColor = src.GetColor("_EmissionColor");
            if (src.HasProperty("_Metallic"))
                vatData.metallic = src.GetFloat("_Metallic");
            if (src.HasProperty("_Glossiness"))
                vatData.smoothness = src.GetFloat("_Glossiness");
            else if (src.HasProperty("_Smoothness"))
                vatData.smoothness = src.GetFloat("_Smoothness");
            if (src.HasProperty("_BumpScale"))
                vatData.normalScale = src.GetFloat("_BumpScale");
            if (src.HasProperty("_OcclusionStrength"))
                vatData.occlusionStrength = src.GetFloat("_OcclusionStrength");
        }

        static void ApplyMaterialToVatPBR(Material src, Material dst, VatAnimationData vatData)
        {
            if (vatData.albedoTexture != null)
                dst.SetTexture("_MainTex", vatData.albedoTexture);
            if (vatData.normalMapTexture != null)
                dst.SetTexture("_BumpMap", vatData.normalMapTexture);
            if (vatData.metallicTexture != null)
                dst.SetTexture("_MetallicGlossMap", vatData.metallicTexture);
            if (vatData.emissionTexture != null)
                dst.SetTexture("_EmissionMap", vatData.emissionTexture);
            if (vatData.occlusionTexture != null)
                dst.SetTexture("_OcclusionMap", vatData.occlusionTexture);

            dst.SetColor("_Color", vatData.albedoTint);
            dst.SetFloat("_Metallic", vatData.metallic);
            dst.SetFloat("_Glossiness", vatData.smoothness);
            dst.SetFloat("_BumpScale", vatData.normalScale);
            dst.SetFloat("_OcclusionStrength", vatData.occlusionStrength);
            dst.SetColor("_EmissionColor", vatData.emissionColor);

            if (src != null && src.HasProperty("_Color"))
                dst.SetColor("_Color", src.GetColor("_Color") * vatData.albedoTint);
        }

        static void ApplyMaterialToLodMaterial(Material src, Material dst, VatAnimationData vatData, bool unlit)
        {
            if (vatData.albedoTexture != null)
                dst.SetTexture("_MainTex", vatData.albedoTexture);

            dst.SetColor("_Color", vatData.albedoTint);

            if (src != null && src.HasProperty("_Color"))
                dst.SetColor("_Color", src.GetColor("_Color") * vatData.albedoTint);

            if (!unlit)
            {
                if (vatData.metallicTexture != null)
                    dst.SetTexture("_MetallicGlossMap", vatData.metallicTexture);
                if (vatData.emissionTexture != null)
                    dst.SetTexture("_EmissionMap", vatData.emissionTexture);

                dst.SetFloat("_Metallic", vatData.metallic);
                dst.SetFloat("_Glossiness", vatData.smoothness);
                dst.SetColor("_EmissionColor", vatData.emissionColor);
            }
        }

        // ───────────────────────── Editor-only asset save ─────────────────────────

#if UNITY_EDITOR
        public static string SaveAsset(VatAnimationData vatData, string outputRoot = "Assets/VATs")
        {
            if (vatData == null) return null;

            if (!UnityEditor.AssetDatabase.IsValidFolder(outputRoot))
            {
                var parent = System.IO.Path.GetDirectoryName(outputRoot);
                var folder = System.IO.Path.GetFileName(outputRoot);
                if (parent != null && UnityEditor.AssetDatabase.IsValidFolder(parent))
                    UnityEditor.AssetDatabase.CreateFolder(parent, folder);
                else
                    outputRoot = "Assets";
            }

            string name = vatData.name.Replace("_VAT", "");
            string assetPath = System.IO.Path.Combine(outputRoot, name + "_VAT.asset");
            var existing = UnityEditor.AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null)
                UnityEditor.AssetDatabase.DeleteAsset(assetPath);

            UnityEditor.AssetDatabase.CreateAsset(vatData, assetPath);
            UnityEditor.AssetDatabase.AddObjectToAsset(vatData.positionTexture, vatData);
            UnityEditor.AssetDatabase.AddObjectToAsset(vatData.normalTexture, vatData);
            UnityEditor.AssetDatabase.AddObjectToAsset(vatData.animationMaterial, vatData);
            UnityEditor.AssetDatabase.AddObjectToAsset(vatData.originalMesh, vatData);

            SaveTextureSubAsset(vatData, vatData.albedoTexture, name + "_Albedo");
            SaveTextureSubAsset(vatData, vatData.normalMapTexture, name + "_NormalMap");
            SaveTextureSubAsset(vatData, vatData.metallicTexture, name + "_Metallic");
            SaveTextureSubAsset(vatData, vatData.emissionTexture, name + "_Emission");
            SaveTextureSubAsset(vatData, vatData.occlusionTexture, name + "_Occlusion");

            if (vatData.lodLowMaterial != null)
                UnityEditor.AssetDatabase.AddObjectToAsset(vatData.lodLowMaterial, vatData);
            if (vatData.lodUltraMaterial != null)
                UnityEditor.AssetDatabase.AddObjectToAsset(vatData.lodUltraMaterial, vatData);

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
            return assetPath;
        }

        static void SaveTextureSubAsset(VatAnimationData vatData, Texture2D tex, string subName)
        {
            if (tex == null) return;
            if (tex == vatData.positionTexture || tex == vatData.normalTexture) return;
            if (!string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(tex))) return;
            tex.name = subName;
            UnityEditor.AssetDatabase.AddObjectToAsset(tex, vatData);
        }
#endif
    }
}
