namespace MiniECS.Rendering
{
    public struct AnimationUpdateSystem : ISystem, IClientSystem, IMainThreadSystem
    {
        public void OnCreate(ref SystemContext ctx) { }

        public void OnUpdate(ref SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            var pool = EntityManager.Pool<AnimationComponent>();

            for (int i = 0; i < pool.entities.Count; i++)
            {
                var anim = pool.components[i];
                float frameRate = anim.FrameRate > 0f ? anim.FrameRate : 30f;
                anim.CurrentFrame += dt * anim.Speed * frameRate;
                if (anim.FrameCount > 0 && anim.CurrentFrame >= anim.FrameCount)
                    anim.CurrentFrame %= anim.FrameCount;
                pool.components[i] = anim;
            }
        }

        public void OnDestroy(ref SystemContext ctx) { }
    }
}
