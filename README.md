# MiniECS

A lightweight, high-performance Entity Component System framework for Unity with GPU-instanced VAT (Vertex Animation Texture) rendering and automatic network synchronization via Netcode for GameObjects.

## Features

- **Zero-alloc component pools** — Components stored in contiguous arrays with O(1) add/remove
- **Up to 7-component queries** — Type-safe queries with Processor or foreach enumerator patterns
- **Job System integration** — Systems run as Burst-compiled IJobs or IJobParallelFor by default
- **GPU-instanced VAT rendering** — Render 20K+ animated characters via `CommandBuffer.DrawMeshInstancedIndirect`
- **4-level LOD** — Distance-based LOD with simplified shaders and frame quantization
- **Automatic network sync** — Mark components with `[Sync]` and snapshots replicate automatically
- **Role-based systems** — `IServerSystem` / `IClientSystem` markers for server-only or client-only logic

---

## Quick Start

### 1. Define a Component

```csharp
using MiniECS;

public struct HealthComponent : IComponentData
{
    public float Current;
    public float Max;
}
```

### 2. Create an Entity

```csharp
var entity = EntityManager.CreateEntity();
EntityManager.Add(entity, new HealthComponent { Current = 100f, Max = 100f });
```

### 3. Write a System

```csharp
using MiniECS;

public struct HealthRegenSystem : ISystem, IMainThreadSystem
{
    public void OnCreate(ref SystemContext ctx) { }

    public void OnUpdate(ref SystemContext ctx)
    {
        var pool = EntityManager.Pool<HealthComponent>();
        for (int i = 0; i < pool.entities.Count; i++)
        {
            ref var hp = ref pool.components[i];
            if (hp.Current < hp.Max)
                hp.Current += 10f * ctx.DeltaTime;
        }
    }

    public void OnDestroy(ref SystemContext ctx) { }
}
```

### 4. Bootstrap

```csharp
using MiniECS;
using MiniECS.Rendering;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
        EntityManager.Reset();
        ModelRegistry.Clear();
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(true, true); // server + client
    }

    void Update() => scheduler.Update(Time.deltaTime);
}
```

---

## Core Concepts

### Entity

An `Entity` is a lightweight readonly struct holding an integer ID. It does not store data — data lives in component pools.

```csharp
Entity e = EntityManager.CreateEntity();
Debug.Log(e.Id); // 1, 2, 3, ...
```

### Components

Components are plain structs implementing `IComponentData`. They are stored in type-indexed pools.

```csharp
public struct PositionComponent : IComponentData
{
    public Vector3 Value;
}

public struct TagComponent : IComponentData { }
```

**Adding/removing components:**

```csharp
var entity = EntityManager.CreateEntity();
EntityManager.Add(entity, new PositionComponent { Value = Vector3.up });
EntityManager.Add(entity, new TagComponent());

bool has = EntityManager.Has<PositionComponent>(entity.Id); // true
EntityManager.RemoveComponent<PositionComponent>(entity.Id);
```

### ComponentPool\<T\>

Each component type has a pool backed by a contiguous array. Access it via `EntityManager.Pool<T>()`:

```csharp
var pool = EntityManager.Pool<PositionComponent>();

// Direct array access (fast iteration)
for (int i = 0; i < pool.entities.Count; i++)
{
    int entityId = pool.entities[i];
    ref PositionComponent pos = ref pool.components[i];
    pos.Value += Vector3.forward * Time.deltaTime;
}

// Lookup by entity ID
if (pool.lookup.TryGetValue(entityId, out int index))
{
    ref var comp = ref pool.components[index];
}
```

### Queries

Queries iterate over entities that have specific component combinations.

**Using a Processor struct (zero-alloc):**

```csharp
public struct MoveProcessor : IProcessor<PositionComponent>
{
    public float Speed;

    public void Execute(Entity e, ref PositionComponent pos)
    {
        pos.Value += Vector3.forward * Speed;
    }
}

// Usage
var query = EntityManager.Query<PositionComponent>();
query.ForEach(new MoveProcessor { Speed = 5f });
```

