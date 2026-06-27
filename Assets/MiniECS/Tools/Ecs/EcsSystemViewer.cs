using System.Collections.Generic;
using UnityEngine;

namespace MiniECS.Tools
{
    public class EcsSystemViewer : MonoBehaviour
    {
        [SerializeField] KeyCode toggleKey = KeyCode.F5;
        [SerializeField] bool showWindow = true;

        Vector2 scrollPos;

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showWindow = !showWindow;
        }

        void OnGUI()
        {
            if (!showWindow) return;

            var scheduler = SystemScheduler.Main;
            bool hasTiming = scheduler != null;

            float w = 400;
            float h = 300;
            float x = Screen.width - w - 10;
            float y = 10;

            GUI.Box(new Rect(x, y, w, h), "ECS Systems");

            float lx = x + 5, ly = y + 20, lw = w - 10;
            float rowH = 20;

            var systems = SystemRegistry.Systems;
            float totalH = systems.Count * rowH + 30;
            float viewH = h - 30;

            scrollPos = GUI.BeginScrollView(
                new Rect(lx, ly, lw, viewH),
                scrollPos,
                new Rect(0, 0, lw - 20, Mathf.Max(totalH, viewH)));

            float ry = 5;
            string header = hasTiming
                ? "System                    Side        Last (ms)"
                : "System                    Side";
            GUI.Box(new Rect(0, ry, lw - 20, rowH), header);
            ry += rowH;

            foreach (var s in systems)
            {
                string side = (s.IsServer ? "S" : "") + (s.IsClient ? "C" : "");
                string label;
                if (hasTiming)
                {
                    string time = "—";
                    if (scheduler.LastFrameTimeMs.TryGetValue(s.DisplayName, out double t))
                        time = $"{t:F4}";
                    label = $"{s.DisplayName,-30} {side,-10} {time}";
                }
                else
                {
                    label = $"{s.DisplayName,-30} {side}";
                }

                GUI.Box(new Rect(0, ry, lw - 20, rowH), label);
                ry += rowH;
            }

            GUI.EndScrollView();
        }
    }
}
