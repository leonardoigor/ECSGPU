using UnityEngine;

namespace MiniECS.Rendering
{
    [CreateAssetMenu(menuName = "MiniECS/VAT Animation Data", fileName = "NewVatAnimationData")]
    public class VatAnimationData : ScriptableObject
    {
        public Texture2D positionTexture;
        public Texture2D normalTexture;
        public Mesh originalMesh;
        public Material animationMaterial;
        public AnimationClipInfo[] clips;
        public int vertexCount;
        public int totalFrames;

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
