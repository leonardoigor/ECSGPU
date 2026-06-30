using UnityEngine;

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

        void OnValidate()
        {
            if (positionTexture != null)
                vertexCount = positionTexture.width;
        }
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
