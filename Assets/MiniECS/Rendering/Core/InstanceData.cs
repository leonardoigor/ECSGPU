using UnityEngine;

namespace MiniECS.Rendering
{
    public struct InstanceData
    {
        public Matrix4x4 transformMatrix;
        public float animIndex;
        public float currentFrame;
        public float clipStartFrame;
        public float clipFrameCount;
    }
}
