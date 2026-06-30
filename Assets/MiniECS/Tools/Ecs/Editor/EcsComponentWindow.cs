using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using MiniECS;

namespace MiniECS.Tools.Editor
{
    public class EcsComponentWindow : EditorWindow
    {
        [MenuItem("Tools/MiniECS/ECS Entity List")]
        public static void Open() => GetWindow<EcsComponentWindow>("ECS Entities");

        enum ViewMode { Entities, Components }

        ViewMode viewMode = ViewMode.Entities;

        // ——— Entity View State ———
        Vector2 entityScroll;
        Vector2 detailScroll;
        int selectedId = -1;
        int hoverId = -1;
        string searchText = "";
        int filterCompIndex;
        string[] filterCompOptions;
        string[] archetypeFilterOptions;
        int archetypeFilterIndex;

        // ——— Cached data ———
        List<EntityCache> entities = new();
        List<EntityCache> filtered = new();
        Dictionary<Type, ComponentMeta> typeMeta = new();
        List<Type> allCompTypes = new();
        List<ArchetypeInfo> archetypes = new();
        bool dirty = true;
        int lastEntityCount = -1;
        double nextAutoRebuildTime;

        // ——— Layout constants ———
        const float ROW_HEIGHT = 22f;
        const float TOOLBAR_H = 20f;
        const float DETAIL_H = 150f;

        // Icons for common component types (mapped by name substring)
        static readonly Dictionary<string, string> CompIconMap = new()
        {
            { "Position", "P" },
            { "Velocity", "V" },
            { "Transform", "X" },
            { "Animator", "A" },
            { "Input", "I" },
            { "Tag", "T" },
            { "Data", "D" },
            { "Health", "H" },
            { "Damage", "Dmg" },
        };

        // ——— Entity cache struct ———
        struct EntityCache
        {
            public int Id;
            public string DebugName;
            public int ArchetypeHash;
            public string ArchetypeLabel;
            public List<Type> Components;
            public bool IsModified;
        }

        struct ArchetypeInfo
        {
            public int Hash;
            public string Label;
            public List<Type> ComponentTypes;
            public int Count;
        }

        class ComponentMeta
        {
            public string Name;
            public FieldInfo[] Fields;
            public string Icon;
            public bool IsCached;
        }

        // ═══════════════════════════════════════════
        // WINDOW LIFECYCLE
        // ═══════════════════════════════════════════

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying) return;

            // Repaint every editor frame so component values stay live
            Repaint();

