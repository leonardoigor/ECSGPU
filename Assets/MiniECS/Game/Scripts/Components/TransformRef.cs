using MiniECS;
using UnityEngine;

namespace Game.Components
{
    // TransformRef is a local reference — not synced over network
    public struct TransformRef : IComponentData
    {
        public Transform Value;
    }
}
