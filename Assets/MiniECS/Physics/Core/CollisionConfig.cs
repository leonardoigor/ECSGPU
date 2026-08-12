namespace MiniECS.Physics
{
    /// <summary>
    /// Global tuning for the collider system. Flip RunCollidersOnGpu to move the
    /// broad/narrow phase onto the GPU (ECSGPU.GpuEcs) instead of the CPU (MiniECS).
    /// </summary>
    public static class CollisionConfig
    {
        public static bool RunCollidersOnGpu = false;
        public static float BroadPhaseCellSize = 4f;
        public static int MaxContactsPerEntity = 8;
        public static bool ResolvePenetration = true;
        public static bool GenerateEvents = true;
    }
}
