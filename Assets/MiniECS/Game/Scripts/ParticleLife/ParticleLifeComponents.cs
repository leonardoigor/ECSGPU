using UnityEngine;
using MiniECS;

namespace Game.ParticleLife
{
    public struct ParticlePosition : IComponentData
    {
        public Vector3 Value;
    }

    public struct ParticleVelocity : IComponentData
    {
        public Vector3 Value;
    }

    public struct ParticleSpecies : IComponentData
    {
        public int Value;
    }
}