**Using foreach enumerator:**

```csharp
var query = EntityManager.Query<PositionComponent, HealthComponent>();
foreach (var (entity, pos, hp) in query.ForEach())
{
    pos.Value += Vector3.forward * Time.deltaTime;
    hp.Current -= 10f * Time.deltaTime;
}
```

**Filtering out entities with a component:**

```csharp
// Only entities that have PositionComponent but NOT TagComponent
EntityManager.Query<PositionComponent>()
    .None<TagComponent>()
    .ForEach(new MoveProcessor { Speed = 5f });
```

**Multi-component Processor:**

```csharp
public struct DamageProcessor : IProcessor<HealthComponent, PositionComponent>
{
    public void Execute(Entity e, ref HealthComponent hp, ref PositionComponent pos)
    {
        if (hp.Current <= 0)
            Debug.Log($"Entity at {pos.Value} died!");
    }
}

EntityManager.Query<HealthComponent, PositionComponent>()
    .ForEach(new DamageProcessor());
```

### ValueRW — Mutable Component Access

By default, components in queries are read-only. Wrap a component type in `ValueRW<T>` to get mutable access via `.Value`:

```csharp
public struct BounceProcessor : IProcessor<ValueRW<PositionComponent>>
{
    public void Execute(Entity e, ref ValueRW<PositionComponent> pos)
    {
        pos.Value += Vector3.up * Mathf.Sin(Time.time) * 0.1f;
    }
}

EntityManager.Query<ValueRW<PositionComponent>>()
    .ForEach(new BounceProcessor());
```

---

## System Types

Systems are structs implementing `ISystem`. Marker interfaces control execution behavior:

| Interface | Behavior |
|-----------|----------|
| *(none)* | Runs as a Burst-compiled `IJob` (default, parallel-safe) |
| `IMainThreadSystem` | Runs on main thread, completes pending JobHandle first |
| `IParallelBatchSystem` | Runs as `IJobParallelFor` via `OnPrepare` + `ScheduleBatches` |
| `IServerSystem` | Only runs when scheduler is created with `server: true` |
| `IClientSystem` | Only runs when scheduler is created with `client: true` |

### Default System (Burst IJob)

```csharp
public struct GravitySystem : ISystem
{
    public void OnCreate(ref SystemContext ctx) { }

    public void OnUpdate(ref SystemContext ctx)
    {
        // Runs in a Burst-compiled job
        var pool = EntityManager.Pool<VelocityComponent>();
        var posPool = EntityManager.Pool<PositionComponent>();
        for (int i = 0; i < pool.entities.Count; i++)
        {
            int id = pool.entities[i];
            if (posPool.lookup.TryGetValue(id, out int pi))
                posPool.components[pi].Value += pool.components[i].Value * ctx.DeltaTime;
        }
    }

    public void OnDestroy(ref SystemContext ctx) { }
}
```

### Main Thread System

```csharp
public struct InputSystem : ISystem, IMainThreadSystem
{
    public void OnCreate(ref SystemContext ctx) { }

    public void OnUpdate(ref SystemContext ctx)
    {
        // Runs on main thread — safe for Unity API calls
        if (Input.GetKeyDown(KeyCode.Space))
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent { Value = Vector3.zero });
        }
    }

    public void OnDestroy(ref SystemContext ctx) { }
}
```

### Parallel Batch System (IJobParallelFor)

