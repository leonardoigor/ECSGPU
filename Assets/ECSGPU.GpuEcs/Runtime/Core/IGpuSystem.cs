namespace ECSGPU.GpuEcs
{
    public interface IGpuSystem
    {
        void OnCreate(GpuWorld world);
        void OnUpdate(GpuWorld world, float deltaTime);
        void OnDestroy();
    }
}
