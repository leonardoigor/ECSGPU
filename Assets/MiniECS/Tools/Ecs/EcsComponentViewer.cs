using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MiniECS;
using UnityEngine;

namespace MiniECS.Tools
{
    public class EcsComponentViewer : MonoBehaviour
    {
        [SerializeField] KeyCode toggleKey = KeyCode.F6;
        [SerializeField] bool showWindow = true;

        Vector2 scrollPos;
        int selectedIndex = -1;
        string[] poolNames;
        List<KeyValuePair<Type, IComponentPool>> pools = new();

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showWindow = !showWindow;
        }

        void OnGUI()
        {
            if (!showWindow) return;

            RefreshPools();

            float w = 450;
            float h = 400;
            float x = Screen.width - w - 10;
            float y = Screen.height - h - 10;

            GUI.Box(new Rect(x, y, w, h), "ECS Components");

            float lx = x + 5, ly = y + 20, lw = w - 10;
            float rowH = 20;
            float totalH = pools.Count * rowH + 30;
            float viewH = h - 30;

            scrollPos = GUI.BeginScrollView(
                new Rect(lx, ly, lw, viewH),
                scrollPos,
                new Rect(0, 0, lw - 20, Mathf.Max(totalH, viewH)));

            float ry = 5;
            GUI.Box(new Rect(0, ry, lw - 20, rowH), "Component                 Entities     Size (bytes)");
            ry += rowH;

            for (int i = 0; i < pools.Count; i++)
            {
                var kvp = pools[i];
                int count = kvp.Value.GetEntities().Count;
                int elementSize = 0;
                try { elementSize = System.Runtime.InteropServices.Marshal.SizeOf(kvp.Key); }
                catch { elementSize = -1; }

                string sizeStr = elementSize > 0 ? $"{elementSize}" : "?";

                bool isSelected = i == selectedIndex;
                string label = $"{kvp.Key.Name,-25} {count,-12} {sizeStr}";

                var rect = new Rect(0, ry, lw - 20, rowH);
                if (GUI.Button(rect, label))
                    selectedIndex = isSelected ? -1 : i;

                ry += rowH;

                if (isSelected && count > 0)
                {
                    var pool = kvp.Value;
                    var entities = pool.GetEntities();
                    var lookup = pool.GetLookup();
                    var compArray = pool.GetComponents();

                    string detail = ShowPoolData(kvp.Key, compArray, entities, lookup, count);
                    GUI.Box(new Rect(10, ry, lw - 40, 60), detail);
                    ry += 65;
                }
            }

            GUI.EndScrollView();
        }

        void RefreshPools()
        {
            pools.Clear();
            foreach (var kvp in EntityManager.GetAllPools())
                pools.Add(kvp);
        }

        string ShowPoolData(Type type, Array components, List<int> entities,
            Dictionary<int, int> lookup, int count)
        {
            if (count == 0) return "No entities";
            if (count > 20)
                return $"{count} entities total\nFirst 5 shown below...";

            var sb = new StringBuilder();
            int showCount = Mathf.Min(count, 5);
            for (int i = 0; i < showCount; i++)
            {
                int entityId = entities[i];
                if (i > 0) sb.Append("\n");
                sb.Append($"  E[{entityId}]: ");
                try
                {
                    var val = components.GetValue(i);
                    sb.Append(val?.ToString() ?? "null");
                }
                catch { sb.Append("<error>"); }
            }
            if (count > 5)
                sb.Append($"\n  ... and {count - 5} more");
            return sb.ToString();
        }
    }
}
