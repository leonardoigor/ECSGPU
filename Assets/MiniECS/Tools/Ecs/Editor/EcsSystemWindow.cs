using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using MiniECS;

namespace MiniECS.Tools.Editor
{
    public class EcsSystemWindow : EditorWindow
    {
        [MenuItem("Tools/MiniECS/ECS Systems")]
        public static void Open() => GetWindow<EcsSystemWindow>("ECS Systems");

        Vector2 scrollPos;
        int selectedIndex = -1;

        void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
                Repaint();
        }

        void OnGUI()
        {
            var systems = SystemRegistry.Systems;
            if (systems == null || systems.Count == 0)
            {
                EditorGUILayout.HelpBox("No ECS systems registered.\nCall SystemRegistry.RegisterAll() or enter Play Mode.", MessageType.Info);
                return;
            }

            DrawToolbar(systems.Count);

            using (var scope = new GUILayout.ScrollViewScope(scrollPos))
            {
                scrollPos = scope.scrollPosition;

                for (int i = 0; i < systems.Count; i++)
                {
                    var s = systems[i];
                    DrawSystemRow(s, i);
                }
            }
        }

        void DrawToolbar(int count)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField($"Systems ({count})", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    Repaint();
            }
        }

        void DrawSystemRow(SystemWrapper s, int index)
        {
            bool hasTiming = EditorApplication.isPlaying && SystemScheduler.Main != null;
            double ms = 0;
            if (hasTiming)
                SystemScheduler.Main.LastFrameTimeMs.TryGetValue(s.DisplayName, out ms);

            Color timeColor = ms < 1 ? Color.green : ms < 5 ? Color.yellow : Color.red;

            string sideIcon = s.IsServer ? (s.IsClient ? "\u25C9" : "\u25D3") : "\u25D1";
            string timing = hasTiming ? $"{ms:F4} ms" : "";

            float rowH = 20;
            if (index == selectedIndex) rowH = 120;

            var rowRect = EditorGUILayout.GetControlRect(false, rowH);

            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                selectedIndex = (selectedIndex == index) ? -1 : index;
                Event.current.Use();
                Repaint();
            }

            if (index == selectedIndex)
                EditorGUI.DrawRect(rowRect, new Color(0.2f, 0.4f, 0.8f, 0.25f));

            // Main line
            var lineRect = new Rect(rowRect.x, rowRect.y, rowRect.width, 20);

            float timingWidth = hasTiming ? 80 : 0;
            float nameWidth = lineRect.width - 30 - timingWidth;

            GUI.Label(new Rect(lineRect.x + 5, lineRect.y, 25, 20), sideIcon);
            GUI.Label(new Rect(lineRect.x + 28, lineRect.y, nameWidth, 20), s.DisplayName);

            if (hasTiming)
            {
                var prevColor = GUI.color;
                GUI.color = timeColor;
                GUI.Label(new Rect(lineRect.xMax - timingWidth - 5, lineRect.y, timingWidth, 20), timing, EditorStyles.boldLabel);
                GUI.color = prevColor;
            }

            // Details panel when selected
            if (index == selectedIndex)
            {
                float detailY = rowRect.y + 22;
                float detailH = rowH - 24;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Details", EditorStyles.miniBoldLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Server", GUILayout.Width(60));
                        EditorGUILayout.LabelField(s.IsServer ? "Yes" : "No", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("Client", GUILayout.Width(60));
                        EditorGUILayout.LabelField(s.IsClient ? "Yes" : "No", EditorStyles.miniLabel);
                    }

                    bool isMainThread = typeof(IMainThreadSystem)
                        .IsAssignableFrom(s.GetType().GetGenericArguments()[0]);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Main Thread", GUILayout.Width(80));
                        EditorGUILayout.LabelField(isMainThread ? "Yes" : "No", EditorStyles.miniLabel);
                    }

                    if (hasTiming)
                    {
                        EditorGUILayout.Space(2);
                        EditorGUILayout.LabelField($"Execution Time: {ms:F4} ms  |  {ms * 1000:F2} us", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.Space(2);
                }
            }
        }
    }
}