```csharp
using Unity.Collections;
using Unity.Jobs;

public struct ParallelMoveSystem : ISystem, IClientSystem, IParallelBatchSystem
{
    static NativeArray<Vector3> positions;

    public void OnCreate(ref SystemContext ctx) { }

    public void OnPrepare(ref SystemContext ctx, out int totalWork, out int batchSize)
    {
        var pool = EntityManager.Pool<PositionComponent>();
        positions = new NativeArray<Vector3>(pool.entities.Count, Allocator.TempJob);
        for (int i = 0; i < pool.entities.Count; i++)
            positions[i] = pool.components[i].Value;
        totalWork = pool.entities.Count;
        batchSize = 64;
    }

    public JobHandle ScheduleBatches(ref SystemContext ctx, int totalWork, int batchSize, JobHandle dep)
    {
        return new MoveJob { Positions = positions, Dt = ctx.DeltaTime }
            .Schedule(totalWork, batchSize, dep);
    }

    public void OnDestroy(ref SystemContext ctx) { if (positions.IsCreated) positions.Dispose(); }

    [Unity.Burst.BurstCompile]
    struct MoveJob : IJobParallelFor
    {
        public NativeArray<Vector3> Positions;
        public float Dt;
        public void Execute(int i) => Positions[i] += Vector3.forward * Dt;
    }
}
```

### SystemContext

Passed to every system update. Contains:

```csharp
public struct SystemContext
{
    public float DeltaTime;   // Frame delta time
    public bool IsServer;     // true if running as server
    public bool IsClient;     // true if running as client
}
```

---

## Rendering — VAT GPU Pipeline

The rendering module uses **Vertex Animation Textures (VAT)** to render thousands of animated characters via a single `CommandBuffer.DrawMeshInstancedIndirect` call per model.

### How It Works

1. **Bake**: Animation clips are sampled frame-by-frame, vertex positions/normals are written into textures
2. **Register**: `VatAuthoring` or `VatGpuRenderSpawner` registers mesh + material + `VatAnimationData` via `ModelRegistry`
3. **Collect**: `RenderCollectSystem` frustum-culls, computes LOD levels, quantizes animation frames
4. **Render**: `RenderSubmitSystem` uploads instance data to GPU and issues `DrawMeshInstancedIndirect`

### VatAnimationData

ScriptableObject containing all data needed to render animated characters:

| Field | Description |
|-------|-------------|
| `positionTexture` | Texture storing per-vertex positions per frame |
| `normalTexture` | Texture storing per-vertex normals per frame |
| `originalMesh` | The baked mesh (vertex count must match texture width) |
| `animationMaterial` | Material using VatPBR/VatSimple/VatUnlit shader |
| `clips[]` | Array of `AnimationClipInfo` (name, startFrame, frameCount, frameRate) |
| `vertexCount` | Number of vertices (width of VAT textures) |
| `totalFrames` | Total animation frames (height of VAT textures) |
| `lodLowMaterial` | LOD2 material (VatSimple — simplified PBR) |
| `lodUltraMaterial` | LOD3 material (VatUnlit — unlit, cheapest) |
| `lod2Distance` / `lod3Distance` / `cullDistance` | LOD transition distances in meters |

### VAT Baker

Open via **Tools > MiniECS > VAT Baker**.

1. Click **Scan Project** to find skinned models
2. Match animation clips to each model (auto-match or manual)
3. Configure frame rate, LOD distances
4. Click **Bake Selected** — creates VatAnimationData asset with all textures, materials, and LOD meshes

### Shaders

| Shader | Use Case | Features |
|--------|----------|----------|
| `Custom/VatPBR` | LOD1 — full quality | PBR lighting, normal maps, metallic/smoothness, emission, fresnel, shadow caster |
| `Custom/VatSimple` | LOD2 — medium quality | Diffuse + basic specular, no normal maps, shadow caster |
| `Custom/VatUnlit` | LOD3 — far distance | Unlit albedo only, no shadows |
| `Custom/VatInstancing` | Fallback | Simple diffuse with hardcoded light |

All shaders use `SV_VertexID` to look up vertex positions from the VAT texture. **Do not use decimated meshes** — the vertex count must match the original baked mesh.

### LOD System

Distance-based 4-level LOD runs in `RenderCollectSystem.OnPrepare`:

