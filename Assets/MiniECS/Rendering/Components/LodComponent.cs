using MiniECS;

namespace MiniECS.Rendering
{
    public struct LodComponent : IComponentData
    {
        public int LodModelId;
        public int LodLevel;
    }
}
