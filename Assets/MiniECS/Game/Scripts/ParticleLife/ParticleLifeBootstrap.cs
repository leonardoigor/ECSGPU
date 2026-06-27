using UnityEngine;
using MiniECS;
using System.Collections.Generic;

namespace Game.ParticleLife
{
    [System.Serializable]
    public class SpeciesDef
    {
        public string name = "Nova";
        public Color color = Color.white;
    }

    [System.Serializable]
    public class RuleDef
    {
        public int speciesA;
        public int speciesB;
        public enum ActionType { Atracao, Repulsao, Neutro }
        public ActionType action = ActionType.Atracao;
        public float strength = 1f;

        public string ActionLabel()
        {
            switch (action)
            {
                case ActionType.Atracao: return "atração";
                case ActionType.Repulsao: return "repulsão";
                default: return "neutro";
            }
        }
    }

    public class ParticleLifeBootstrap : MonoBehaviour
    {
        [Header("Species")]
        public List<SpeciesDef> speciesDefs = new();

        [Header("Rules")]
        public List<RuleDef> ruleDefs = new();

        [Header("Spawn")]
        public int maxParticles = 3000;
        [Range(1, 50)] public int spawnBatch = 5;
        [Range(0.05f, 2f)] public float spawnInterval = 0.2f;
        public float spawnRadius = 3f;
        public Vector3 spawnCenter = new Vector3(15, 0, 0);
        public float spawnSpeed = 3f;

        [Header("Physics")]
        public float interactionRadius = 4f;
        public float maxSpeed = 6f;
        public float damping = 0.97f;
        public float noiseStrength = 0.6f;

        [Header("Rendering")]
        [Range(0.1f, 1f)] public float sphereRadius = 0.25f;

        [Header("Camera")]
        public float orbitRadius = 35f;
        public float orbitSpeed = 0.08f;
        public float orbitHeight = 8f;

        public bool showSpawner = true;

        Camera cam;
        Transform camPivot;
        Material defaultMat;
        MaterialPropertyBlock mpb;
        SystemScheduler scheduler;
        float orbitAngle;
        LineRenderer spawnerWire;

        // Runtime state
        bool running;
        bool showUI = true;
        int uiTab;
        string[] tabNames = new string[] { "ESPÉCIES", "REGRAS", "CONFIG" };
        Vector2 scrollPos;
        int editingColorIdx = -1;
        string[] actionLabels = new string[] { "atração", "repulsão", "neutro" };
        bool showPresets;

        // Rendering state
        readonly Dictionary<int, GameObject> activeSpheres = new();
        readonly Queue<GameObject> spherePool = new();
        readonly List<int> toRemove = new();
        readonly HashSet<int> visibleSet = new();

        // ─── Unity Lifecycle ────────────────────────────────────────

        void Awake()
        {
            cam = Camera.main;
            if (cam == null) { enabled = false; return; }
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { enabled = false; return; }

            defaultMat = new Material(shader);
            mpb = new MaterialPropertyBlock();

            SetupDefaults();
            SetupSpawnerVisual();
            SetupCamera();

            if (SystemScheduler.Main == null)
            {
                SystemRegistry.RegisterAll();
                scheduler = new SystemScheduler(false, true);
            }
        }

        void SetupDefaults()
        {
            speciesDefs.Clear();
            speciesDefs.Add(new SpeciesDef { name = "Verde", color = new Color(0f, 1f, 0.3f) });
            speciesDefs.Add(new SpeciesDef { name = "Roxo", color = new Color(0.8f, 0.2f, 1f) });

            ruleDefs.Clear();
            ruleDefs.Add(new RuleDef { speciesA = 0, speciesB = 0, action = RuleDef.ActionType.Atracao, strength = 1.2f });
            ruleDefs.Add(new RuleDef { speciesA = 1, speciesB = 1, action = RuleDef.ActionType.Atracao, strength = 1.0f });
            ruleDefs.Add(new RuleDef { speciesA = 0, speciesB = 1, action = RuleDef.ActionType.Repulsao, strength = 5f });
            ruleDefs.Add(new RuleDef { speciesA = 1, speciesB = 0, action = RuleDef.ActionType.Repulsao, strength = 5f });

            ParticleLifeSimSystem.SpeciesCount = speciesDefs.Count;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) showUI = !showUI;

