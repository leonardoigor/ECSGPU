using UnityEngine;

namespace MiniECS.Rendering
{
    public static class VatAnimationDataExtensions
    {
        public static int GetAnimationCount(this VatAnimationData vat)
        {
            if (vat == null || vat.clips == null) return 0;
            return vat.clips.Length;
        }

        public static int GetValidAnimationIndex(this VatAnimationData vat, int animIndex)
        {
            int count = vat.GetAnimationCount();
            if (count <= 0) return 0;
            if (animIndex < 0) return 0;
            if (animIndex >= count) return count - 1;
            return animIndex;
        }

        public static AnimationClipInfo GetAnimationInfo(this VatAnimationData vat, int animIndex)
        {
            if (vat == null)
                return default;

            int count = vat.GetAnimationCount();
            if (count > 0)
            {
                int idx = vat.GetValidAnimationIndex(animIndex);
                return vat.clips[idx];
            }

            return new AnimationClipInfo
            {
                name = "Static",
                startFrame = 0,
                frameCount = Mathf.Max(1, vat.totalFrames),
                frameRate = 30f
            };
        }

        public static int GetFrameCount(this VatAnimationData vat, int animIndex)
        {
            var info = vat.GetAnimationInfo(animIndex);
            return Mathf.Max(1, info.frameCount);
        }

        public static float GetFrameRate(this VatAnimationData vat, int animIndex, float fallback = 30f)
        {
            var info = vat.GetAnimationInfo(animIndex);
            return info.frameRate > 0f ? info.frameRate : fallback;
        }

        public static int GetStartFrame(this VatAnimationData vat, int animIndex)
        {
            var info = vat.GetAnimationInfo(animIndex);
            return info.startFrame;
        }
    }
}

