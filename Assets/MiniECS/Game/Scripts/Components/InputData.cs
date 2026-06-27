using MiniECS;
using UnityEngine;

namespace Game.Components
{
    // InputData is client-local — sent via RPC, not synced
    public struct InputData : IComponentData
    {
        public Vector2 Move;
    }
}
