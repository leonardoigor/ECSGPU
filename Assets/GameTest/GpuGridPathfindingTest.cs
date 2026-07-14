using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using ECSGPU.GpuEcs;
using ECSGPU.GpuEcs.Examples;
using MiniECS;
using MiniECS.Rendering;

public sealed class GpuGridPathfindingTest : MonoBehaviour
{
    const int BlockedDistance = 1 << 20;

    [Header("Shader")]
    public ComputeShader pathfindingShader;
    public ComputeShader initShader;
    public ComputeShader relaxShader;
    public ComputeShader commitShader;

    [Header("Grid")]
    public int width = 24;
    public int height = 16;
    public float cellSize = 1.5f;
    public Vector2Int startCell = new Vector2Int(1, 1);
    public Vector2Int targetCell = new Vector2Int(22, 14);

    [Header("Visuals")]
    public bool enableDebugVisuals = true;
    public float tileHeight = 0.15f;
    public Gradient distanceGradient;

    [Header("CPU Bridge")]
    public bool exportToCpuEcs;
    public bool exportPositionsToMiniEcs = true;

    GpuWorld _world;
    GpuPathInitKernel _initKernel;
    GpuPathRelaxKernel _relaxKernel;
    GpuPathCommitKernel _commitKernel;
    GpuWorldDebug _worldDebug;
    GpuComponentBuffer<GpuPathStateComponent> _stateBuffer;
    GpuComponentBuffer<GpuPathStateNextComponent> _stateNextBuffer;

    Transform[] _tiles;
    MeshRenderer[] _tileRenderers;
    LineRenderer _pathLine;
    GpuReadbackRequest<GpuPathStateComponent> _pendingStateReadback;
    GpuPathStateComponent[] _cpuSnapshot;
    Entity[] _cpuPathEntities;
    bool _readbackPending;
    bool _applyVisualsAfterReadback;
    bool _exportCpuAfterReadback;

    void Start()
    {
        EnsureGradient();
        ResolveShaders();

        if (initShader == null || relaxShader == null || commitShader == null)
        {
            Debug.LogError("[GpuPathfinding] Missing generated compute shader.", this);
            enabled = false;
            return;
        }

        BuildWorld();
        if (enableDebugVisuals)
            BuildVisuals();
        SolveAndVisualize();
    }

    void Update()
    {
        PollReadback();

        if (Input.GetKeyDown(KeyCode.Space))
            SolveAndVisualize();
    }