| LOD | Condition | Shader | Animation Rate |
|-----|-----------|--------|----------------|
| Cull | `distSqr > cullDistance²` | — | Not rendered |
| LOD3 | `distSqr > lod3Distance²` | VatUnlit | Every 4 frames + jitter |
| LOD2 | `distSqr > lod2Distance²` | VatSimple | Every 2 frames + jitter |
| LOD1 | Close | VatPBR | Every frame |

Anti-robot jitter: `entity.Id % updateInterval * 0.5f` offset prevents synchronized frame updates across entities.

### VatAnimation API

Control animations at runtime:

```csharp
using MiniECS.Rendering;

// Switch to animation clip index 2, at 1.5x speed
VatAnimation.SetAnimation(entityId, animIndex: 2, speed: 1.5f);

// Change speed
VatAnimation.SetAnimSpeed(entityId, 0.5f);

// Pause / Resume
VatAnimation.Pause(entityId);
VatAnimation.Resume(entityId);

// Jump to specific frame
VatAnimation.SetFrame(entityId, 10f);
```

### Per-Instance Colors

Add a `ColorComponent` to any entity to tint its render:

```csharp
EntityManager.Add(entity, new ColorComponent { Value = Color.red });
```

Colors are passed to the shader via the `InstanceData.color` field in the StructuredBuffer. Default is white if no `ColorComponent` is present.

### Spawning Entities

```csharp
using MiniECS;
using MiniECS.Rendering;
using UnityEngine;

public class Spawner : MonoBehaviour
{
    public VatAnimationData vatModel;
    public int count = 10000;

    void Start()
    {
        int modelId = ModelRegistry.Register(
            vatModel.originalMesh,
            vatModel.animationMaterial,
            vatModel
        );

        for (int i = 0; i < count; i++)
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent
            {
                Value = Random.insideUnitSphere * 50f
            });
            EntityManager.Add(entity, new RotationComponent
            {
                Value = Random.rotationUniform
            });
            EntityManager.Add(entity, new ModelIdComponent { Value = modelId });
            EntityManager.Add(entity, new AnimationComponent
            {
                AnimIndex = 0,
                CurrentFrame = Random.Range(0f, 30f),
                Speed = Random.Range(0.8f, 1.2f),
                FrameCount = vatModel.clips[0].frameCount,
                FrameRate = vatModel.clips[0].frameRate
            });
            EntityManager.Add(entity, new ColorComponent
            {
                Value = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.5f, 1f)
            });
        }
    }
}
```

---

## Netcode — Network Sync

MiniECS includes automatic network synchronization via Unity's Netcode for GameObjects.

### [Sync] Attribute

Mark any `IComponentData` struct with `[Sync]` to enable automatic replication:

```csharp
using MiniECS;
using UnityEngine;

[Sync]
public struct SyncPosition : IComponentData
{
    public Vector3 Value;
}

[Sync]
public struct SyncHealth : IComponentData
{
    public float Current;
    public float Max;
}
```

### How It Works

1. **Server**: `EcsNetworkManager` collects all entities with `[Sync]` components, serializes them into a snapshot, and broadcasts to all clients every `SyncConfig.SyncInterval` (default 0.1s)
2. **Client**: Receives the snapshot, creates/updates local entities via `SyncEntityRegistry` (server ID → local ID mapping), and removes entities that no longer exist on the server

### Setup

1. Add `EcsNetworkManager` component to a GameObject in the scene
2. Add `ServerBootstrap` to a server GameObject, `ClientBootstrap` to client GameObjects
3. Both bootstraps call `EcsNetworkManager.TryInitialize()` automatically

### Supported Types

Serialization supports: `int`, `uint`, `float`, `double`, `bool`, `byte`, `sbyte`, `short`, `ushort`, `long`, `ulong`, `Vector2`, `Vector3`, `Vector2Int`, `Vector3Int`, `Quaternion`, `Color`, `string`, enums, and custom `vector2`/`vector3` types.

