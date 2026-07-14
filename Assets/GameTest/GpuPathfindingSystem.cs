using Unity.Mathematics;

namespace ECSGPU.GpuEcs.Examples
{
    [GpuSystem("CSInitializePathGrid", ThreadX = 64)]
    public partial struct GpuPathInitKernel
    {
        [GpuReadOnly] public GpuComponentBuffer<GpuGridCoordComponent> _coords;
        [GpuReadWrite] public GpuComponentBuffer<GpuPathStateComponent> _state;

        public uint _entityCount;
        public int _targetX;
        public int _targetY;
        public int _blockedDistance;

        [GpuKernel]
        public void Execute()
        {
            uint idx = GpuDispatch.threadId.x;
            if (idx >= _entityCount) return;

            int2 coord = _coords[idx].Value;
            GpuPathStateComponent cell = _state[idx];
            cell.BestDirection = -1;

            if (cell.Walkable == 0u)
            {
                cell.Distance = _blockedDistance;
                _state[idx] = cell;
                return;
            }

            if (coord.x == _targetX && coord.y == _targetY)
            {
                cell.Distance = 0;
                cell.BestDirection = 4;
            }
            else
            {
                cell.Distance = _blockedDistance;
            }

            _state[idx] = cell;
        }
    }

    [GpuSystem("CSRelaxPathGrid", ThreadX = 64)]
    public partial struct GpuPathRelaxKernel
    {
        [GpuReadOnly] public GpuComponentBuffer<GpuGridCoordComponent> _coords;
        [GpuReadOnly] public GpuComponentBuffer<GpuPathStateComponent> _state;
        [GpuReadWrite] public GpuComponentBuffer<GpuPathStateNextComponent> _stateNext;

        public uint _entityCount;
        public int _width;
        public int _height;
        public int _blockedDistance;

        [GpuKernel]
        public void Execute()
        {
            uint idx = GpuDispatch.threadId.x;
            if (idx >= _entityCount) return;

            int2 coord = _coords[idx].Value;
            GpuPathStateComponent current = _state[idx];
            GpuPathStateNextComponent next;
            next.Distance = current.Distance;
            next.BestDirection = current.BestDirection;
            next.Walkable = current.Walkable;
            next.Padding = current.Padding;

            if (current.Walkable == 0u)
            {
                next.Distance = _blockedDistance;
                next.BestDirection = -1;
                _stateNext[idx] = next;
                return;
            }

            if (current.Distance == 0)
            {
                next.BestDirection = 4;
                _stateNext[idx] = next;
                return;
            }

            int bestDistance = current.Distance;
            int bestDirection = current.BestDirection;

            if (coord.x > 0)
            {
                uint neighborIndex = (uint)(coord.y * _width + (coord.x - 1));
                GpuPathStateComponent neighbor = _state[neighborIndex];
                if (neighbor.Walkable != 0u)
                {
                    int candidate = neighbor.Distance + 1;
                    if (candidate < bestDistance)
                    {
                        bestDistance = candidate;
                        bestDirection = 0;
                    }
                }
            }

            if (coord.x < _width - 1)
            {
                uint neighborIndex = (uint)(coord.y * _width + (coord.x + 1));
                GpuPathStateComponent neighbor = _state[neighborIndex];
                if (neighbor.Walkable != 0u)
                {
                    int candidate = neighbor.Distance + 1;
                    if (candidate < bestDistance)
                    {
                        bestDistance = candidate;
                        bestDirection = 1;
                    }
                }
            }

            if (coord.y > 0)
            {
                uint neighborIndex = (uint)((coord.y - 1) * _width + coord.x);
                GpuPathStateComponent neighbor = _state[neighborIndex];
                if (neighbor.Walkable != 0u)
                {
                    int candidate = neighbor.Distance + 1;
                    if (candidate < bestDistance)
                    {
                        bestDistance = candidate;
                        bestDirection = 2;
                    }
                }
            }

            if (coord.y < _height - 1)
            {
                uint neighborIndex = (uint)((coord.y + 1) * _width + coord.x);
                GpuPathStateComponent neighbor = _state[neighborIndex];
                if (neighbor.Walkable != 0u)
                {
                    int candidate = neighbor.Distance + 1;
                    if (candidate < bestDistance)
                    {
                        bestDistance = candidate;
                        bestDirection = 3;
                    }
                }
            }

            next.Distance = bestDistance;
            next.BestDirection = bestDirection;
            _stateNext[idx] = next;
        }
    }

    [GpuSystem("CSCommitPathGrid", ThreadX = 64)]
    public partial struct GpuPathCommitKernel
    {
        [GpuReadOnly] public GpuComponentBuffer<GpuPathStateNextComponent> _stateNext;
        [GpuReadWrite] public GpuComponentBuffer<GpuPathStateComponent> _state;

        public uint _entityCount;

        [GpuKernel]
        public void Execute()
        {
            uint idx = GpuDispatch.threadId.x;
            if (idx >= _entityCount) return;

            GpuPathStateNextComponent source = _stateNext[idx];
            GpuPathStateComponent target;
            target.Distance = source.Distance;
            target.BestDirection = source.BestDirection;
            target.Walkable = source.Walkable;
            target.Padding = source.Padding;
            _state[idx] = target;
        }
    }
}
