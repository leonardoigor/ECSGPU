using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MiniECS.Physics
{
    /// <summary>
    /// Caches convex hull vertex data extracted from Unity meshes so that the
    /// narrow phase can run GJK/EPA against MeshColliders without per-frame work.
    /// Points of every hull are stored concatenated; ranges index into the blob.
    /// </summary>
    public static class ConvexHullRegistry
    {
        static NativeArray<float3> _points;
        static NativeArray<int2> _ranges; // x = start index, y = vertex count
        static Dictionary<Mesh, int> _meshToIndex = new Dictionary<Mesh, int>();
        static int _count;

        public static int GetOrCreate(Mesh mesh)
        {
            if (mesh == null) return -1;
            if (_meshToIndex.TryGetValue(mesh, out int idx)) return idx;

            var verts = mesh.vertices;
            int n = verts.Length;
            if (n == 0) return -1;

            int start = _points.IsCreated ? _points.Length : 0;
            var newPoints = new NativeArray<float3>(start + n, Allocator.Persistent);
            if (_points.IsCreated)
            {
                newPoints.Slice(0, start).CopyFrom(_points);
                _points.Dispose();
            }
            for (int i = 0; i < n; i++)
                newPoints[start + i] = (float3)verts[i];
            _points = newPoints;

            int rangeCount = _ranges.IsCreated ? _ranges.Length : 0;
            var newRanges = new NativeArray<int2>(rangeCount + 1, Allocator.Persistent);
            if (_ranges.IsCreated)
            {
                newRanges.Slice(0, rangeCount).CopyFrom(_ranges);
                _ranges.Dispose();
            }
            newRanges[rangeCount] = new int2(start, n);
            _ranges = newRanges;

            idx = _count++;
            _meshToIndex[mesh] = idx;
            return idx;
        }

        public static void GetHull(int index, out NativeArray<float3> points, out int start, out int count)
        {
            points = _points;
            start = _ranges[index].x;
            count = _ranges[index].y;
        }

        public static NativeArray<float3> Points => _points;
        public static NativeArray<int2> Ranges => _ranges;

        public static void Clear()
        {
            if (_points.IsCreated) _points.Dispose();
            if (_ranges.IsCreated) _ranges.Dispose();
            _points = default;
            _ranges = default;
            _meshToIndex.Clear();
            _count = 0;
        }
    }
}