            if (scheduler != null) scheduler.Update(Time.deltaTime);

            if (SystemScheduler.Main != null)
            {
                orbitAngle += orbitSpeed * Time.deltaTime;
                UpdateCameraPosition();
                UpdateRendering();
                UpdateSpawnerWire();
            }
        }

        void OnDestroy() => CleanupSpheres();

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.2f, 1f, 0.3f);
            Gizmos.DrawWireSphere(spawnCenter, spawnRadius);
        }

        // ─── Spawner Visual ─────────────────────────────────────────

        void SetupSpawnerVisual()
        {
            var go = new GameObject("SpawnerVisual");
            go.transform.SetParent(transform);
            spawnerWire = go.AddComponent<LineRenderer>();
            spawnerWire.loop = true;
            spawnerWire.startWidth = 0.05f;
            spawnerWire.endWidth = 0.05f;
            spawnerWire.material = new Material(Shader.Find("Sprites/Default"));
            spawnerWire.startColor = new Color(0.8f, 0.2f, 1f, 0.5f);
            spawnerWire.endColor = new Color(0.8f, 0.2f, 1f, 0.5f);
            spawnerWire.positionCount = 0;
            spawnerWire.enabled = showSpawner;
            RebuildSpawnerWire();
        }

        void RebuildSpawnerWire()
        {
            if (spawnerWire == null || !showSpawner) return;
            int segs = 24, rings = 4;
            var pts = new List<Vector3>();
            for (int r = 0; r < rings; r++)
            {
                float theta = r * Mathf.PI / rings;
                for (int s = 0; s <= segs; s++)
                {
                    float phi = s * 2f * Mathf.PI / segs;
                    pts.Add(spawnCenter + new Vector3(
                        spawnRadius * Mathf.Sin(theta) * Mathf.Cos(phi),
                        spawnRadius * Mathf.Cos(theta),
                        spawnRadius * Mathf.Sin(theta) * Mathf.Sin(phi)
                    ));
                }
            }
            spawnerWire.positionCount = pts.Count;
            spawnerWire.SetPositions(pts.ToArray());
        }

        void UpdateSpawnerWire()
        {
            if (spawnerWire != null && spawnerWire.enabled)
                spawnerWire.transform.position = Vector3.zero;
        }

        // ─── Camera ─────────────────────────────────────────────────

        void SetupCamera()
        {
            camPivot = new GameObject("CamPivot").transform;
            camPivot.position = Vector3.zero;
            cam.transform.SetParent(camPivot);
            orbitAngle = 0f;
            UpdateCameraPosition();
        }

        void UpdateCameraPosition()
        {
            if (camPivot == null || cam == null) return;
            cam.transform.localPosition = new Vector3(
                Mathf.Sin(orbitAngle) * orbitRadius,
                orbitHeight,
                Mathf.Cos(orbitAngle) * orbitRadius
            );
            cam.transform.LookAt(Vector3.zero);
        }

        // ─── Simulation Control ─────────────────────────────────────

        void CompileRules()
        {
            int max = ParticleLifeSimSystem.MaxSpecies;
            for (int a = 0; a < max; a++)
                for (int b = 0; b < max; b++)
                    ParticleLifeSimSystem.SetRule(a, b, 0f);

            foreach (var r in ruleDefs)
            {
                if (r.speciesA < 0 || r.speciesA >= max) continue;
                if (r.speciesB < 0 || r.speciesB >= max) continue;
                float val = r.strength;
                if (r.action == RuleDef.ActionType.Repulsao) val = -val;
                else if (r.action == RuleDef.ActionType.Neutro) val = 0f;
                float existing = 0;
                ParticleLifeSimSystem.GetRule(r.speciesA, r.speciesB, out existing);
                ParticleLifeSimSystem.SetRule(r.speciesA, r.speciesB, existing + val);
            }
        }

        void PushConfig()
        {
            ParticleLifeSimSystem.SpeciesCount = speciesDefs.Count;
            ParticleLifeSimSystem.MaxPopulation = maxParticles;
            ParticleLifeSimSystem.InteractionRadius = interactionRadius;
            ParticleLifeSimSystem.MaxSpeed = maxSpeed;
            ParticleLifeSimSystem.Damping = damping;
            ParticleLifeSimSystem.NoiseStrength = noiseStrength;
            ParticleLifeSimSystem.SpawnInterval = spawnInterval;
            ParticleLifeSimSystem.SpawnCount = spawnBatch;
            ParticleLifeSimSystem.SpawnRadius = spawnRadius;
            ParticleLifeSimSystem.SpawnCenter = spawnCenter;
            ParticleLifeSimSystem.SpawnSpeed = spawnSpeed;
        }

        public void StartSimulation()
        {
            if (running) return;
            CompileRules();
            PushConfig();
            ParticleLifeSimSystem.ParticleCount = 0;
            ParticleLifeSimSystem.SpawnEnabled = true;
            running = true;
            Debug.Log("[ParticleLife] Simulação iniciada!");
        }

        public void StopSimulation()
        {
            ParticleLifeSimSystem.SpawnEnabled = false;
            running = false;
            Debug.Log("[ParticleLife] Simulação parada.");
        }

        public void ResetSimulation()
        {
            StopSimulation();
            CleanupSpheres();
            ParticleLifeSimSystem.Reset();
            // Rebuild spawner wire (position may have changed)
            RebuildSpawnerWire();
            Debug.Log("[ParticleLife] Simulação resetada.");
        }

        public void ApplyRulesNow()
        {
            if (!running) return;
            CompileRules();
            PushConfig();
            Debug.Log("[ParticleLife] Regras reaplicadas!");
        }

        // ─── Rendering ──────────────────────────────────────────────

        GameObject GetOrCreateSphere()
        {
            if (spherePool.Count > 0)
            {
                var s = spherePool.Dequeue();
                s.SetActive(true);
                return s;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * sphereRadius * 2f;
            go.GetComponent<MeshRenderer>().sharedMaterial = defaultMat;
            return go;
        }

        void ReturnSphere(GameObject go)
        {
            go.SetActive(false);
            spherePool.Enqueue(go);
        }

        void CleanupSpheres()
        {
            foreach (var kvp in activeSpheres)
                if (kvp.Value != null) Destroy(kvp.Value);
            activeSpheres.Clear();
            while (spherePool.Count > 0)
            {
                var go = spherePool.Dequeue();
                if (go != null) Destroy(go);
            }
        }

        void UpdateRendering()
        {
            var camPos = cam.transform.position;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            float sqrViewDist = orbitRadius * orbitRadius * 2f;
            var bounds = new Bounds(Vector3.zero, Vector3.one * sphereRadius * 2f);
            visibleSet.Clear();
            int count = ParticleLifeSimSystem.ParticleCount;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = ParticleLifeSimSystem.Positions[i];
                if ((camPos - pos).sqrMagnitude > sqrViewDist) continue;
                bounds.center = pos;
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                visibleSet.Add(i);

                if (!activeSpheres.TryGetValue(i, out var go))
                {
                    go = GetOrCreateSphere();
                    go.transform.position = pos;
                    go.transform.localScale = Vector3.one * sphereRadius * 2f;
                    activeSpheres[i] = go;
                }
                else go.transform.position = pos;

                int sp = ParticleLifeSimSystem.Species[i];
                Color col = sp >= 0 && sp < speciesDefs.Count ? speciesDefs[sp].color : Color.gray;
                mpb.SetColor("_Color", col);
                go.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
            }

            toRemove.Clear();
            foreach (var kvp in activeSpheres)
                if (!visibleSet.Contains(kvp.Key)) { ReturnSphere(kvp.Value); toRemove.Add(kvp.Key); }
            foreach (var i in toRemove) activeSpheres.Remove(i);
        }

        // ─── Main UI ────────────────────────────────────────────────

        void OnGUI()
        {
            if (cam == null || !showUI) return;

            float px = Screen.width - 420;
            float py = 10;
            float pw = 400;

            GUI.Box(new Rect(px, py, pw, 28), "VIDA ARTIFICIAL 3D  [F1]");
            py += 30;

            float tabW = pw / 4f;
            for (int t = 0; t < 3; t++)
                if (GUI.Toggle(new Rect(px + t * tabW, py, tabW, 22), uiTab == t, tabNames[t], GUI.skin.button))
                { uiTab = t; editingColorIdx = -1; }
            if (GUI.Toggle(new Rect(px + 3 * tabW, py, tabW, 22), showPresets, "PRESETS", GUI.skin.button))
                showPresets = !showPresets;
            py += 24;

            float availH = Mathf.Min(Screen.height - py - 50, 460);
            scrollPos = GUI.BeginScrollView(new Rect(px, py, pw, availH), scrollPos,
                new Rect(0, 0, pw - 20, TabContentHeight()));

            DrawTabContent(pw - 20);

            GUI.EndScrollView();
            py += availH + 4;

            DrawBottomBar(px, py, pw);
            DrawStatsOverlay();
            DrawColorPickerPopup();
        }

        float TabContentHeight()
        {
            if (uiTab == 0) return 40 + Mathf.Max(speciesDefs.Count, 2) * 30 + 30;
            if (uiTab == 1) return 40 + Mathf.Max(ruleDefs.Count, 1) * 44 + 40 + (showPresets ? 320 : 0);
            return 320;
        }

        void DrawTabContent(float w)
        {
            if (uiTab == 0) DrawSpeciesTab(w);
            else if (uiTab == 1) DrawRulesTab(w);
            else DrawConfigTab(w);
        }

        // ─── Species Tab ────────────────────────────────────────────

        void DrawSpeciesTab(float w)
        {
            GUI.Label(new Rect(5, 5, w, 18), "Espécies:");
            float y = 23;

            for (int i = 0; i < speciesDefs.Count; i++)
            {
                var sp = speciesDefs[i];

                // Color swatch
                Rect swatch = new Rect(5, y, 22, 22);
                GUI.color = sp.color;
                if (GUI.Button(swatch, "")) editingColorIdx = editingColorIdx == i ? -1 : i;
                GUI.color = Color.white;
                GUI.Box(swatch, "");

                // Name
                sp.name = GUI.TextField(new Rect(32, y, w - 80, 22), sp.name);

                // Remove
                if (speciesDefs.Count > 2)
                    if (GUI.Button(new Rect(w - 44, y, 40, 22), "X"))
                    { speciesDefs.RemoveAt(i); editingColorIdx = -1; return; }

                y += 26;
            }

            if (speciesDefs.Count < 8)
                if (GUI.Button(new Rect(5, y, 130, 24), "+ Adicionar"))
                {
                    speciesDefs.Add(new SpeciesDef
                    {
                        name = "Nova " + (speciesDefs.Count + 1),
                        color = new Color(
                            Random.value * 0.8f + 0.2f,
                            Random.value * 0.8f + 0.2f,
                            Random.value * 0.8f + 0.2f
                        )
                    });
                }
        }

        // ─── Color Picker Popup ─────────────────────────────────────

        void DrawColorPickerPopup()
        {
            if (editingColorIdx < 0 || editingColorIdx >= speciesDefs.Count) return;
            var sp = speciesDefs[editingColorIdx];

            float px = Screen.width - 420 + 30;
            float py = 80 + editingColorIdx * 26;
            if (py > Screen.height - 140) py = Screen.height - 140;

            GUI.Box(new Rect(px, py, 240, 110), "Cor: " + sp.name);
            GUI.Label(new Rect(px + 10, py + 20, 20, 20), "R");
            sp.color.r = GUI.HorizontalSlider(new Rect(px + 35, py + 22, 160, 20), sp.color.r, 0f, 1f);
            GUI.Label(new Rect(px + 200, py + 20, 30, 20), sp.color.r.ToString("F2"));

            GUI.Label(new Rect(px + 10, py + 42, 20, 20), "G");
            sp.color.g = GUI.HorizontalSlider(new Rect(px + 35, py + 44, 160, 20), sp.color.g, 0f, 1f);
            GUI.Label(new Rect(px + 200, py + 42, 30, 20), sp.color.g.ToString("F2"));

            GUI.Label(new Rect(px + 10, py + 64, 20, 20), "B");
            sp.color.b = GUI.HorizontalSlider(new Rect(px + 35, py + 66, 160, 20), sp.color.b, 0f, 1f);
            GUI.Label(new Rect(px + 200, py + 64, 30, 20), sp.color.b.ToString("F2"));

            // Preview
            GUI.color = sp.color;
            GUI.Box(new Rect(px + 10, py + 88, 150, 18), "");
            GUI.color = Color.white;

            if (GUI.Button(new Rect(px + 190, py + 86, 40, 20), "OK"))
                editingColorIdx = -1;
        }

        // ─── Rules Tab ──────────────────────────────────────────────

        void DrawRulesTab(float w)
        {
            GUI.Label(new Rect(5, 5, w, 18), "Regras (A → ação → B):");
            float y = 23;

            for (int i = 0; i < ruleDefs.Count; i++)
            {
                var r = ruleDefs[i];
                int n = speciesDefs.Count;

                // Species A dropdown
                string[] spNames = speciesDefs.ConvertAll(s => s.name).ToArray();
                int newA = Mathf.RoundToInt(GUI.Toolbar(new Rect(5, y, 100, 22), r.speciesA, spNames));
                r.speciesA = newA;

                // Action dropdown
                int actIdx = (int)r.action;
                int newAct = GUI.Toolbar(new Rect(110, y, 80, 22), actIdx, actionLabels);
                r.action = (RuleDef.ActionType)newAct;

                // Species B dropdown
                int newB = Mathf.RoundToInt(GUI.Toolbar(new Rect(195, y, 100, 22), r.speciesB, spNames));
                r.speciesB = newB;

                // Strength slider
                float labelX = 300;
                GUI.Label(new Rect(labelX, y, 20, 22), "F:");
                r.strength = GUI.HorizontalSlider(new Rect(labelX + 18, y + 4, w - labelX - 64, 20), r.strength, 0.1f, 10f);

                // Remove
                if (GUI.Button(new Rect(w - 22, y, 20, 22), "X"))
                { ruleDefs.RemoveAt(i); return; }

                y += 24;
            }

            if (GUI.Button(new Rect(5, y, 120, 24), "+ Nova Regra"))
                ruleDefs.Add(new RuleDef
                {
                    speciesA = 0,
                    speciesB = 0,
                    action = RuleDef.ActionType.Atracao,
                    strength = 1f
                });

            y += 30;

            // ─── Presets ────────────────────────────────────────────
            if (!showPresets) return;
            GUI.Label(new Rect(5, y, w, 18), "PRESETS:", GUI.skin.label);
            y += 20;

            string[] presets = new string[] {
                "Verde vs Roxo (2 spp)", "Predador-Presa (3 spp)",
                "Segregação (3 spp)", "Caos Total (4 spp)",
                "Ecosystema (4 spp)", "Atração Universal"
            };
            for (int p = 0; p < presets.Length; p++)
            {
                if (GUI.Button(new Rect(10, y, w - 20, 22), presets[p]))
                    LoadPreset(p);
                y += 24;
            }
        }

        void LoadPreset(int idx)
        {
            switch (idx)
            {
                case 0: LoadPreset_VerdeRoxo(); break;
                case 1: LoadPreset_PredadorPresa(); break;
                case 2: LoadPreset_Segregacao(); break;
                case 3: LoadPreset_Caos(); break;
                case 4: LoadPreset_Ecosystema(); break;
                case 5: LoadPreset_AtracaoUniversal(); break;
            }
        }

        void SetSpecies(int i, string name, Color c)
        {
            while (speciesDefs.Count <= i)
                speciesDefs.Add(new SpeciesDef());
            speciesDefs[i].name = name;
            speciesDefs[i].color = c;
        }

        void AddRule(int a, int b, RuleDef.ActionType act, float str)
        {
            ruleDefs.Add(new RuleDef { speciesA = a, speciesB = b, action = act, strength = str });
        }

        void ClearRules() => ruleDefs.Clear();

        void LoadPreset_VerdeRoxo()
        {
            SetSpecies(0, "Verde", new Color(0f, 1f, 0.3f));
            SetSpecies(1, "Roxo", new Color(0.8f, 0.2f, 1f));
            while (speciesDefs.Count > 2) speciesDefs.RemoveAt(speciesDefs.Count - 1);
            ClearRules();
            AddRule(0, 0, RuleDef.ActionType.Atracao, 1.2f);
            AddRule(1, 1, RuleDef.ActionType.Atracao, 1f);
            AddRule(0, 1, RuleDef.ActionType.Repulsao, 5f);
            AddRule(1, 0, RuleDef.ActionType.Repulsao, 5f);
        }

        void LoadPreset_PredadorPresa()
        {
            SetSpecies(0, "Presa", new Color(0f, 1f, 0.3f));
            SetSpecies(1, "Predador", new Color(1f, 0.2f, 0.2f));
            SetSpecies(2, "Neutro", new Color(0.2f, 0.5f, 1f));
            while (speciesDefs.Count > 3) speciesDefs.RemoveAt(speciesDefs.Count - 1);
            ClearRules();
            AddRule(0, 0, RuleDef.ActionType.Atracao, 1f);
            AddRule(1, 1, RuleDef.ActionType.Atracao, 1f);
            AddRule(2, 2, RuleDef.ActionType.Atracao, 0.5f);
            AddRule(0, 1, RuleDef.ActionType.Repulsao, 4f);  // Presa ← Predador
            AddRule(1, 0, RuleDef.ActionType.Atracao, 0.5f); // Predador ← Presa
            AddRule(0, 2, RuleDef.ActionType.Atracao, 0.3f);
            AddRule(2, 0, RuleDef.ActionType.Atracao, 0.3f);
            AddRule(1, 2, RuleDef.ActionType.Repulsao, 1f);
            AddRule(2, 1, RuleDef.ActionType.Repulsao, 1f);
        }

        void LoadPreset_Segregacao()
        {
            SetSpecies(0, "Vermelho", new Color(1f, 0.2f, 0.2f));
            SetSpecies(1, "Azul", new Color(0.2f, 0.5f, 1f));
            SetSpecies(2, "Amarelo", new Color(1f, 0.9f, 0.1f));
            while (speciesDefs.Count > 3) speciesDefs.RemoveAt(speciesDefs.Count - 1);
            ClearRules();
            for (int i = 0; i < 3; i++) AddRule(i, i, RuleDef.ActionType.Atracao, 2f);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (i != j) AddRule(i, j, RuleDef.ActionType.Repulsao, 3f);
        }

        void LoadPreset_Caos()
        {
            SetSpecies(0, "Verde", new Color(0f, 1f, 0.3f));
            SetSpecies(1, "Roxo", new Color(0.8f, 0.2f, 1f));
            SetSpecies(2, "Vermelho", new Color(1f, 0.2f, 0.2f));
            SetSpecies(3, "Azul", new Color(0.2f, 0.5f, 1f));
            while (speciesDefs.Count > 4) speciesDefs.RemoveAt(speciesDefs.Count - 1);
            ClearRules();
            for (int i = 0; i < 4; i++) AddRule(i, i, RuleDef.ActionType.Atracao, 0.3f);
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    if (i != j) AddRule(i, j, RuleDef.ActionType.Repulsao, 2.5f);
        }

        void LoadPreset_Ecosystema()
        {
            SetSpecies(0, "Planta", new Color(0f, 1f, 0.3f));
            SetSpecies(1, "Herbívoro", new Color(1f, 0.9f, 0.1f));
            SetSpecies(2, "Predador", new Color(1f, 0.2f, 0.2f));
            SetSpecies(3, "Decompositor", new Color(0.8f, 0.2f, 1f));
            while (speciesDefs.Count > 4) speciesDefs.RemoveAt(speciesDefs.Count - 1);
            ClearRules();
            AddRule(0, 0, RuleDef.ActionType.Atracao, 1f);
            AddRule(1, 1, RuleDef.ActionType.Atracao, 1f);
            AddRule(2, 2, RuleDef.ActionType.Atracao, 1f);
            AddRule(3, 3, RuleDef.ActionType.Atracao, 1f);
            AddRule(0, 1, RuleDef.ActionType.Repulsao, 3f);  // Planta ← Herbívoro
            AddRule(1, 0, RuleDef.ActionType.Atracao, 1.5f); // Herbívoro ← Planta
            AddRule(1, 2, RuleDef.ActionType.Repulsao, 4f);  // Herbívoro ← Predador
            AddRule(2, 1, RuleDef.ActionType.Atracao, 1.5f); // Predador ← Herbívoro
            AddRule(2, 3, RuleDef.ActionType.Repulsao, 2f);  // Predador ← Decompositor
            AddRule(3, 2, RuleDef.ActionType.Atracao, 1f);   // Decompositor ← Predador
            AddRule(3, 0, RuleDef.ActionType.Atracao, 1f);   // Decompositor ← Planta
            AddRule(0, 3, RuleDef.ActionType.Repulsao, 1f);  // Planta ← Decompositor
        }

        void LoadPreset_AtracaoUniversal()
        {
            SetSpecies(0, "Verde", new Color(0f, 1f, 0.3f));
            SetSpecies(1, "Roxo", new Color(0.8f, 0.2f, 1f));
            while (speciesDefs.Count > 2) speciesDefs.RemoveAt(speciesDefs.Count - 1);
            ClearRules();
            AddRule(0, 0, RuleDef.ActionType.Atracao, 1.5f);
            AddRule(1, 1, RuleDef.ActionType.Atracao, 1.5f);
            AddRule(0, 1, RuleDef.ActionType.Atracao, 0.2f);
            AddRule(1, 0, RuleDef.ActionType.Atracao, 0.2f);
        }

        // ─── Config Tab ─────────────────────────────────────────────

        void DrawConfigTab(float w)
        {
            float y = 5;
            GUI.Label(new Rect(5, y, w, 18), "Spawn:", GUI.skin.label); y += 20;

            DrawSliderInt(w, ref y, "Máx partículas", ref maxParticles, 100, 10000);
            DrawSliderInt(w, ref y, "Lote por spawn", ref spawnBatch, 1, 50);
            DrawSlider(w, ref y, "Intervalo (s)", ref spawnInterval, 0.05f, 2f);
            DrawSlider(w, ref y, "Raio spawn", ref spawnRadius, 0.5f, 10f);
            DrawSlider(w, ref y, "Velocidade spawn", ref spawnSpeed, 0.5f, 10f);

            y += 10;
            GUI.Label(new Rect(5, y, w, 18), "Física:", GUI.skin.label); y += 20;

            DrawSlider(w, ref y, "Raio interação", ref interactionRadius, 1f, 10f);
            DrawSlider(w, ref y, "Vel. máxima", ref maxSpeed, 1f, 20f);
            DrawSlider(w, ref y, "Amortecimento", ref damping, 0.8f, 1f);
            DrawSlider(w, ref y, "Ruído", ref noiseStrength, 0f, 2f);
        }

        void DrawSlider(float w, ref float y, string label, ref float val, float min, float max)
        {
            GUI.Label(new Rect(10, y, 100, 20), label);
            val = GUI.HorizontalSlider(new Rect(115, y + 2, w - 140, 20), val, min, max);
            GUI.Label(new Rect(w - 20, y, 30, 20), val.ToString("F2"));
            y += 22;
        }

        void DrawSliderInt(float w, ref float y, string label, ref int val, int min, int max)
        {
            GUI.Label(new Rect(10, y, 100, 20), label);
            float f = val;
            f = GUI.HorizontalSlider(new Rect(115, y + 2, w - 140, 20), f, min, max);
            val = Mathf.RoundToInt(f);
            GUI.Label(new Rect(w - 20, y, 30, 20), val.ToString());
            y += 22;
        }

        // ─── Bottom Bar ─────────────────────────────────────────────

        void DrawBottomBar(float px, float py, float pw)
        {
            GUI.Box(new Rect(px, py, pw, 38), "");

            if (!running)
            {
                if (GUI.Button(new Rect(px + 10, py + 4, 100, 30), "INICIAR"))
                    StartSimulation();
            }
            else
            {
                if (GUI.Button(new Rect(px + 10, py + 4, 100, 30), "PARAR"))
                    StopSimulation();
            }

            if (GUI.Button(new Rect(px + 120, py + 4, 100, 30), "RESET"))
                ResetSimulation();

            if (running && GUI.Button(new Rect(px + 230, py + 4, 100, 30), "BORA!"))
                ApplyRulesNow();

            if (GUI.Button(new Rect(px + 340, py + 4, 50, 30), running ? "ON" : "OFF"))
            {
                if (running) StopSimulation();
                else StartSimulation();
            }
        }

        // ─── Stats Overlay ──────────────────────────────────────────

        void DrawStatsOverlay()
        {
            int total = ParticleLifeSimSystem.ParticleCount;
            int visible = activeSpheres.Count;
            int pooled = spherePool.Count;
            float dt = Time.deltaTime;
            string fps = dt > 0.0001f ? (1f / dt).ToString("F0") : "...";

            float sx = 10;
            float sy = 10;
            float sw = 220;
            float sh = 28 + (running ? 48 : 0) + speciesDefs.Count * 18;

            GUI.Box(new Rect(sx, sy, sw, sh), running ? "EM EXECUÇÃO" : "CONFIGURANDO");
            sy += 20;
            GUI.Label(new Rect(sx + 10, sy, sw - 20, 18),
                $"FPS: {fps}  Partículas: {total}");
            sy += 18;
            GUI.Label(new Rect(sx + 10, sy, sw - 20, 18),
                $"Visíveis: {visible}  Pool: {pooled}");
            sy += 18;

            if (running)
            {
                sy += 4;
                ParticleLifeSimSystem.RecalcPopulation();
                for (int s = 0; s < speciesDefs.Count; s++)
                {
                    GUI.Box(new Rect(sx + 10, sy, 14, 14), "");
                    GUI.color = speciesDefs[s].color;
                    GUI.Box(new Rect(sx + 10, sy, 14, 14), "");
                    GUI.color = Color.white;
                    GUI.Label(new Rect(sx + 28, sy, sw - 38, 14),
                        $"{speciesDefs[s].name}: {ParticleLifeSimSystem.SpeciesPop[s]}");
                    sy += 16;
                }
            }
        }
    }
}
