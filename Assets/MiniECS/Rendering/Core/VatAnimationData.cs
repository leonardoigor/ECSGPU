using UnityEngine;
using Unity.Mathematics;
using MiniECS.Physics;

namespace MiniECS.Rendering
{
    [CreateAssetMenu(menuName = "MiniECS/VAT Animation Data", fileName = "NewVatAnimationData")]
    public class VatAnimationData : ScriptableObject
    {
        [Header("VAT Textures")]
        public Texture2D positionTexture;
        public Texture2D normalTexture;

        [Header("Mesh & Material")]
        public Mesh originalMesh;
        public Material animationMaterial;

        [Header("Animation")]
        public AnimationClipInfo[] clips;
        public int vertexCount;
        public int totalFrames;

        [Header("LOD Materials (created by baker)")]
        public Material lodLowMaterial;
        public Material lodUltraMaterial;

        [Header("LOD Distances (meters)")]
        public float lod2Distance = 20f;
        public float lod3Distance = 50f;
        public float cullDistance = 100f;

        [System.NonSerialized] public float sqrLod2Distance;
        [System.NonSerialized] public float sqrLod3Distance;
        [System.NonSerialized] public float sqrCullDistance;

        public void ComputeSqrDistances()
        {
            sqrLod2Distance = lod2Distance * lod2Distance;
            sqrLod3Distance = lod3Distance * lod3Distance;
            sqrCullDistance = cullDistance * cullDistance;
        }

        [Header("Original Material (copied by baker)")]
        public Texture2D albedoTexture;
        public Texture2D normalMapTexture;
        public Texture2D metallicTexture;
        public Texture2D emissionTexture;
        public Texture2D occlusionTexture;
        public Color albedoTint = Color.white;
        public Color emissionColor = Color.black;
        public float metallic;
        public float smoothness = 0.5f;
        public float normalScale = 1f;
        public float occlusionStrength = 1f;

        [Header("Baked Collider (captured by VAT Baker from source Collider)")]
        public VatColliderInfo colliderInfo;

        public bool HasCollider => colliderInfo.hasCollider;

        /// <summary>
        /// Builds a ColliderComponent from the baked collider descriptor.
        /// For ConvexMesh with no precomputed index, pass the runtime mesh
        /// (e.g. originalMesh) so its convex hull is registered on demand.
        /// </summary>
        public ColliderComponent BuildColliderComponent(Mesh runtimeMesh = null)
        {
            var c = new ColliderComponent
            {
                IsTrigger = colliderInfo.isTrigger ? (byte)1 : (byte)0,
                Layer = (byte)colliderInfo.layer,
                Shape = colliderInfo.shape,
                HalfExtents = colliderInfo.halfExtents,
                Radius = colliderInfo.radius,
                CapsuleHeight = colliderInfo.capsuleHeight,
                CapsuleAxis = colliderInfo.capsuleAxis,
                CenterOffset = colliderInfo.centerOffset,
                MeshHullIndex = colliderInfo.meshHullIndex
            };
            if (c.Shape == ColliderShapeType.ConvexMesh && c.MeshHullIndex < 0 && runtimeMesh != null)
                c.MeshHullIndex = ConvexHullRegistry.GetOrCreate(runtimeMesh);
            return c;
        }

        void OnValidate()
        {
            if (positionTexture != null)
                vertexCount = positionTexture.width;
        }
    }

    /// <summary>
    /// Serializable snapshot of a Unity Collider, captured by the VAT Baker from
    /// the source GameObject so VAT-spawned entities can be given an equivalent
    /// ColliderComponent at runtime.
    /// </summary>
    [System.Serializable]
    public struct VatColliderInfo
    {
        public bool hasCollider;
        public ColliderShapeType shape;
        public float3 halfExtents;
        public float radius;
        public float capsuleHeight;
        public int capsuleAxis;     // 0 = X, 1 = Y, 2 = Z
        public float3 centerOffset; // local offset from the entity (root) transform
        public int meshHullIndex;   // ConvexMesh only; -1 => resolve at runtime from originalMesh
        public bool isTrigger;
        public int layer;
    }

    [System.Serializable]
    public struct AnimationClipInfo
    {
        public string name;
        public int startFrame;
        public int frameCount;
        public float frameRate;
    }
}
