using MiniECS;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct PositionComponent : IComponentData
    {
        public Vector3 Value;
    }
}
