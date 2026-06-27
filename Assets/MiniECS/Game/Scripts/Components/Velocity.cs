using MiniECS;
using UnityEngine;

namespace Game.Components
{
    [Sync]
    public struct Velocity : IComponentData
    {
        public Vector2 Value;
    }
}
