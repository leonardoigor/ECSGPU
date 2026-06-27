using System.Collections.Generic;
using UnityEngine;

namespace MiniECS.Rendering
{
    public sealed class BatchData
    {
        public static readonly BatchData Instance = new BatchData();

        readonly List<Matrix4x4[]> perModelMatrices = new();
        readonly List<ComputeBuffer> perModelBuffers = new();
        int[] counts;

        BatchData()
        {
            counts = new int[16];
        }

        public void EnsureModelCapacity(int modelCount)
        {
            while (perModelMatrices.Count < modelCount)
            {
                perModelMatrices.Add(null);
                perModelBuffers.Add(null);
            }
            if (modelCount > counts.Length)
            {
                int[] newCounts = new int[modelCount * 2];
                System.Array.Copy(counts, newCounts, counts.Length);
                counts = newCounts;
            }
        }

        public ref int GetCount(int modelId) => ref counts[modelId];

        public void SetMatrices(int modelId, Matrix4x4[] matrices, int instanceCount)
        {
            perModelMatrices[modelId] = matrices;
            counts[modelId] = instanceCount;

            EnsureBufferSize(modelId, instanceCount);
        }

        void EnsureBufferSize(int modelId, int needed)
        {
            var buf = perModelBuffers[modelId];
            if (buf != null && buf.count >= needed)
                return;

            buf?.Release();
            perModelBuffers[modelId] = new ComputeBuffer(
                Mathf.NextPowerOfTwo(needed),
                sizeof(float) * 16
            );
        }

        public Matrix4x4[] GetMatrices(int modelId) => perModelMatrices[modelId];

        public void UploadAndDraw(int modelId, Mesh mesh, Material material,
            MaterialPropertyBlock mpb, ComputeBuffer argsBuffer, Bounds bounds)
        {
            int instanceCount = counts[modelId];
            if (instanceCount == 0)
                return;

            var matrices = perModelMatrices[modelId];
            var buffer = perModelBuffers[modelId];

            buffer.SetData(matrices, 0, 0, instanceCount);

            uint[] args =
            {
                mesh.GetIndexCount(0),
                (uint)instanceCount,
                mesh.GetIndexStart(0),
                mesh.GetBaseVertex(0),
                0
            };
            argsBuffer.SetData(args);

            mpb.SetBuffer("_MatrixBuffer", buffer);

            Graphics.DrawMeshInstancedIndirect(
                mesh, 0, material, bounds,
                argsBuffer, 0, mpb,
                UnityEngine.Rendering.ShadowCastingMode.On, true
            );
        }

        public void ClearCounts(int modelCount)
        {
            for (int i = 0; i < modelCount; i++)
                counts[i] = 0;
        }

        public void Dispose()
        {
            for (int i = 0; i < perModelBuffers.Count; i++)
                perModelBuffers[i]?.Release();
            perModelMatrices.Clear();
            perModelBuffers.Clear();
        }
    }
}
