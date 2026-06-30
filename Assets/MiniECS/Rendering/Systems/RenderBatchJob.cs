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
        [ReadOnly] public NativeArray<Vector4> AnimData;
        [ReadOnly] public NativeArray<Color> Colors;
        [ReadOnly] public NativeArray<int> Prefix;
        [NativeDisableParallelForRestriction]
        public NativeArray<InstanceData> FlatInstanceData;

        public void Execute(int index)
        {
            int modelId = ModelOfEntity[index];
            int slot = SlotForEntity[index];
            int writeIdx = Prefix[modelId] + slot;

            FlatInstanceData[writeIdx] = new InstanceData
            {
                transformMatrix = Matrix4x4.TRS(
                    Positions[index],
                    Rotations[index],
                    Vector3.one
                ),
                animIndex = AnimData[index].x,
                currentFrame = AnimData[index].y,
                clipStartFrame = AnimData[index].z,
                clipFrameCount = AnimData[index].w,
                color = Colors[index]
            };
        }
    }
}
