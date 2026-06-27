
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class SystemRegistry {
    static readonly List<SystemWrapper> systems = new();
    public static IReadOnlyList<SystemWrapper> Systems => systems;

    public static void RegisterAll() {
        systems.Clear();
        foreach (var t in Assembly.GetExecutingAssembly().GetTypes()) {
            if (!t.IsValueType) continue;
            if (!typeof(ISystem).IsAssignableFrom(t)) continue;
            var wt = typeof(SystemWrapper<>).MakeGenericType(t);
            systems.Add((SystemWrapper)Activator.CreateInstance(wt));
        }
        systems.Sort((a, b) => {
            var tA = a.GetType().GetGenericArguments()[0];
            var tB = b.GetType().GetGenericArguments()[0];
            bool aBatch = typeof(IParallelBatchSystem).IsAssignableFrom(tA);
            bool aMain = typeof(IMainThreadSystem).IsAssignableFrom(tA);
            bool bBatch = typeof(IParallelBatchSystem).IsAssignableFrom(tB);
            bool bMain = typeof(IMainThreadSystem).IsAssignableFrom(tB);
            if (aBatch && bMain) return -1;
            if (aMain && bBatch) return 1;
            return string.Compare(tA.FullName, tB.FullName, StringComparison.Ordinal);
        });
    }
}
