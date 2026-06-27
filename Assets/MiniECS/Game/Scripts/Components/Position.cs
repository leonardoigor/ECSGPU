using MiniECS;
using UnityEngine;

namespace Game.Components
{
    [Sync]
    public struct Position : IComponentData
    {
        public Vector2 Value;
    }
}
