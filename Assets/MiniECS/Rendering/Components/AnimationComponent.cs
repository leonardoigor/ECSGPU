using MiniECS;

namespace MiniECS.Rendering
{
    public struct AnimationComponent : IComponentData
    {
        public int AnimIndex;
        public float CurrentFrame;
        public float Speed;
        public int FrameCount;
        public float FrameRate;
    }
}