    void BuildWorld()
    {
        int count = width * height;
        _world = new GpuWorld();
        _worldDebug = GetComponent<GpuWorldDebug>();
        if (_worldDebug == null)
            _worldDebug = gameObject.AddComponent<GpuWorldDebug>();
        _worldDebug.World = _world;

        var coords = new GpuGridCoordComponent[count];
        var stateData = new GpuPathStateComponent[count];
        var stateNextData = new GpuPathStateNextComponent[count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y);
                bool walkable = IsWalkableCell(x, y);

                coords[index] = new GpuGridCoordComponent
                {
                    Value = new int2(x, y)
                };

                stateData[index] = new GpuPathStateComponent
                {
                    Distance = BlockedDistance,
                    BestDirection = -1,
                    Walkable = walkable ? 1u : 0u,
                    Padding = 0u
                };

                stateNextData[index] = new GpuPathStateNextComponent
                {
                    Distance = BlockedDistance,
                    BestDirection = -1,
                    Walkable = walkable ? 1u : 0u,
                    Padding = 0u
                };
            }
        }

        ForceWalkable(stateData, stateNextData, startCell);
        ForceWalkable(stateData, stateNextData, targetCell);

        _world.SetComponentData(coords);
        _world.SetComponentData(stateData);
        _world.SetComponentData(stateNextData);

        _stateBuffer = _world.GetBuffer<GpuPathStateComponent>();
        _stateNextBuffer = _world.GetBuffer<GpuPathStateNextComponent>();
        _cpuPathEntities = exportToCpuEcs ? new Entity[count] : null;
    }

    void BuildVisuals()
    {
        var existingRoot = transform.Find("GridRoot");
        if (existingRoot != null)
            Destroy(existingRoot.gameObject);

        Transform root = new GameObject("GridRoot").transform;
        root.SetParent(transform, false);

        int count = width * height;
        _tiles = new Transform[count];
        _tileRenderers = new MeshRenderer[count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y);
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = $"Cell_{x}_{y}";
                tile.transform.SetParent(root, false);
                tile.transform.position = CellToWorld(x, y);
                tile.transform.localScale = new Vector3(cellSize * 0.92f, tileHeight, cellSize * 0.92f);
                _tiles[index] = tile.transform;
                _tileRenderers[index] = tile.GetComponent<MeshRenderer>();
            }
        }

        var pathGo = new GameObject("PathLine");
        pathGo.transform.SetParent(transform, false);
        _pathLine = pathGo.AddComponent<LineRenderer>();
        _pathLine.material = new Material(Shader.Find("Sprites/Default"));
        _pathLine.widthMultiplier = cellSize * 0.22f;
        _pathLine.positionCount = 0;
        _pathLine.startColor = Color.yellow;
        _pathLine.endColor = new Color(1f, 0.6f, 0.1f);
        _pathLine.numCapVertices = 4;
    }

    void SolveAndVisualize()
    {
        int count = width * height;
        if (count <= 0)
            return;

        _initKernel = new GpuPathInitKernel
        {
            _coords = _world.GetBuffer<GpuGridCoordComponent>(),
            _state = _stateBuffer,
            _entityCount = (uint)count,
            _targetX = targetCell.x,
            _targetY = targetCell.y,
            _blockedDistance = BlockedDistance
        };

        _relaxKernel = new GpuPathRelaxKernel
        {
            _coords = _world.GetBuffer<GpuGridCoordComponent>(),
            _state = _stateBuffer,
            _stateNext = _stateNextBuffer,
            _entityCount = (uint)count,
            _width = width,
            _height = height,
            _blockedDistance = BlockedDistance
        };

        _commitKernel = new GpuPathCommitKernel
        {
            _state = _stateBuffer,
            _stateNext = _stateNextBuffer,
            _entityCount = (uint)count
        };

        _initKernel.Execute(initShader, _world, count);

        int iterations = count;
        for (int i = 0; i < iterations; i++)
        {
            _relaxKernel.Execute(relaxShader, _world, count);
            _commitKernel.Execute(commitShader, _world, count);
        }

        RequestStateSnapshot(enableDebugVisuals, exportToCpuEcs);
    }

    void ApplyVisuals(GpuPathStateComponent[] stateData)
    {
        int furthestDistance = 1;
        for (int i = 0; i < stateData.Length; i++)
        {
            if (stateData[i].Walkable != 0u && stateData[i].Distance < BlockedDistance)
                furthestDistance = Mathf.Max(furthestDistance, stateData[i].Distance);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y);
                var state = stateData[index];
                Color color;

                if (state.Walkable == 0u)
                    color = new Color(0.08f, 0.08f, 0.08f);
                else if (x == targetCell.x && y == targetCell.y)
                    color = Color.green;
                else if (x == startCell.x && y == startCell.y)
                    color = Color.cyan;
                else if (state.Distance >= BlockedDistance)
                    color = Color.red;
                else
                    color = distanceGradient.Evaluate(state.Distance / (float)furthestDistance);

                _tileRenderers[index].material.color = color;
            }
        }

        var pathPoints = BuildPathPoints(stateData);
        _pathLine.positionCount = pathPoints.Count;
        for (int i = 0; i < pathPoints.Count; i++)
            _pathLine.SetPosition(i, pathPoints[i]);
    }

    List<Vector3> BuildPathPoints(GpuPathStateComponent[] stateData)
    {
        var path = new List<Vector3>();
        Vector2Int current = startCell;
        int safety = width * height;

        for (int i = 0; i < safety; i++)
        {
            path.Add(CellToWorld(current.x, current.y) + Vector3.up * (cellSize * 0.35f));
            if (current == targetCell)
                break;

            var state = stateData[ToIndex(current.x, current.y)];
            if (state.Walkable == 0u || state.BestDirection < 0)
                break;

            Vector2Int step = DirectionToOffset(state.BestDirection);
            if (step == Vector2Int.zero)
                break;

            current += step;
        }

        return path;
    }

    Vector2Int DirectionToOffset(int direction)
    {
        switch (direction)
        {
            case 0: return Vector2Int.left;
            case 1: return Vector2Int.right;
            case 2: return Vector2Int.down;
            case 3: return Vector2Int.up;
            default: return Vector2Int.zero;
        }
    }

    bool IsWalkableCell(int x, int y)
    {
        if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            return false;

        for (int wallX = 4; wallX < width - 1; wallX += 5)
        {
            int gapY = 1 + ((wallX * 3) % (height - 2));
            if (x == wallX && y != gapY)
                return false;
        }

        return true;
    }

    void ForceWalkable(GpuPathStateComponent[] stateData, GpuPathStateNextComponent[] stateNextData, Vector2Int cell)
    {
        int index = ToIndex(cell.x, cell.y);
        stateData[index].Walkable = 1u;
        stateNextData[index].Walkable = 1u;
    }

    int ToIndex(int x, int y)
    {
        return y * width + x;
    }

    Vector3 CellToWorld(int x, int y)
    {
        float offsetX = -(width - 1) * cellSize * 0.5f;
        float offsetZ = -(height - 1) * cellSize * 0.5f;
        return new Vector3(offsetX + x * cellSize, 0f, offsetZ + y * cellSize);
    }

    void EnsureGradient()
    {
        if (distanceGradient != null && distanceGradient.colorKeys.Length > 0)
            return;

        distanceGradient = new Gradient();
        distanceGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.2f, 0.3f, 1f), 0f),
                new GradientColorKey(new Color(0.4f, 0.9f, 1f), 0.35f),
                new GradientColorKey(new Color(1f, 0.9f, 0.25f), 0.7f),
                new GradientColorKey(new Color(1f, 0.35f, 0.2f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
    }

    void RequestStateSnapshot(bool applyVisuals, bool exportCpu)
    {
        if (_stateBuffer == null)
            return;

        _pendingStateReadback = _world.ReadbackAsync<GpuPathStateComponent>();
        _readbackPending = true;
        _applyVisualsAfterReadback = applyVisuals;
        _exportCpuAfterReadback = exportCpu;
    }

    void PollReadback()
    {
        if (!_readbackPending || !_pendingStateReadback.IsDone)
            return;

        _readbackPending = false;
        if (_pendingStateReadback.HasError || !_pendingStateReadback.TryGetData(out var data))
            return;

        EnsureSnapshotCapacity(data.Length);
        for (int i = 0; i < data.Length; i++)
            _cpuSnapshot[i] = data[i];

        if (_applyVisualsAfterReadback && enableDebugVisuals)
            ApplyVisuals(_cpuSnapshot);

        if (_exportCpuAfterReadback)
            ExportSnapshotToMiniEcs(_cpuSnapshot);
    }

    void EnsureSnapshotCapacity(int count)
    {
        if (_cpuSnapshot == null || _cpuSnapshot.Length != count)
            _cpuSnapshot = new GpuPathStateComponent[count];
    }

    void ExportSnapshotToMiniEcs(GpuPathStateComponent[] stateData)
    {
        if (_cpuPathEntities == null || _cpuPathEntities.Length != stateData.Length)
            _cpuPathEntities = new Entity[stateData.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = ToIndex(x, y);
                Entity entity = _cpuPathEntities[index];
                if (entity.Id == 0)
                {
                    entity = EntityManager.CreateEntity();
                    _cpuPathEntities[index] = entity;
                }

                EntityManager.Add(entity, new PathCellCpuComponent
                {
                    Coord = new Vector2Int(x, y),
                    Distance = stateData[index].Distance,
                    BestDirection = stateData[index].BestDirection,
                    Walkable = stateData[index].Walkable != 0u
                });

                if (exportPositionsToMiniEcs)
                {
                    EntityManager.Add(entity, new PositionComponent
                    {
                        Value = CellToWorld(x, y)
                    });
                }
            }
        }
    }

    void ResolveShaders()
    {
        initShader = GpuGeneratedComputeShaderLoader.Load<GpuPathInitKernel>() ?? initShader ?? pathfindingShader;
        relaxShader = GpuGeneratedComputeShaderLoader.Load<GpuPathRelaxKernel>() ?? relaxShader ?? pathfindingShader;
        commitShader = GpuGeneratedComputeShaderLoader.Load<GpuPathCommitKernel>() ?? commitShader ?? pathfindingShader;
    }

    void OnDestroy()
    {
        _readbackPending = false;
        if (_worldDebug != null)
            _worldDebug.World = null;
        _world?.Dispose();
    }
}
