using UnityEngine;
using MiniECS;

public struct PathCellCpuComponent : IComponentData
{
    public Vector2Int Coord;
    public int Distance;
    public int BestDirection;
    public bool Walkable;
}