### SyncConfig

```csharp
using MiniECS.Netcode;

SyncConfig.SyncInterval = 0.1f;  // 10 snapshots per second
SyncConfig.Delivery = NetworkDelivery.ReliableFragmentedSequenced;
```

---

## Authoring

### EcsAuthoring\<T\>

Abstract MonoBehaviour base for baking components into entities:

```csharp
using MiniECS;
using MiniECS.Authoring;

public class PlayerAuthoring : EcsAuthoring<HealthComponent>
{
    public float maxHealth = 100f;

    protected override HealthComponent Bake()
    {
        return new HealthComponent { Current = maxHealth, Max = maxHealth };
    }
}
```

### VatAuthoring

Specialized authoring for VAT-rendered characters:

```csharp
using MiniECS.Rendering.Authoring;

public class EnemyAuthoring : MonoBehaviour
{
    public VatAnimationData vatData;

    void Awake()
    {
        // Registers mesh + material + VAT data, creates entity with
        // PositionComponent, RotationComponent, ModelIdComponent, AnimationComponent
        var authoring = gameObject.AddComponent<VatAuthoring>();
        authoring.vatData = vatData;
    }
}
```

### RenderAuthoring

For static (non-animated) instanced meshes:

```csharp
using MiniECS.Rendering.Authoring;

// Attach to a GameObject with MeshFilter + MeshRenderer
// Automatically creates entity with Position, Rotation, ModelId
var authoring = gameObject.AddComponent<RenderAuthoring>();
```

---

## Bootstrap

### ServerBootstrap

```csharp
using MiniECS;
using MiniECS.Rendering;
using UnityEngine;

public class ServerBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
        EntityManager.Reset();
        ModelRegistry.Clear();
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(server: true, client: false);
    }

    void FixedUpdate() => scheduler.Update(Time.fixedDeltaTime);
}
```

### ClientBootstrap

```csharp
using MiniECS;
using MiniECS.Rendering;
using UnityEngine;

public class ClientBootstrap : MonoBehaviour
{
    SystemScheduler scheduler;

    void Awake()
    {
        EntityManager.Reset();
        ModelRegistry.Clear();
        SystemRegistry.RegisterAll();
        scheduler = new SystemScheduler(server: false, client: true);
    }

    void Update() => scheduler.Update(Time.deltaTime);
}
```

---

## Editor Tools

### EcsSystemWindow

**Tools > MiniECS > System Viewer** — Shows all registered systems, their execution type (Main/Parallel/Batch), and per-frame timing.

### EcsComponentWindow

**Tools > MiniECS > Component Viewer** — Shows all component pools and entity counts per type.

---

## API Reference

### EntityManager (static)

| Method | Description |
|--------|-------------|
| `CreateEntity()` | Creates a new entity, returns `Entity` |
| `Add<T>(entity, component)` | Adds component to entity |
| `Has<T>(entityId)` | Checks if entity has component |
| `RemoveComponent<T>(entityId)` | Removes component from entity |
| `Pool<T>()` | Returns `ComponentPool<T>` for direct access |
| `Query<T1..T7>()` | Returns typed `Query` for iteration |
| `GetActiveEntityCount()` | Returns count of living entities |
| `Reset()` | Clears all entities and pools |

### ComponentPool\<T\>

| Member | Description |
|--------|-------------|
| `entities` | `List<int>` of entity IDs in this pool |
| `components` | `T[]` contiguous component array |
| `lookup` | `Dictionary<int, int>` entity ID → array index |
| `Add(id, component)` | Adds or updates component |
| `Remove(id)` | Removes component (swap-back) |
| `Get(id)` | Returns `ref T` component |
| `Has(id)` | Checks existence |

### Query\<T1..T7\>

| Method | Description |
|--------|-------------|
| `ForEach(processor)` | Iterates with processor struct |
| `ForEach()` | Returns foreach enumerator |
| `None<T>()` | Excludes entities with component T |
