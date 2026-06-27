using MiniECS;
using UnityEngine;

namespace MiniECS.Rendering
{
    public struct RotationComponent : IComponentData
    {
        public Quaternion Value;
    }
}
