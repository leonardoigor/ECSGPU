using Unity.Collections;

namespace MiniECS.Rendering
{
    public struct AnimationUpdateSystem : ISystem, IClientSystem, IMainThreadSystem
    {
        public void OnCreate(ref SystemContext ctx) { }

        public void OnUpdate(ref SystemContext ctx)
        {
            float dt = ctx.DeltaTime;
            var pool = EntityManager.Pool<AnimationComponent>();
            var active = AoISystem.GetActiveEntities();

            if (!active.IsCreated) return;

            for (int i = 0; i < active.Length; i++)
            {
                int id = active[i];
                if (!pool.Has(id)) continue;

                int idx = pool.GetIndex(id);
                var anim = pool.components[idx];
                float frameRate = anim.FrameRate > 0f ? anim.FrameRate : 30f;
                anim.CurrentFrame += dt * anim.Speed * frameRate;
                if (anim.FrameCount > 0 && anim.CurrentFrame >= anim.FrameCount)
                    anim.CurrentFrame %= anim.FrameCount;
                pool.components[idx] = anim;
            }
        }

        public void OnDestroy(ref SystemContext ctx) { }
    }
}
