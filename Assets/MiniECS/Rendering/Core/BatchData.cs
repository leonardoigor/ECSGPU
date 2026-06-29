using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniECS.Rendering
{
    public sealed class BatchData
    {
        public static readonly BatchData Instance = new BatchData();

        readonly List<InstanceData[]> perModelInstanceData = new();
        readonly List<ComputeBuffer> perModelBuffers = new();
        readonly List<ComputeBuffer> perModelMatrixBuffers = new();
        readonly HashSet<int> invalidVatModels = new();
        int[] counts;

        BatchData()
        {
            counts = new int[16];
        }

        public void EnsureModelCapacity(int modelCount)
        {
            while (perModelInstanceData.Count < modelCount)
            {
                perModelInstanceData.Add(null);
                perModelBuffers.Add(null);
                perModelMatrixBuffers.Add(null);
            }
            if (modelCount > counts.Length)
            {
                int[] newCounts = new int[modelCount * 2];
                System.Array.Copy(counts, newCounts, counts.Length);
                counts = newCounts;
            }
        }

        public ref int GetCount(int modelId) => ref counts[modelId];

        public void SetInstanceData(int modelId, InstanceData[] data, int instanceCount)
        {
            perModelInstanceData[modelId] = data;
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
                sizeof(float) * 20
            );

            var matBuf = perModelMatrixBuffers[modelId];
            matBuf?.Release();
            perModelMatrixBuffers[modelId] = new ComputeBuffer(
                Mathf.NextPowerOfTwo(needed),
                sizeof(float) * 16
            );
        }

        public InstanceData[] GetInstanceData(int modelId) => perModelInstanceData[modelId];

        public void UploadAndDraw(int modelId, Mesh mesh, Material material,
            MaterialPropertyBlock mpb, ComputeBuffer argsBuffer, Bounds bounds)
        {
            int instanceCount = counts[modelId];
            if (instanceCount == 0)
                return;

            var instanceData = perModelInstanceData[modelId];
            var buffer = perModelBuffers[modelId];
            var matrixBuffer = perModelMatrixBuffers[modelId];

            if (instanceData == null)
            {
                Debug.LogError($"[BatchData] UploadAndDraw: instanceData is NULL for modelId={modelId}");
                return;
            }
            if (buffer == null)
            {
                Debug.LogError($"[BatchData] UploadAndDraw: ComputeBuffer is NULL for modelId={modelId}");
                return;
            }
            if (mesh == null)
            {
                Debug.LogError($"[BatchData] UploadAndDraw: mesh is NULL for modelId={modelId}");
                return;
            }
            if (material == null)
            {
                Debug.LogError($"[BatchData] UploadAndDraw: material is NULL for modelId={modelId}");
                return;
            }

            buffer.SetData(instanceData, 0, 0, instanceCount);

            var args = new uint[5];
            args[0] = mesh.GetIndexCount(0);
            args[1] = (uint)instanceCount;
            args[2] = mesh.GetIndexStart(0);
            args[3] = mesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuffer.SetData(args);

            var vatData = ModelRegistry.GetVatData(modelId);
            if (vatData != null)
            {
                if (mesh.vertexCount != vatData.vertexCount)
                {
                    if (invalidVatModels.Add(modelId))
                    {
                        Debug.LogError(
                            $"[BatchData] VAT mesh mismatch on modelId={modelId}. " +
                            $"Draw mesh vertexCount={mesh.vertexCount} " +
                            $"but VAT vertexCount={vatData.vertexCount}. Re-bake the VAT asset."
                        );
                    }
                    return;
                }

                mpb.SetBuffer("_InstanceBuffer", buffer);
                mpb.SetTexture("_PositionVAT", vatData.positionTexture);
                mpb.SetTexture("_NormalVAT", vatData.normalTexture);
                mpb.SetFloat("_VATWidth", vatData.vertexCount);
                mpb.SetFloat("_VATHeight", vatData.totalFrames);
            }
            else
            {
                var matrices = new Matrix4x4[instanceCount];
                for (int i = 0; i < instanceCount; i++)
                    matrices[i] = instanceData[i].transformMatrix;
                matrixBuffer.SetData(matrices, 0, 0, instanceCount);
                mpb.SetBuffer("_MatrixBuffer", matrixBuffer);
            }

            var cmd = new CommandBuffer { name = "VAT Draw" };
            cmd.DrawMeshInstancedIndirect(mesh, 0, material, -1, argsBuffer, 0, mpb);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        public void ClearCounts(int modelCount)
        {
            for (int i = 0; i < modelCount; i++)
                counts[i] = 0;
        }

        public void Dispose()
        {
            for (int i = 0; i < perModelBuffers.Count; i++)
            {
                perModelBuffers[i]?.Release();
                perModelMatrixBuffers[i]?.Release();
            }
            invalidVatModels.Clear();
            perModelInstanceData.Clear();
            perModelBuffers.Clear();
            perModelMatrixBuffers.Clear();
        }
    }
}
