using MiniECS;
using MiniECS.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace MiniECS.Physics
{
    /// <summary>
    /// Drop on a GameObject that also carries a Unity Collider (Box / Sphere /
    /// Capsule / convex MeshCollider). On awake it spawns an ECS entity with the
    /// matching ColliderComponent plus Position/Rotation, so the collider system
    /// can simulate it.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    [AddComponentMenu("MiniECS/Physics/Collider Authoring")]
    public class ColliderAuthoring : MonoBehaviour
    {
        public bool isTrigger = false;
        public byte layer = 0;
        public Collider sourceCollider;

        void Awake()
        {
            var col = sourceCollider != null ? sourceCollider : GetComponent<Collider>();
            if (col == null)
            {
                Debug.LogWarning($"[ColliderAuthoring] {name} has no Collider to convert.");
                return;
            }

            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new PositionComponent { Value = transform.position });
            EntityManager.Add(entity, new RotationComponent { Value = transform.rotation });
            EntityManager.Add(entity, Build(col, isTrigger));
        }

        public static ColliderComponent Build(Collider col, bool trigger)
        {
            var c = new ColliderComponent
            {
                IsTrigger = trigger ? (byte)1 : (byte)0,
                Layer = 0,
                MeshHullIndex = -1
            };

            if (col is SphereCollider sc)
            {
                c.Shape = ColliderShapeType.Sphere;
                c.CenterOffset = (float3)sc.center;
                c.Radius = sc.radius;
            }
            else if (col is BoxCollider bc)
            {
                c.Shape = ColliderShapeType.Box;
                c.CenterOffset = (float3)bc.center;
                c.HalfExtents = (float3)(bc.size * 0.5f);
            }
            else if (col is CapsuleCollider cap)
            {
                c.Shape = ColliderShapeType.Capsule;
                c.CenterOffset = (float3)cap.center;
                c.Radius = cap.radius;
                c.CapsuleHeight = cap.height;
                c.CapsuleAxis = (int)cap.direction; // 0 = X, 1 = Y, 2 = Z
            }
            else if (col is MeshCollider mc)
            {
                c.Shape = ColliderShapeType.ConvexMesh;
                c.MeshHullIndex = ConvexHullRegistry.GetOrCreate(mc.sharedMesh);
            }
            else
            {
                c.Shape = ColliderShapeType.Sphere;
                c.CenterOffset = float3.zero;
                c.Radius = col.bounds.extents.magnitude;
            }

            return c;
        }
    }
}
