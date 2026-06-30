namespace MiniECS.Rendering
{
    public static class VatAnimation
    {
        static ComponentPool<AnimationComponent> Pool => EntityManager.Pool<AnimationComponent>();

        public static void SetAnimation(int entity, int animIndex, float speed = 1f)
        {
            ref var anim = ref Pool.Get(entity);
            anim.AnimIndex = animIndex;
            anim.CurrentFrame = 0f;
            anim.Speed = speed;

            var modelPool = EntityManager.Pool<ModelIdComponent>();
            if (modelPool.Has(entity))
            {
                var vatData = ModelRegistry.GetVatData(modelPool.components[modelPool.GetIndex(entity)].Value);
                if (vatData != null && vatData.clips != null && vatData.clips.Length > 0)
                {
                    int clipIndex = UnityEngine.Mathf.Clamp(animIndex, 0, vatData.clips.Length - 1);
                    anim.FrameCount = vatData.clips[clipIndex].frameCount;
                    anim.FrameRate = vatData.clips[clipIndex].frameRate;
                }
            }
        }

        public static void SetAnimSpeed(int entity, float speed)
        {
            ref var anim = ref Pool.Get(entity);
            anim.Speed = speed;
        }

        public static void Pause(int entity)
        {
            ref var anim = ref Pool.Get(entity);
            anim.Speed = 0f;
        }

        public static void Resume(int entity)
        {
            ref var anim = ref Pool.Get(entity);
            if (anim.Speed == 0f)
                anim.Speed = 1f;
        }

        public static void SetFrame(int entity, float frame)
        {
            ref var anim = ref Pool.Get(entity);
            anim.CurrentFrame = frame;
        }
    }
}
