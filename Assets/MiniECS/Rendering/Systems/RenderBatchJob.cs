using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace MiniECS.Rendering
{
    [BurstCompile]
    struct RenderBatchJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ModelOfEntity;
        [ReadOnly] public NativeArray<int> SlotForEntity;
        [ReadOnly] public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<Quaternion> Rotations;
        [ReadOnly] public NativeArray<int> Prefix;
        public NativeArray<Matrix4x4> FlatMatrices;

        public void Execute(int index)
        {
            int modelId = ModelOfEntity[index];
            int slot = SlotForEntity[index];
            FlatMatrices[Prefix[modelId] + slot] = Matrix4x4.TRS(
                Positions[index],
                Rotations[index],
                Vector3.one
            );
        }
    }
}
