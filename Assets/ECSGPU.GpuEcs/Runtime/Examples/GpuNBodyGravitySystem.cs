// v8
using Unity.Mathematics;

namespace ECSGPU.GpuEcs.Examples
{
    [GpuSystem("CSGravity", ThreadX = 256)]
    public partial struct GpuNBodyGravityKernel
    {
        [GpuReadWrite] public GpuComponentBuffer<GpuPositionComponent> _positions;
        [GpuReadWrite] public GpuComponentBuffer<GpuVelocityComponent> _velocities;
        [GpuReadOnly]  public GpuComponentBuffer<GpuMassComponent> _masses;
        [GpuReadWrite] public GpuComponentBuffer<GpuForceComponent> _forces;

        public float _deltaTime;
        public float _gravitationalConstant;
        public float _softening;
        public uint _entityCount;
        public bool _planarMotion;

        [GpuKernel]
        public void Execute()
        {
            uint idx = GpuDispatch.threadId.x;
            if (idx >= _entityCount) return;

            float3 myPos = _positions[idx].Value;
            float3 acc = float3.zero;

            for (uint other = 0; other < _entityCount; other++)
            {
                if (other == idx) continue;
                float3 delta = _positions[other].Value - myPos;
                if (_planarMotion) delta.y = 0;
                float distSqr = GpuMath.dot(delta, delta) + _softening * _softening;
                float invDist3 = GpuMath.rsqrt(distSqr);
                invDist3 = invDist3 * invDist3 * invDist3;
                acc += delta * (_gravitationalConstant * _masses[other].Value * invDist3);
            }

            GpuForceComponent forceResult;
            forceResult.Value = acc * _masses[idx].Value;
            _forces[idx] = forceResult;
        }
    }

    [GpuSystem("CSIntegrateMotion", ThreadX = 256)]
    public partial struct GpuNBodyIntegrateKernel
    {
        [GpuReadWrite] public GpuComponentBuffer<GpuPositionComponent> _positions;
        [GpuReadWrite] public GpuComponentBuffer<GpuVelocityComponent> _velocities;
        [GpuReadOnly]  public GpuComponentBuffer<GpuMassComponent> _masses;
        [GpuReadOnly]  public GpuComponentBuffer<GpuForceComponent> _forces;

        public float _deltaTime;
        public uint _entityCount;
        public bool _planarMotion;

        [GpuKernel]
        public void Execute()
        {
            uint idx = GpuDispatch.threadId.x;
            if (idx >= _entityCount) return;

            float3 position = _positions[idx].Value;
            float3 velocity = _velocities[idx].Value;
            float mass = _masses[idx].Value;
            float3 force = _forces[idx].Value;

            float3 acceleration = force / GpuMath.max(mass, 0.0001f);

            velocity += acceleration * _deltaTime;
            position += velocity * _deltaTime;

            if (_planarMotion)
            {
                position.y = 0;
                velocity.y = 0;
            }

            GpuPositionComponent posResult;
            posResult.Value = position;
            _positions[idx] = posResult;

            GpuVelocityComponent velResult;
            velResult.Value = velocity;
            _velocities[idx] = velResult;
        }
    }
}