            // Periodically rebuild cache to pick up new/deleted entities
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextAutoRebuildTime)
            {
                nextAutoRebuildTime = now + 2.0;
                dirty = true;
            }
        }

        // ═══════════════════════════════════════════
        // MAIN GUI
        // ═══════════════════════════════════════════

        void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to view ECS data.", MessageType.Info);
                return;
            }

            DrawMainToolbar();
            if (dirty) RebuildCache();

            if (viewMode == ViewMode.Entities)
                DrawEntityView();
            else
                DrawComponentsView();
        }

        void DrawMainToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Toggle(viewMode == ViewMode.Entities, "Entities",
                    EditorStyles.toolbarButton, GUILayout.Width(70)) != (viewMode == ViewMode.Entities))
                { viewMode = ViewMode.Entities; dirty = true; Repaint(); }

                if (GUILayout.Toggle(viewMode == ViewMode.Components, "Components",
                    EditorStyles.toolbarButton, GUILayout.Width(90)) != (viewMode == ViewMode.Components))
                { viewMode = ViewMode.Components; dirty = true; Repaint(); }

                GUILayout.Space(10);

                if (viewMode == ViewMode.Entities)
                    DrawEntityToolbar();
                else
                    DrawComponentToolbar();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(55)))
                    RebuildCache();

                EditorGUILayout.LabelField($"E: {lastEntityCount}", EditorStyles.miniLabel, GUILayout.Width(60));
            }
        }

        // ═══════════════════════════════════════════
        // ENTITY VIEW
        // ═══════════════════════════════════════════

        void DrawEntityToolbar()
        {
            EditorGUI.BeginChangeCheck();
            searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField, GUILayout.Width(140));
            if (EditorGUI.EndChangeCheck()) ApplyFilters();

            GUILayout.Space(5);

            // Archetype filter (always draw to avoid IMGUI control count mismatch)
            string[] archOptions = archetypeFilterOptions ?? new[] { "All" };
            int prev = archetypeFilterIndex;
            archetypeFilterIndex = EditorGUILayout.Popup(
                Mathf.Clamp(archetypeFilterIndex, 0, archOptions.Length - 1),
                archOptions, EditorStyles.toolbarDropDown, GUILayout.Width(100));
            if (prev != archetypeFilterIndex) ApplyFilters();
        }

        void DrawEntityView()
        {
            float headerH = 18f;
            float viewH = position.height - TOOLBAR_H - DETAIL_H - 4;
            float contentW = position.width - 6;

            // Column headers
            Rect headerRect = new Rect(0, TOOLBAR_H, contentW, headerH);
            EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.18f, 0.18f, 1f));
            GUI.Label(new Rect(headerRect.x + 4, headerRect.y, 45, headerH), "ID", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(headerRect.x + 52, headerRect.y, 100, headerH), "Name", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(headerRect.x + 160, headerRect.y, 90, headerH), "Archetype", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(headerRect.x + 255, headerRect.y, contentW - 260, headerH), "Components", EditorStyles.miniBoldLabel);

            // Empty state
            if (filtered.Count == 0)
            {
                Rect emptyRect = new Rect(0, TOOLBAR_H + headerH, contentW, viewH - headerH);
                EditorGUI.HelpBox(emptyRect, "No entities match the current filters.", MessageType.Info);
                DrawDetailPanel();
                return;
            }

            // Virtualized list
            float totalH = filtered.Count * ROW_HEIGHT;
            float scrollY = entityScroll.y;
            int startIdx = Mathf.Max(0, (int)(scrollY / ROW_HEIGHT));
            int endIdx = Mathf.Min(filtered.Count - 1,
                startIdx + Mathf.CeilToInt(viewH / ROW_HEIGHT) + 1);

            float scrollContentW = contentW - 20;
            Rect scrollRect = new Rect(0, TOOLBAR_H + headerH, contentW, viewH - headerH);
            Rect contentRect = new Rect(0, 0, scrollContentW, Mathf.Max(totalH, viewH - headerH));

            entityScroll = GUI.BeginScrollView(scrollRect, entityScroll, contentRect);

            float rowY = startIdx * ROW_HEIGHT;
            for (int i = startIdx; i <= endIdx && i < filtered.Count; i++)
            {
                var entity = filtered[i];
                DrawEntityRow(new Rect(0, rowY, scrollContentW, ROW_HEIGHT), entity, i);
                rowY += ROW_HEIGHT;
            }

            GUI.EndScrollView();

            // Detail panel
            DrawDetailPanel();
        }

        void DrawEntityRow(Rect rect, EntityCache entity, int index)
        {
            bool isSelected = entity.Id == selectedId;
            bool isHover = entity.Id == hoverId;

            // Background
            if (isSelected)
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.40f, 0.70f, 0.35f));
            else if (isHover)
                EditorGUI.DrawRect(rect, new Color(0.30f, 0.30f, 0.30f, 0.30f));
            else if (index % 2 == 1)
                EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 0.20f));

            if (entity.IsModified)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), Color.yellow);

            float x = rect.x + 4;
            float y = rect.y + 2;

            // ID
            GUI.Label(new Rect(x, y, 45, 18), entity.Id.ToString(), EditorStyles.miniLabel);
            x += 50;

            // Name
            string name = string.IsNullOrEmpty(entity.DebugName) ? "—" : entity.DebugName;
            GUI.Label(new Rect(x, y, 100, 18), name, EditorStyles.miniLabel);
            x += 108;

            // Archetype
            GUI.Label(new Rect(x, y, 85, 18), entity.ArchetypeLabel, EditorStyles.miniLabel);
            x += 90;

            // Component tags
            float tagStartX = x;
            for (int ci = 0; ci < entity.Components.Count; ci++)
            {
                var ct = entity.Components[ci];
                string icon = GetComponentIcon(ct);
                float tagW = 20;
                if (x + tagW > rect.xMax) break;

                var tagRect = new Rect(x, y + 1, tagW, 16);
                Color prev = GUI.color;
                int hash = ct.Name.GetHashCode();
                float hue = (hash & 0xFF) / 255f;
                GUI.color = Color.HSVToRGB(hue, 0.6f, 0.8f);
                GUI.Box(tagRect, icon, EditorStyles.helpBox);
                GUI.color = prev;
                x += tagW + 2;
            }

            // Interaction
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0)
                {
                    selectedId = entity.Id;
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.button == 1)
                {
                    selectedId = entity.Id;
                    Event.current.Use();
                    ShowEntityContextMenu(entity);
                    Repaint();
                }
            }

            if (Event.current.type == EventType.MouseMove && rect.Contains(Event.current.mousePosition))
            {
                if (hoverId != entity.Id)
                {
                    hoverId = entity.Id;
                    Repaint();
                }
            }
        }

        void DrawDetailPanel()
        {
            if (selectedId < 0) return;

            // Find selected entity
            EntityCache? found = null;
            foreach (var e in filtered)
                if (e.Id == selectedId) { found = e; break; }

            if (found == null) return;
            var entity = found.Value;

            float panelY = position.height - DETAIL_H;
            Rect bgRect = new Rect(0, panelY, position.width, DETAIL_H);
            EditorGUI.DrawRect(bgRect, new Color(0.15f, 0.15f, 0.15f, 1f));
            GUI.Box(bgRect, "", EditorStyles.helpBox);

            using (new GUILayout.AreaScope(bgRect))
            {
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

                EditorGUILayout.LabelField($"Entity [{entity.Id}]  —  {entity.Components.Count} components",
                    EditorStyles.boldLabel);

                if (!string.IsNullOrEmpty(entity.DebugName))
                    EditorGUILayout.LabelField($"Name: {entity.DebugName}", EditorStyles.miniLabel);

                EditorGUILayout.LabelField($"Archetype: {entity.ArchetypeLabel}  (h: {entity.ArchetypeHash:X8})",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4);

                // List components with values
                foreach (var compType in entity.Components)
                {
                    if (!typeMeta.TryGetValue(compType, out var meta) || !meta.IsCached)
                        CacheComponentMeta(compType);

                    string icon = GetComponentIcon(compType);

                    EditorGUILayout.LabelField($"{icon}  {compType.Name}",
                        EditorStyles.miniBoldLabel);

                    // Get value from pool
                    var pool = EntityManager.GetPool(compType);
                    if (pool.Has(entity.Id))
                    {
                        int poolIdx = pool.GetIndex(entity.Id);
                        object val = pool.GetComponents().GetValue(poolIdx);
                        var fields = typeMeta[compType].Fields;

                        if (fields.Length == 0)
                        {
                            EditorGUILayout.LabelField($"    {val}", EditorStyles.miniLabel);
                        }
                        else
                        {
                            EditorGUI.indentLevel += 2;
                            foreach (var f in fields)
                            {
                                object fVal = f.GetValue(val);
                                string formatted = FormatValue(f.FieldType, fVal);
                                EditorGUILayout.LabelField($"{f.Name}: {formatted}",
                                    EditorStyles.miniLabel);
                            }
                            EditorGUI.indentLevel -= 2;
                        }
                    }

                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        void ShowEntityContextMenu(EntityCache entity)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Entity ID"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = entity.Id.ToString();
                Debug.Log($"Copied Entity ID: {entity.Id}");
            });
            menu.AddItem(new GUIContent($"Archetype: {entity.ArchetypeLabel}"), false, () => { });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"Components ({entity.Components.Count})"), false, () => { });
            foreach (var ct in entity.Components)
                menu.AddItem(new GUIContent($"  {GetComponentIcon(ct)} {ct.Name}"), false, () => { });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Filter → Only this Archetype"), false, () =>
            {
                archetypeFilterIndex = archetypes.FindIndex(a => a.Hash == entity.ArchetypeHash) + 1;
                ApplyFilters();
            });
            menu.ShowAsContext();
        }

        // ═══════════════════════════════════════════
        // COMPONENTS VIEW (secondary)
        // ═══════════════════════════════════════════

        Vector2 compScroll;
        readonly Dictionary<string, bool> compFoldouts = new();

        void DrawComponentToolbar() { }

        void DrawComponentsView()
        {
            using (var scope = new GUILayout.ScrollViewScope(compScroll))
            {
                compScroll = scope.scrollPosition;

                int total = 0;
                foreach (var kvp in EntityManager.GetAllPools())
                {
                    string name = kvp.Key.Name;
                    int count = kvp.Value.GetEntities().Count;
                    total += count;

                    if (!compFoldouts.ContainsKey(name))
                        compFoldouts[name] = false;

                    var hr = EditorGUILayout.GetControlRect(false, 22);
                    EditorGUI.DrawRect(new Rect(hr.x, hr.y, hr.width, 22),
                        new Color(0.2f, 0.2f, 0.2f, 0.5f));

                    var foldRect = new Rect(hr.x + 4, hr.y, hr.width - 100, 22);
                    compFoldouts[name] = EditorGUI.Foldout(foldRect, compFoldouts[name], $"  {name}", true);

                    var cntRect = new Rect(hr.xMax - 95, hr.y + 2, 90, 18);
                    GUI.Box(cntRect, $"{count} entities", EditorStyles.helpBox);

                    if (compFoldouts[name])
                    {
                        EditorGUI.indentLevel++;
                        DrawPoolDetail(kvp.Value, kvp.Key, count);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(1);
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"Total instances: {total}", EditorStyles.miniBoldLabel);
            }
        }

        void DrawPoolDetail(IComponentPool pool, Type type, int count)
        {
            if (count == 0) { EditorGUILayout.LabelField("(empty)", EditorStyles.miniLabel); return; }

            int size = 0;
            try { size = System.Runtime.InteropServices.Marshal.SizeOf(type); } catch { }

            EditorGUILayout.LabelField($"struct | {size}B | [{count} instances]", EditorStyles.miniLabel);
            EditorGUILayout.Space(2);

            var entities = pool.GetEntities();
            var components = pool.GetComponents();
            int show = Mathf.Min(count, 15);

            for (int i = 0; i < show; i++)
            {
                int eid = entities[i];
                object val = components.GetValue(i);
                var meta = GetOrCacheMeta(type);
                if (meta.Fields.Length == 0)
                    EditorGUILayout.LabelField($"E[{eid}]", val?.ToString() ?? "null", EditorStyles.miniLabel);
                else
                {
                    var sb = new StringBuilder();
                    sb.Append("{ ");
                    for (int fi = 0; fi < meta.Fields.Length; fi++)
                    {
                        if (fi > 0) sb.Append(" | ");
                        sb.Append($"{meta.Fields[fi].Name}: {FormatValue(meta.Fields[fi].FieldType, meta.Fields[fi].GetValue(val))}");
                    }
                    sb.Append(" }");
                    EditorGUILayout.LabelField($"E[{eid}]", sb.ToString(), EditorStyles.miniLabel);
                }
            }
            if (count > 15)
                EditorGUILayout.LabelField($"... and {count - 15} more", EditorStyles.miniLabel);
        }

        // ═══════════════════════════════════════════
        // DATA CACHE
        // ═══════════════════════════════════════════

        void RebuildCache()
        {
            dirty = false;
            entities.Clear();
            archetypes.Clear();
            allCompTypes.Clear();

            // Collect all component types and their pools
            var pools = EntityManager.GetAllPools().ToList();
            var poolByType = new Dictionary<Type, IComponentPool>();
            foreach (var kvp in pools)
            {
                allCompTypes.Add(kvp.Key);
                poolByType[kvp.Key] = kvp.Value;
            }

            // Cache component meta
            foreach (var ct in allCompTypes)
                GetOrCacheMeta(ct);

            // Collect all entity IDs
            var entityIdSet = new HashSet<int>();
            foreach (var kvp in pools)
                foreach (int id in kvp.Value.GetEntities())
                    entityIdSet.Add(id);

            var sortedIds = entityIdSet.OrderBy(id => id).ToList();

            // Build archetype map: component set hash -> archetype
            var archetypeMap = new Dictionary<int, List<Type>>();

            foreach (int eid in sortedIds)
            {
                var comps = new List<Type>();
                foreach (var ct in allCompTypes)
                    if (poolByType[ct].Has(eid))
                        comps.Add(ct);

                int hash = ComputeArchetypeHash(comps);
                if (!archetypeMap.ContainsKey(hash))
                    archetypeMap[hash] = comps;

                string debugName = ExtractDebugName(eid, poolByType);

                entities.Add(new EntityCache
                {
                    Id = eid,
                    DebugName = debugName,
                    ArchetypeHash = hash,
                    ArchetypeLabel = archetypeMap[hash].Count > 0
                        ? $"A:{archetypeMap[hash].Count}C" : "A:0",
                    Components = comps,
                    IsModified = false,
                });
            }

            // Build archetype list
            foreach (var kvp in archetypeMap)
            {
                archetypes.Add(new ArchetypeInfo
                {
                    Hash = kvp.Key,
                    Label = $"A:{kvp.Value.Count}C",
                    ComponentTypes = kvp.Value,
                    Count = sortedIds.Count
                });
            }

            lastEntityCount = sortedIds.Count;

            // Build filter dropdowns
            BuildFilterOptions();

            // Apply current filters
            ApplyFilters();
        }

        void BuildFilterOptions()
        {
            // Archetype filter options
            var archLabels = new List<string> { "All Archetypes" };
            foreach (var a in archetypes)
                archLabels.Add($"{a.Label} ({a.Count})");
            archetypeFilterOptions = archLabels.ToArray();
            if (archetypeFilterIndex >= archetypeFilterOptions.Length)
                archetypeFilterIndex = 0;
        }

        void ApplyFilters()
        {
            filtered.Clear();

            bool hasSearch = !string.IsNullOrEmpty(searchText);
            bool hasArchFilter = archetypeFilterIndex > 0;

            foreach (var e in entities)
            {
                if (hasSearch)
                {
                    string q = searchText.ToLower();
                    bool match = e.Id.ToString().Contains(q)
                        || e.DebugName.ToLower().Contains(q)
                        || e.Components.Any(c => c.Name.ToLower().Contains(q));
                    if (!match) continue;
                }

                if (hasArchFilter)
                {
                    var arch = archetypes[archetypeFilterIndex - 1];
                    if (e.ArchetypeHash != arch.Hash) continue;
                }

                filtered.Add(e);
            }
        }

        int ComputeArchetypeHash(List<Type> comps)
        {
            int hash = 17;
            foreach (var t in comps)
                hash = hash * 31 + t.GetHashCode();
            return hash;
        }

        string ExtractDebugName(int eid, Dictionary<Type, IComponentPool> poolByType)
        {
            // Return the first component's type name as the entity name
            foreach (var kvp in poolByType)
            {
                if (!kvp.Value.Has(eid)) continue;
                return kvp.Key.Name;
            }
            return "";
        }

        // ═══════════════════════════════════════════
        // COMPONENT META CACHE
        // ═══════════════════════════════════════════

        ComponentMeta GetOrCacheMeta(Type type)
        {
            if (typeMeta.TryGetValue(type, out var meta) && meta.IsCached)
                return meta;

            meta = new ComponentMeta
            {
                Name = type.Name,
                Fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance),
                Icon = GetComponentIcon(type),
                IsCached = true,
            };
            typeMeta[type] = meta;
            return meta;
        }

        void CacheComponentMeta(Type type) => GetOrCacheMeta(type);

        static string GetComponentIcon(Type type)
        {
            string n = type.Name;
            foreach (var kvp in CompIconMap)
                if (n.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            return n.Length > 0 ? n.Substring(0, Math.Min(2, n.Length)) : "?";
        }

        // ═══════════════════════════════════════════
        // VALUE FORMATTING
        // ═══════════════════════════════════════════

        static string FormatValue(Type fieldType, object val)
        {
            if (val == null) return "null";
            if (fieldType == typeof(Vector2)) { var v = (Vector2)val; return $"({v.x:F3}, {v.y:F3})"; }
            if (fieldType == typeof(Vector3)) { var v = (Vector3)val; return $"({v.x:F3}, {v.y:F3}, {v.z:F3})"; }
            if (fieldType == typeof(Vector2Int)) { var v = (Vector2Int)val; return $"({v.x}, {v.y})"; }
            if (fieldType == typeof(Vector3Int)) { var v = (Vector3Int)val; return $"({v.x}, {v.y}, {v.z})"; }
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                return ((UnityEngine.Object)val)?.name ?? "null";
            if (fieldType == typeof(string)) return (string)val;
            if (!fieldType.IsValueType) return val.ToString();
            return val.ToString();
        }
    }
}
