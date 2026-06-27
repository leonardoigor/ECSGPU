using MiniECS;
using UnityEngine;

namespace Game.Components
{
    // AnimatorRef is a local reference — not synced over network
    public struct AnimatorRef : IComponentData
    {
        public Animator Value;
    }
}
