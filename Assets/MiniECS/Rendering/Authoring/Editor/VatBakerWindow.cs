using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace MiniECS.Rendering.Authoring.Editor
{
    public class VatBakerWindow : EditorWindow
    {
        class AssetEntry
        {
            public GameObject prefab;
            public string assetPath;
            public string label;
            public SkinnedMeshRenderer skin;
            public Mesh sharedMesh;
            public int vertexCount;
            public List<AnimationClip> clips = new();
            public bool selected = true;
            public bool baked;
            public bool expanded;
        }

        Vector2 scrollPos;
        List<AssetEntry> entries = new();
        float frameRate = 30f;
        bool normalHalfPrecision;
        string outputRoot = "Assets/VATs";
        string scanScope = "Assets";
        bool hasScanned;
        AnimationClip clipToAdd;

        [MenuItem("Tools/MiniECS/VAT Baker")]
        static void Open()
        {
            GetWindow<VatBakerWindow>("VAT Baker");
        }

        void OnEnable()
        {
            hasScanned = false;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("VAT Baker — Batch", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawConfig();
            DrawScanArea();
            DrawEntryList();
            DrawBakeButtons();
        }

        void DrawConfig()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scan Folder", GUILayout.Width(80));
            scanScope = EditorGUILayout.TextField(scanScope);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Scan Folder", scanScope, "");
                if (!string.IsNullOrEmpty(chosen))
                {
                    if (chosen.StartsWith(Application.dataPath))
                        scanScope = "Assets" + chosen.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("Invalid", "Must be inside Assets/.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            outputRoot = EditorGUILayout.TextField("Output Folder", outputRoot);

            frameRate = EditorGUILayout.FloatField("Frame Rate", frameRate);
            normalHalfPrecision = EditorGUILayout.Toggle("Normal RGBAHalf (else RGBA32)", normalHalfPrecision);
            EditorGUILayout.Space();
        }

        void DrawScanArea()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan Project", GUILayout.Height(30)))
                ScanAllAssets();

            if (GUILayout.Button("Clear List", GUILayout.Height(30)))
            {
                entries.Clear();
                hasScanned = false;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            if (hasScanned)
            {
                int withClips = entries.FindAll(e => e.clips.Count > 0).Count;
                EditorGUILayout.LabelField($"Found {entries.Count} skinned model(s) ({withClips} with animations).");
            }
        }

        void DrawEntryList()
        {
            if (entries.Count == 0) return;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];

                var bg = new GUIStyle("box");
                if (e.clips.Count == 0 && !e.baked)
                    bg.normal.textColor = Color.yellow;

                EditorGUILayout.BeginVertical(bg);
                EditorGUILayout.BeginHorizontal();

                e.selected = EditorGUILayout.Toggle(e.selected, GUILayout.Width(20));

                e.expanded = EditorGUILayout.Foldout(e.expanded, e.label, true);

                EditorGUILayout.LabelField($"{e.vertexCount} verts", GUILayout.Width(70));

                if (e.baked)
                {
                    EditorGUILayout.LabelField("OK", GUILayout.Width(24));
                }
                else if (e.clips.Count == 0)
                {
                    var warnStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.yellow } };
                    EditorGUILayout.LabelField("no clips", warnStyle, GUILayout.Width(60));
                }
                else
                {
                    EditorGUILayout.LabelField($"{e.clips.Count} clip(s)", GUILayout.Width(60));
                }

                if (!e.baked && GUILayout.Button("Find Clips", GUILayout.Width(70)))
                    AutoMatchClips(e);

                EditorGUILayout.EndHorizontal();

                if (e.expanded)
                {
                    EditorGUILayout.Space();
                    DrawClipList(e, i);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
        }

        void DrawClipList(AssetEntry entry, int index)
        {
            for (int c = 0; c < entry.clips.Count; c++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  {entry.clips[c].name}  ({entry.clips[c].length:F2}s)", GUILayout.MaxWidth(400));
                if (!entry.baked && GUILayout.Button("x", GUILayout.Width(20)))
                {
                    entry.clips.RemoveAt(c);
                    c--;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!entry.baked)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("  Add clip:", GUILayout.Width(70));
                clipToAdd = EditorGUILayout.ObjectField(clipToAdd, typeof(AnimationClip), false, GUILayout.MaxWidth(300)) as AnimationClip;
                if (clipToAdd != null)
                {
                    entry.clips.Add(clipToAdd);
                    clipToAdd = null;
                }

                if (GUILayout.Button("From FBX...", GUILayout.Width(80)))
                    PickFbxClips(entry);
                EditorGUILayout.EndHorizontal();
            }

            if (entry.clips.Count == 0)
            {
                EditorGUILayout.HelpBox("No animation clips. Drag clips above or click 'Find Clips' / 'From FBX'.", MessageType.Warning);
            }
        }

        void PickFbxClips(AssetEntry entry)
        {
            string fbxPath = EditorUtility.OpenFilePanel("Pick Animation FBX", scanScope, "fbx");
            if (string.IsNullOrEmpty(fbxPath)) return;

            if (!fbxPath.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("Invalid", "FBX must be inside the project.", "OK");
                return;
            }

            string relPath = "Assets" + fbxPath.Substring(Application.dataPath.Length);
            var fromFbx = ExtractClipsFromFbx(relPath);
            if (fromFbx.Count == 0)
            {
                EditorUtility.DisplayDialog("No Clips", $"No animation clips found in {relPath}", "OK");
                return;
            }

            foreach (var clip in fromFbx)
            {
                if (!entry.clips.Contains(clip))
                    entry.clips.Add(clip);
            }
        }

        void DrawBakeButtons()
        {
            if (entries.Count == 0) return;

            int selectedCount = entries.FindAll(e => e.selected).Count;
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = selectedCount > 0;
            if (GUILayout.Button($"Bake Selected ({selectedCount})", GUILayout.Height(35)))
                BakeSelected();
            GUI.enabled = true;

            if (GUILayout.Button("Bake All", GUILayout.Height(35)))
            {
                foreach (var e in entries) e.selected = true;
                BakeSelected();
            }

            EditorGUILayout.EndHorizontal();
        }

        void ScanAllAssets()
        {
            entries.Clear();
            hasScanned = false;

            if (!AssetDatabase.IsValidFolder(scanScope))
            {
                EditorUtility.DisplayDialog("Invalid Folder", $"Folder '{scanScope}' does not exist.", "OK");
                return;
            }

            var seenPaths = new HashSet<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { scanScope }))
                seenPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { scanScope }))
                seenPaths.Add(AssetDatabase.GUIDToAssetPath(guid));

            foreach (string path in seenPaths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null || skin.sharedMesh == null) continue;

                var entry = new AssetEntry
                {
                    prefab = go,
                    assetPath = path,
                    label = path,
                    skin = skin,
                    sharedMesh = skin.sharedMesh,
                    vertexCount = skin.sharedMesh.vertexCount,
                    selected = true
                };

                var clips = CollectClips(go);
                if (clips.Count > 0)
                    entry.clips.AddRange(clips);

                AutoMatchClips(entry);

                entries.Add(entry);
            }

            hasScanned = true;
            int withClips = entries.FindAll(e => e.clips.Count > 0).Count;
            if (entries.Count == 0)
                Debug.Log("[VatBaker] No skinned models found in project.");
            else
                Debug.Log($"[VatBaker] Found {entries.Count} skinned model(s), {withClips} with animation clips.");
            Repaint();
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

        void AutoMatchClips(AssetEntry entry)
        {
            var seen = new HashSet<AnimationClip>(entry.clips);
            string prefix = ExtractPrefix(entry.assetPath);
            if (string.IsNullOrEmpty(prefix)) return;

            var allGuids = AssetDatabase.FindAssets("t:Model", new[] { scanScope });
            foreach (var guid in allGuids)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                if (fbxPath == entry.assetPath) continue;

                string fbxName = Path.GetFileNameWithoutExtension(fbxPath);
                if (!fbxName.StartsWith(prefix + "@")) continue;

                var clips = ExtractClipsFromFbx(fbxPath);
                foreach (var clip in clips)
                {
                    if (!seen.Contains(clip))
                    {
                        entry.clips.Add(clip);
                        seen.Add(clip);
                    }
                }
            }
        }

        static string ExtractPrefix(string assetPath)
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);

            int idx = name.IndexOf("@");
            if (idx > 0)
                return name.Substring(0, idx);

            idx = name.LastIndexOf("_Model");
            if (idx > 0)
                return name.Substring(0, idx);

            idx = name.LastIndexOf("_");
            if (idx > 0)
                return name.Substring(0, idx);

            return name;
        }

        static List<AnimationClip> ExtractClipsFromFbx(string fbxPath)
        {
            var clips = new List<AnimationClip>();
            foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
            {
                if (obj is AnimationClip clip && clip != null)
                    clips.Add(clip);
            }
            return clips;
        }

        void BakeSelected()
        {
            var toBake = entries.FindAll(e => e.selected && !e.baked);
            if (toBake.Count == 0) return;

            for (int i = 0; i < toBake.Count; i++)
            {
                if (toBake[i].clips.Count == 0)
                {
                    if (!EditorUtility.DisplayDialog("Missing Clips",
                        $"{toBake[i].label} has no animation clips.\n\nSkip this model?", "Skip", "Cancel"))
                        return;
                    toBake[i].selected = false;
                }
            }

            toBake = entries.FindAll(e => e.selected && !e.baked);
            if (toBake.Count == 0) return;

            EnsureOutputFolder();

            int total = toBake.Count;
            int bakedCount = 0;
            int errorCount = 0;

            for (int idx = 0; idx < total; idx++)
            {
                var entry = toBake[idx];
                string label = $"[{idx + 1}/{total}] {entry.label}";

                if (EditorUtility.DisplayCancelableProgressBar("Baking VATs", label, (float)idx / total))
                {
                    Debug.LogWarning("[VatBaker] Baking cancelled by user.");
                    break;
                }

                if (BakeSingle(entry))
                {
                    entry.baked = true;
                    bakedCount++;
                }
                else
                {
                    errorCount++;
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VatBaker] Done. {bakedCount} baked, {errorCount} failed.");
            Repaint();
        }

        bool BakeSingle(AssetEntry entry)
        {
            var sourcePath = entry.assetPath;
            var name = Path.GetFileNameWithoutExtension(sourcePath);

            var tempGO = Instantiate(entry.prefab);
            tempGO.name = name + "_BakingTemp";

            var animator = tempGO.GetComponentInChildren<Animator>();
            var skinnedMR = tempGO.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedMR == null)
            {
                Debug.LogError($"[VatBaker] No SkinnedMeshRenderer on {sourcePath}");
                DestroyImmediate(tempGO);
                return false;
            }

            var clips = entry.clips;
            var bakedMesh = new Mesh();
            Mesh bakedReferenceMesh = null;

            if (clips.Count == 0)
            {
                Debug.LogError($"[VatBaker] No clips selected for {sourcePath}");
                DestroyImmediate(tempGO);
                DestroyImmediate(bakedMesh);
                return false;
            }

            var firstClip = clips[0];
            firstClip.SampleAnimation(tempGO, 0f);
            if (animator != null) animator.Update(0f);
            skinnedMR.BakeMesh(bakedMesh);

            int vertexCount = bakedMesh.vertexCount;
            if (vertexCount <= 0)
            {
                Debug.LogError($"[VatBaker] Baked mesh has no vertices for {sourcePath}");
                DestroyImmediate(tempGO);
                DestroyImmediate(bakedMesh);
                return false;
            }

            bakedReferenceMesh = Instantiate(bakedMesh);
            bakedReferenceMesh.name = name + "_VAT_RenderMesh";

            int totalFrames = 0;
            for (int c = 0; c < clips.Count; c++)
            {
                int frames = Mathf.RoundToInt(clips[c].length * frameRate);
                if (frames < 1) frames = 1;
                totalFrames += frames;
            }

            var posFormat = TextureFormat.RGBAHalf;
            var normFormat = normalHalfPrecision ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;

            var posTex = new Texture2D(vertexCount, totalFrames, posFormat, false)
            {
                name = name + "_PositionVAT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var normTex = new Texture2D(vertexCount, totalFrames, normFormat, false)
            {
                name = name + "_NormalVAT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var clipInfos = new AnimationClipInfo[clips.Count];
            int currentRow = 0;

            for (int c = 0; c < clips.Count; c++)
            {
                var clip = clips[c];
                int frames = Mathf.RoundToInt(clip.length * frameRate);
                if (frames < 1) frames = 1;

                clipInfos[c] = new AnimationClipInfo
                {
                    name = clip.name,
                    startFrame = currentRow,
                    frameCount = frames,
                    frameRate = frameRate
                };

                for (int f = 0; f < frames; f++)
                {
                    float normalizedTime = frames > 1 ? f / (float)(frames - 1) : 0f;

                    clip.SampleAnimation(tempGO, normalizedTime * clip.length);
                    if (animator != null) animator.Update(0f);

                    skinnedMR.BakeMesh(bakedMesh);

                    var verts = bakedMesh.vertices;
                    var norms = bakedMesh.normals;

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

                    posTex.SetPixels(0, currentRow, vertexCount, 1, posColors);
                    normTex.SetPixels(0, currentRow, vertexCount, 1, normColors);
                    currentRow++;
                }
            }

            posTex.Apply(false, true);
            normTex.Apply(false, true);

            DestroyImmediate(tempGO);
            if (bakedMesh != null) DestroyImmediate(bakedMesh);

            string assetPath = Path.Combine(outputRoot, name + "_VAT.asset");
            var existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(assetPath);

            var vatData = ScriptableObject.CreateInstance<VatAnimationData>();
            vatData.name = name + "_VAT";
            vatData.positionTexture = posTex;
            vatData.normalTexture = normTex;
            vatData.originalMesh = bakedReferenceMesh;
            vatData.clips = clipInfos;
            vatData.vertexCount = vertexCount;
            vatData.totalFrames = totalFrames;

            var shader = Shader.Find("Custom/VatInstancing");
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
                mat.SetColor("_Color", Color.white);
            }
            else
            {
                Debug.LogError("[VatBaker] Custom/VatInstancing shader not found!");
                mat = new Material(Shader.Find("Standard"));
            }

            vatData.animationMaterial = mat;

            AssetDatabase.CreateAsset(vatData, assetPath);
            AssetDatabase.AddObjectToAsset(posTex, vatData);
            AssetDatabase.AddObjectToAsset(normTex, vatData);
            AssetDatabase.AddObjectToAsset(mat, vatData);
            AssetDatabase.AddObjectToAsset(bakedReferenceMesh, vatData);

            return true;
        }

        void EnsureOutputFolder()
        {
            if (AssetDatabase.IsValidFolder(outputRoot)) return;

            var parent = Path.GetDirectoryName(outputRoot);
            var folder = Path.GetFileName(outputRoot);
            if (parent != null && AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder(parent, folder);
            else
                outputRoot = "Assets";
        }
    }
}
